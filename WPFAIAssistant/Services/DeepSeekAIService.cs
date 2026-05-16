using Microsoft.Extensions.AI;
using OpenAI;
using System.ClientModel;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Runtime.CompilerServices;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
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
            // deepseek-v4-pro: thinking model — raw HTTP tool-call loop + thinking capture + streaming final answer
            // deepseek-v4-flash and others: IChatClient + UseFunctionInvocation (standard OpenAI-compatible)
            bool isProModel = modelId.Contains("pro", StringComparison.OrdinalIgnoreCase)
                           || modelId.Contains("reasoner", StringComparison.OrdinalIgnoreCase);

            var baseSystem = "You are a helpful AI assistant. Format your responses using Markdown when appropriate. " +
                             "You have access to tools — use them whenever the task requires local file system access.";
            var systemPrompt = string.IsNullOrWhiteSpace(systemPromptExtra)
                ? baseSystem
                : baseSystem + "\n\n" + systemPromptExtra;

            if (isProModel)
            {
                // ── deepseek-v4-pro: raw HTTP path ─────────────────────────────────────────
                // 1. Non-streaming tool-call loop (may iterate multiple rounds)
                //    Each request includes: tools + thinking:{type:enabled} + reasoning_effort
                //    When tool_calls returned: invoke locally, append reasoning_content + tool results, repeat
                // 2. Once no tool_calls: stream the final answer (captures thinking/reasoning_content)
                var aiFunctions = _agentRegistry.GetAIFunctions();
                var messages = BuildRawMessages(systemPrompt, history, userMessage);

                await foreach (var token in RunProToolLoopAndStreamAsync(
                    messages, aiFunctions, modelId, apiKey, baseUrl, onThinkingChunk, cancellationToken))
                {
                    yield return token;
                }
                yield break;
            }

            // ── deepseek-v4-flash / standard models: IChatClient + UseFunctionInvocation ──
            // Single pipeline call; middleware intercepts tool-call requests, invokes AIFunctions
            // locally, appends results, and lets model continue to the final streamed answer.
            {
                var aiFunctions = _agentRegistry.GetAIFunctions();
                var chatClient = BuildMeaiChatClient(modelId, apiKey, baseUrl);
                var options = new ChatOptions
                {
                    MaxOutputTokens = 16000,
                    Temperature = 0.7f,
                    Tools = aiFunctions.Count > 0 ? [.. aiFunctions] : null,
                    ToolMode = aiFunctions.Count > 0 ? ChatToolMode.Auto : null,
                };
                var meaiMessages = BuildMeaiMessages(systemPrompt, history, userMessage);

                await foreach (var update in chatClient.GetStreamingResponseAsync(
                    meaiMessages, options, cancellationToken))
                {
                    if (!string.IsNullOrEmpty(update.Text))
                        yield return update.Text;
                }
            }
        }

        // ── deepseek-v4-pro: tool-call loop (non-streaming) then stream final answer ──────
        private static async IAsyncEnumerable<string> RunProToolLoopAndStreamAsync(
            List<object> messages,
            IReadOnlyList<AIFunction> aiFunctions,
            string modelId,
            string apiKey,
            string baseUrl,
            Func<string, Task>? onThinkingChunk,
            [EnumeratorCancellation] CancellationToken cancellationToken)
        {
            var url = baseUrl.TrimEnd('/') + "/chat/completions";
            var toolDefs = BuildToolDefinitions(aiFunctions);
            var functionMap = aiFunctions.ToDictionary(f => f.Name, f => f);

            using var http = new HttpClient();
            http.DefaultRequestHeaders.Authorization =
                new AuthenticationHeaderValue("Bearer", apiKey);

            // Tool-call loop (non-streaming)
            while (true)
            {
                var body = new Dictionary<string, object?>
                {
                    ["model"] = modelId,
                    ["messages"] = messages,
                    ["max_tokens"] = 16000,
                    ["reasoning_effort"] = "high",
                    ["thinking"] = new Dictionary<string, string> { ["type"] = "enabled" },
                };
                if (toolDefs.Count > 0)
                    body["tools"] = toolDefs;

                var requestBody = JsonSerializer.Serialize(body);
                using var req = new HttpRequestMessage(HttpMethod.Post, url)
                {
                    Content = new StringContent(requestBody, Encoding.UTF8, "application/json")
                };

                using var resp = await http.SendAsync(req, cancellationToken);
                resp.EnsureSuccessStatusCode();

                var responseJson = await resp.Content.ReadAsStringAsync(cancellationToken);
                using var doc = JsonDocument.Parse(responseJson);
                var choice = doc.RootElement.GetProperty("choices")[0];
                var msg = choice.GetProperty("message");

                // Extract reasoning_content and content
                string? reasoning = null;
                string? content = null;
                if (msg.TryGetProperty("reasoning_content", out var rc) && rc.ValueKind == JsonValueKind.String)
                    reasoning = rc.GetString();
                if (msg.TryGetProperty("content", out var ct) && ct.ValueKind != JsonValueKind.Null)
                    content = ct.GetString();

                // Check finish_reason
                var finishReason = choice.GetProperty("finish_reason").GetString();

                // Build assistant message to append (must include reasoning_content when tool_calls present)
                var assistantMsg = new Dictionary<string, object?>
                {
                    ["role"] = "assistant",
                    ["content"] = content,
                    ["reasoning_content"] = reasoning,
                };

                if (finishReason != "tool_calls")
                {
                    // No more tool calls — switch to streaming for the final answer
                    // (messages already has the full context; append assistant stub and stream)
                    messages.Add(assistantMsg);
                    break;
                }

                // Has tool_calls — invoke them locally
                var toolCallsJson = msg.GetProperty("tool_calls");
                var toolCallsList = new List<Dictionary<string, object?>>();
                foreach (var tc in toolCallsJson.EnumerateArray())
                {
                    toolCallsList.Add(new()
                    {
                        ["id"] = tc.GetProperty("id").GetString(),
                        ["type"] = "function",
                        ["function"] = new Dictionary<string, object?>
                        {
                            ["name"] = tc.GetProperty("function").GetProperty("name").GetString(),
                            ["arguments"] = tc.GetProperty("function").GetProperty("arguments").GetString(),
                        }
                    });
                }
                assistantMsg["tool_calls"] = toolCallsList;
                messages.Add(assistantMsg);

                // Invoke each tool and append tool result messages
                foreach (var tc in toolCallsJson.EnumerateArray())
                {
                    var toolCallId = tc.GetProperty("id").GetString()!;
                    var funcName = tc.GetProperty("function").GetProperty("name").GetString()!;
                    var argsJson = tc.GetProperty("function").GetProperty("arguments").GetString() ?? "{}";

                    string toolResult;
                    if (functionMap.TryGetValue(funcName, out var aiFunc))
                    {
                        try
                        {
                            var argsNode = JsonNode.Parse(argsJson) as JsonObject ?? new JsonObject();
                            var argsDict = argsNode.ToDictionary(
                                kv => kv.Key,
                                kv => (object?)(kv.Value?.ToString()));
                            var result = await aiFunc.InvokeAsync(new AIFunctionArguments(argsDict), cancellationToken);
                            toolResult = result?.ToString() ?? "(no result)";
                        }
                        catch (Exception ex)
                        {
                            toolResult = $"[Error] {ex.Message}";
                        }
                    }
                    else
                    {
                        toolResult = $"[Error] Unknown function: {funcName}";
                    }

                    messages.Add(new Dictionary<string, object?>
                    {
                        ["role"] = "tool",
                        ["tool_call_id"] = toolCallId,
                        ["content"] = toolResult,
                    });
                }
                // Loop again with updated messages
            }

            // ── Stream the final answer ────────────────────────────────────────────────────
            // Remove the stub assistant message we just added (last entry), re-request with stream=true
            messages.RemoveAt(messages.Count - 1);

            var streamBody = new Dictionary<string, object?>
            {
                ["model"] = modelId,
                ["messages"] = messages,
                ["stream"] = true,
                ["max_tokens"] = 16000,
                ["reasoning_effort"] = "high",
                ["thinking"] = new Dictionary<string, string> { ["type"] = "enabled" },
            };
            if (toolDefs.Count > 0)
                streamBody["tools"] = toolDefs;

            var streamReqBody = JsonSerializer.Serialize(streamBody);
            using var streamReq = new HttpRequestMessage(HttpMethod.Post, url)
            {
                Content = new StringContent(streamReqBody, Encoding.UTF8, "application/json")
            };

            using var streamResp = await http.SendAsync(
                streamReq, HttpCompletionOption.ResponseHeadersRead, cancellationToken);
            streamResp.EnsureSuccessStatusCode();

            using var stream = await streamResp.Content.ReadAsStreamAsync(cancellationToken);
            using var reader = new System.IO.StreamReader(stream);

            while (!reader.EndOfStream && !cancellationToken.IsCancellationRequested)
            {
                var line = await reader.ReadLineAsync(cancellationToken);
                if (string.IsNullOrWhiteSpace(line) || !line.StartsWith("data:")) continue;
                var json = line["data:".Length..].Trim();
                if (json == "[DONE]") break;

                string? reasoning = null;
                string? content = null;
                try
                {
                    using var doc = JsonDocument.Parse(json);
                    var delta = doc.RootElement.GetProperty("choices")[0].GetProperty("delta");

                    if (delta.TryGetProperty("reasoning_content", out var rc) && rc.ValueKind == JsonValueKind.String)
                        reasoning = rc.GetString();
                    else if (delta.TryGetProperty("thinking", out var tk) && tk.ValueKind == JsonValueKind.String)
                        reasoning = tk.GetString();

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

        // ── Build OpenAI tool definitions from AIFunction list ────────────────────────────
        private static List<object> BuildToolDefinitions(IReadOnlyList<AIFunction> functions)
        {
            var tools = new List<object>();
            foreach (var f in functions)
            {
                // AIFunction.JsonSchema contains the JSON schema for parameters
                var schemaNode = f.JsonSchema;
                tools.Add(new
                {
                    type = "function",
                    function = new
                    {
                        name = f.Name,
                        description = f.Description,
                        parameters = schemaNode,
                    }
                });
            }
            return tools;
        }

        // ── Build raw message list ────────────────────────────────────────────────────────
        private static List<object> BuildRawMessages(
            string systemPrompt,
            IReadOnlyList<AppChatMessage> history,
            string userMessage)
        {
            var messages = new List<object>
            {
                new { role = "system", content = systemPrompt }
            };
            foreach (var msg in history)
            {
                var role = msg.Role == MessageRole.User ? "user" : "assistant";
                messages.Add(new { role, content = msg.Content });
            }
            messages.Add(new { role = "user", content = userMessage });
            return messages;
        }

        // ── Build Microsoft.Extensions.AI ChatMessage list (for flash/standard models) ────
        private static List<MEAIChatMessage> BuildMeaiMessages(
            string systemPrompt,
            IReadOnlyList<AppChatMessage> history,
            string userMessage)
        {
            var messages = new List<MEAIChatMessage> { new(ChatRole.System, systemPrompt) };
            foreach (var msg in history)
            {
                var role = msg.Role == MessageRole.User ? ChatRole.User : ChatRole.Assistant;
                messages.Add(new(role, msg.Content));
            }
            messages.Add(new(ChatRole.User, userMessage));
            return messages;
        }

        // ── IChatClient for flash/standard models ─────────────────────────────────────────
        private static IChatClient BuildMeaiChatClient(string modelId, string apiKey, string baseUrl)
        {
            var openAiOptions = new OpenAIClientOptions
            {
                Endpoint = new Uri(baseUrl.TrimEnd('/') + "/")
            };
            return new OpenAIClient(new ApiKeyCredential(apiKey), openAiOptions)
                .GetChatClient(modelId)
                .AsIChatClient()
                .AsBuilder()
                .UseFunctionInvocation()
                .Build();
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
