using System.Net.Http;
using System.Net.Http.Headers;
using System.Runtime.CompilerServices;
using System.Text;
using System.Text.Json;
using WPFAIAssistant.Agents;
using WPFAIAssistant.Models;

namespace WPFAIAssistant.Services
{
    public class DeepSeekAIService : IAIService
    {
        private readonly IAgentRegistry _agentRegistry;

        public DeepSeekAIService(IAgentRegistry agentRegistry)
        {
            _agentRegistry = agentRegistry;
        }

        public async IAsyncEnumerable<string> StreamChatAsync(
            string userMessage,
            string modelId,
            string apiKey,
            string baseUrl,
            IReadOnlyList<ChatMessage> history,
            Func<string, Task>? onThinkingChunk = null,
            string? systemPromptExtra = null,
            [EnumeratorCancellation] CancellationToken cancellationToken = default)
        {
            // deepseek-v4-pro supports thinking mode (via request params) but does NOT support
            // tool/function calling when thinking is enabled. Skip tool resolution for it.
            // deepseek-v4-flash is the standard model and supports tool calls.
            // Legacy names: deepseek-reasoner → thinking, deepseek-chat → standard.
            bool isReasoningModel = modelId.Contains("pro", StringComparison.OrdinalIgnoreCase)
                                 || modelId.Contains("reasoner", StringComparison.OrdinalIgnoreCase);

            var baseSystem = "You are a helpful AI assistant. Format your responses using Markdown when appropriate. " +
                             "You have access to tools/functions — use them whenever the user asks about the local file system.";
            var systemPrompt = string.IsNullOrWhiteSpace(systemPromptExtra)
                ? baseSystem
                : baseSystem + "\n\n" + systemPromptExtra;

            // Build the plain message list we'll send to the streaming endpoint
            var messages = BuildConversationMessages(systemPrompt, history, userMessage);

            if (!isReasoningModel)
            {
                // ── 1. Run tool/function calls via OpenAI-compatible non-streaming pass ──────
                messages = await ResolveToolCallsAsync(messages, modelId, apiKey, baseUrl, cancellationToken);
            }

            // ── Stream via raw HTTP so we can read reasoning_content ───────────
            await foreach (var token in StreamRawAsync(
                messages, modelId, apiKey, baseUrl,
                onThinkingChunk, isReasoningModel, cancellationToken))
            {
                yield return token;
            }
        }

        private async Task<List<Dictionary<string, object?>>> ResolveToolCallsAsync(
            List<Dictionary<string, object?>> messages,
            string modelId,
            string apiKey,
            string baseUrl,
            CancellationToken cancellationToken)
        {
            var tools = _agentRegistry.GetToolDefinitions();
            if (tools.Count == 0)
                return messages;

            var toolSpecs = tools.Select(t => new Dictionary<string, object?>
            {
                ["type"] = "function",
                ["function"] = new Dictionary<string, object?>
                {
                    ["name"] = t.Name,
                    ["description"] = t.Description,
                    ["parameters"] = t.ParametersSchema
                }
            }).ToList();

            // Resolve iterative tool calls; keep final assistant response for streaming phase.
            for (int i = 0; i < 6; i++)
            {
                var requestBody = JsonSerializer.Serialize(new
                {
                    model = modelId,
                    messages,
                    stream = false,
                    max_tokens = 16000,
                    temperature = 0.7,
                    tools = toolSpecs,
                    tool_choice = "auto"
                });

                using var completionDoc = await PostCompletionAsync(baseUrl, apiKey, requestBody, cancellationToken);
                var assistantMessage = completionDoc.RootElement
                    .GetProperty("choices")[0]
                    .GetProperty("message");

                if (!assistantMessage.TryGetProperty("tool_calls", out var toolCalls) ||
                    toolCalls.ValueKind != JsonValueKind.Array ||
                    toolCalls.GetArrayLength() == 0)
                {
                    break;
                }

                messages.Add(new Dictionary<string, object?>
                {
                    ["role"] = "assistant",
                    ["tool_calls"] = JsonSerializer.Deserialize<object>(toolCalls.GetRawText())
                });

                foreach (var call in toolCalls.EnumerateArray())
                {
                    var callId = call.GetProperty("id").GetString() ?? string.Empty;
                    var function = call.GetProperty("function");
                    var toolName = function.GetProperty("name").GetString() ?? string.Empty;
                    var argumentsJson = function.TryGetProperty("arguments", out var argsEl)
                        ? argsEl.GetString() ?? "{}"
                        : "{}";

                    string toolOutput;
                    try
                    {
                        using var argsDoc = JsonDocument.Parse(string.IsNullOrWhiteSpace(argumentsJson) ? "{}" : argumentsJson);
                        toolOutput = _agentRegistry.TryInvoke(toolName, argsDoc.RootElement)
                            ?? $"[Error] Tool not found: {toolName}";
                    }
                    catch (Exception ex)
                    {
                        toolOutput = $"[Error] Tool execution failed: {ex.Message}";
                    }

                    messages.Add(new Dictionary<string, object?>
                    {
                        ["role"] = "tool",
                        ["tool_call_id"] = callId,
                        ["content"] = toolOutput
                    });
                }
            }

            return messages;
        }

        // ── Raw SSE streaming ────────────────────────────────────────────────────
        private static async IAsyncEnumerable<string> StreamRawAsync(
            List<Dictionary<string, object?>> messages,
            string modelId,
            string apiKey,
            string baseUrl,
            Func<string, Task>? onThinkingChunk,
            bool enableThinking,
            [EnumeratorCancellation] CancellationToken cancellationToken)
        {
            var url = baseUrl.TrimEnd('/') + "/chat/completions";

            // deepseek-v4-pro: enable thinking mode via API params.
            // temperature must be 1 when thinking is enabled (API requirement).
            string requestBody;
            if (enableThinking)
            {
                requestBody = JsonSerializer.Serialize(new
                {
                    model = modelId,
                    messages,
                    stream = true,
                    max_tokens = 16000,
                    temperature = 1,
                    thinking = new { type = "enabled" },
                    reasoning_effort = "high",
                });
            }
            else
            {
                requestBody = JsonSerializer.Serialize(new
                {
                    model = modelId,
                    messages,
                    stream = true,
                    max_tokens = 16000,
                    temperature = 0.7,
                });
            }

            using var http = new HttpClient();
            http.DefaultRequestHeaders.Authorization =
                new AuthenticationHeaderValue("Bearer", apiKey);

            using var request = new HttpRequestMessage(HttpMethod.Post, url)
            {
                Content = new StringContent(requestBody, Encoding.UTF8, "application/json")
            };

            using var response = await http.SendAsync(
                request, HttpCompletionOption.ResponseHeadersRead, cancellationToken);

            response.EnsureSuccessStatusCode();

            using var stream = await response.Content.ReadAsStreamAsync(cancellationToken);
            using var reader = new System.IO.StreamReader(stream);

            while (!reader.EndOfStream && !cancellationToken.IsCancellationRequested)
            {
                var line = await reader.ReadLineAsync(cancellationToken);
                if (string.IsNullOrWhiteSpace(line)) continue;
                if (!line.StartsWith("data:")) continue;

                var json = line["data:".Length..].Trim();
                if (json == "[DONE]") break;

                string? reasoning = null;
                string? content = null;
                try
                {
                    using var doc = JsonDocument.Parse(json);
                    var delta = doc.RootElement
                        .GetProperty("choices")[0]
                        .GetProperty("delta");

                    // DeepSeek V4 uses "thinking"; legacy R1/reasoner used "reasoning_content"
                    if (delta.TryGetProperty("thinking", out var tk) &&
                        tk.ValueKind == JsonValueKind.String)
                    {
                        reasoning = tk.GetString();
                    }
                    else if (delta.TryGetProperty("reasoning_content", out var rc) &&
                        rc.ValueKind == JsonValueKind.String)
                    {
                        reasoning = rc.GetString();
                    }

                    if (delta.TryGetProperty("content", out var ct) &&
                        ct.ValueKind == JsonValueKind.String)
                    {
                        content = ct.GetString();
                    }
                }
                catch
                {
                    continue;
                }

                if (!string.IsNullOrEmpty(reasoning) && onThinkingChunk != null)
                    await onThinkingChunk(reasoning);

                if (!string.IsNullOrEmpty(content))
                    yield return content;
            }
        }

        public async Task<IReadOnlyList<string>> GetAvailableModelsAsync(string apiKey, string baseUrl)
        {
            return await Task.FromResult<IReadOnlyList<string>>(new List<string>
            {
                "deepseek-v4-flash",
                "deepseek-v4-pro",
            });
        }

        private static List<Dictionary<string, object?>> BuildConversationMessages(
            string systemPrompt,
            IReadOnlyList<ChatMessage> history,
            string userMessage)
        {
            var messages = new List<Dictionary<string, object?>>
            {
                new()
                {
                    ["role"] = "system",
                    ["content"] = systemPrompt
                }
            };

            foreach (var msg in history)
            {
                var role = msg.Role == MessageRole.User ? "user" : "assistant";
                messages.Add(new Dictionary<string, object?>
                {
                    ["role"] = role,
                    ["content"] = msg.Content
                });
            }

            messages.Add(new Dictionary<string, object?>
            {
                ["role"] = "user",
                ["content"] = userMessage
            });

            return messages;
        }

        private static async Task<JsonDocument> PostCompletionAsync(
            string baseUrl,
            string apiKey,
            string body,
            CancellationToken cancellationToken)
        {
            var url = baseUrl.TrimEnd('/') + "/chat/completions";

            using var http = new HttpClient();
            http.DefaultRequestHeaders.Authorization =
                new AuthenticationHeaderValue("Bearer", apiKey);

            using var request = new HttpRequestMessage(HttpMethod.Post, url)
            {
                Content = new StringContent(body, Encoding.UTF8, "application/json")
            };

            using var response = await http.SendAsync(request, cancellationToken);
            response.EnsureSuccessStatusCode();

            var json = await response.Content.ReadAsStringAsync(cancellationToken);
            return JsonDocument.Parse(json);
        }
    }
}
