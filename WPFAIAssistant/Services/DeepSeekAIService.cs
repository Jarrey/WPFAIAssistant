using Microsoft.Extensions.AI;
using OpenAI;
using System.ClientModel;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Runtime.CompilerServices;
using System.Text;
using System.Text.Json;
using WPFAIAssistant.Agents;
using WPFAIAssistant.Models;
using MEAIChatMessage = Microsoft.Extensions.AI.ChatMessage;
using AppChatMessage = WPFAIAssistant.Models.ChatMessage;

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
            IReadOnlyList<AppChatMessage> history,
            Func<string, Task>? onThinkingChunk = null,
            string? systemPromptExtra = null,
            [EnumeratorCancellation] CancellationToken cancellationToken = default)
        {
            // deepseek-v4-pro / reasoner: thinking mode active, tool calling not supported by API.
            // deepseek-v4-flash: standard model, supports tool calls.
            bool isReasoningModel = modelId.Contains("pro", StringComparison.OrdinalIgnoreCase)
                                 || modelId.Contains("reasoner", StringComparison.OrdinalIgnoreCase);

            var baseSystem = "You are a helpful AI assistant. Format your responses using Markdown when appropriate. " +
                             "You have access to tools/functions — use them whenever the user asks about the local file system.";
            var systemPrompt = string.IsNullOrWhiteSpace(systemPromptExtra)
                ? baseSystem
                : baseSystem + "\n\n" + systemPromptExtra;

            // Build Microsoft.Extensions.AI ChatMessage list
            var meaiMessages = BuildMeaiMessages(systemPrompt, history, userMessage);

            if (!isReasoningModel)
            {
                // ── Tool call resolution via IChatClient + UseFunctionInvocation middleware ──
                meaiMessages = await ResolveToolCallsViaMeaiAsync(
                    meaiMessages, modelId, apiKey, baseUrl, cancellationToken);
            }

            // ── Streaming via raw SSE to capture DeepSeek reasoning_content / thinking ──────
            // IChatClient.GetStreamingResponseAsync does not surface DeepSeek-specific
            // reasoning_content/thinking fields, so we keep the raw SSE streaming pass.
            var rawMessages = ConvertMeaiToRaw(meaiMessages);
            await foreach (var token in StreamRawAsync(
                rawMessages, modelId, apiKey, baseUrl,
                onThinkingChunk, isReasoningModel, cancellationToken))
            {
                yield return token;
            }
        }

        // ── Build Microsoft.Extensions.AI messages ────────────────────────────────────────
        private static List<MEAIChatMessage> BuildMeaiMessages(
            string systemPrompt,
            IReadOnlyList<AppChatMessage> history,
            string userMessage)
        {
            var messages = new List<MEAIChatMessage>
            {
                new(ChatRole.System, systemPrompt)
            };

            foreach (var msg in history)
            {
                var role = msg.Role == MessageRole.User ? ChatRole.User : ChatRole.Assistant;
                messages.Add(new(role, msg.Content));
            }

            messages.Add(new(ChatRole.User, userMessage));
            return messages;
        }

        // ── Tool resolution via IChatClient (Microsoft.Extensions.AI) ─────────────────────
        private async Task<List<MEAIChatMessage>> ResolveToolCallsViaMeaiAsync(
            List<MEAIChatMessage> messages,
            string modelId,
            string apiKey,
            string baseUrl,
            CancellationToken cancellationToken)
        {
            var aiFunctions = _agentRegistry.GetAIFunctions();
            if (aiFunctions.Count == 0)
                return messages;

            // Build IChatClient: OpenAI ChatClient → AsIChatClient → UseFunctionInvocation
            var chatClient = BuildMeaiChatClient(modelId, apiKey, baseUrl, aiFunctions);

            var options = new ChatOptions
            {
                MaxOutputTokens = 16000,
                Temperature = 0.7f,
                Tools = [.. aiFunctions],
                ToolMode = ChatToolMode.Auto,
            };

            // GetResponseAsync with UseFunctionInvocation handles the full tool-call loop
            var response = await chatClient.GetResponseAsync(messages, options, cancellationToken);

            // Append all response messages (tool calls + results + final assistant) to history
            messages.AddRange(response.Messages);
            return messages;
        }

        private static IChatClient BuildMeaiChatClient(
            string modelId,
            string apiKey,
            string baseUrl,
            IReadOnlyList<AIFunction> tools)
        {
            var openAiOptions = new OpenAIClientOptions
            {
                Endpoint = new Uri(baseUrl.TrimEnd('/') + "/")
            };

            var inner = new OpenAIClient(new ApiKeyCredential(apiKey), openAiOptions)
                .GetChatClient(modelId)
                .AsIChatClient();

            // UseFunctionInvocation middleware: auto-invokes local AIFunctions when model requests them
            return inner
                .AsBuilder()
                .UseFunctionInvocation()
                .Build();
        }

        // ── Raw SSE streaming (preserves DeepSeek-specific reasoning_content/thinking) ────
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

            string requestBody;
            if (enableThinking)
            {
                requestBody = JsonSerializer.Serialize(new
                {
                    model = modelId,
                    messages,
                    stream = true,
                    max_tokens = 16000,
                    temperature = 1,          // required when thinking enabled
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
                    if (delta.TryGetProperty("thinking", out var tk) && tk.ValueKind == JsonValueKind.String)
                        reasoning = tk.GetString();
                    else if (delta.TryGetProperty("reasoning_content", out var rc) && rc.ValueKind == JsonValueKind.String)
                        reasoning = rc.GetString();

                    if (delta.TryGetProperty("content", out var ct) && ct.ValueKind == JsonValueKind.String)
                        content = ct.GetString();
                }
                catch { continue; }

                if (!string.IsNullOrEmpty(reasoning) && onThinkingChunk != null)
                    await onThinkingChunk(reasoning);

                if (!string.IsNullOrEmpty(content))
                    yield return content;
            }
        }

        // ── Convert Microsoft.Extensions.AI messages → raw dict list for SSE pass ─────────
        private static List<Dictionary<string, object?>> ConvertMeaiToRaw(
            List<MEAIChatMessage> messages)
        {
            var result = new List<Dictionary<string, object?>>();
            foreach (var m in messages)
            {
                // Skip tool call/result messages — they've already been resolved
                if (m.Role == ChatRole.Tool) continue;

                var role = m.Role == ChatRole.System    ? "system"
                         : m.Role == ChatRole.Assistant ? "assistant"
                         : "user";

                result.Add(new Dictionary<string, object?>
                {
                    ["role"]    = role,
                    ["content"] = m.Text ?? string.Empty
                });
            }
            return result;
        }

        public async Task<IReadOnlyList<string>> GetAvailableModelsAsync(string apiKey, string baseUrl)
        {
            return await Task.FromResult<IReadOnlyList<string>>(
            [
                "deepseek-v4-flash",
                "deepseek-v4-pro",
            ]);
        }
    }
}
