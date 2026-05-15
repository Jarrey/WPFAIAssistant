using Microsoft.SemanticKernel;
using Microsoft.SemanticKernel.ChatCompletion;
using Microsoft.SemanticKernel.Connectors.OpenAI;
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
            // tool/function calling when thinking is enabled. Skip the SK pass for it.
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
            var messages = new List<object>();
            messages.Add(new { role = "system", content = systemPrompt });
            foreach (var msg in history)
            {
                var role = msg.Role == MessageRole.User ? "user" : "assistant";
                messages.Add(new { role, content = msg.Content });
            }
            messages.Add(new { role = "user", content = userMessage });

            if (!isReasoningModel)
            {
                // ── 1. Run any tool/function calls via SK (non-streaming) ──────
                var kernel = BuildKernel(modelId, apiKey, baseUrl);
                _agentRegistry.ApplyToKernel(kernel);

                var chatService = kernel.GetRequiredService<IChatCompletionService>();

                var skHistory = new Microsoft.SemanticKernel.ChatCompletion.ChatHistory();
                skHistory.AddSystemMessage(systemPrompt);
                foreach (var msg in history)
                {
                    if (msg.Role == MessageRole.User)           skHistory.AddUserMessage(msg.Content);
                    else if (msg.Role == MessageRole.Assistant) skHistory.AddAssistantMessage(msg.Content);
                }
                skHistory.AddUserMessage(userMessage);

#pragma warning disable SKEXP0001
                var toolSettings = new OpenAIPromptExecutionSettings
                {
                    MaxTokens = 16000,
                    Temperature = 0.7,
                    ToolCallBehavior = ToolCallBehavior.AutoInvokeKernelFunctions,
                };
#pragma warning restore SKEXP0001

                // Let SK resolve function calls (may loop internally)
                var toolResult = await chatService.GetChatMessageContentAsync(
                    skHistory, toolSettings, kernel, cancellationToken);
                skHistory.Add(toolResult);

                // Rebuild messages from SK history, excluding the final assistant reply
                // so the streaming call has something left to generate.
                messages.Clear();
                var skMessages = skHistory.ToList();
                for (int i = 0; i < skMessages.Count - 1; i++)
                {
                    var m = skMessages[i];
                    var role = m.Role == AuthorRole.System    ? "system"
                             : m.Role == AuthorRole.Assistant ? "assistant"
                             : "user";
                    messages.Add(new { role, content = m.Content ?? string.Empty });
                }
            }

            // ── Stream via raw HTTP so we can read reasoning_content ───────────
            await foreach (var token in StreamRawAsync(
                messages, modelId, apiKey, baseUrl,
                onThinkingChunk, isReasoningModel, cancellationToken))
            {
                yield return token;
            }
        }

        // ── Raw SSE streaming ────────────────────────────────────────────────────
        private static async IAsyncEnumerable<string> StreamRawAsync(
            List<object> messages,
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
                    temperature = 1,           // required when thinking enabled
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
                string? content   = null;
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

                    // normal content
                    if (delta.TryGetProperty("content", out var ct) &&
                        ct.ValueKind == JsonValueKind.String)
                    {
                        content = ct.GetString();
                    }
                }
                catch { continue; }

                if (!string.IsNullOrEmpty(reasoning) && onThinkingChunk != null)
                    await onThinkingChunk(reasoning);

                if (!string.IsNullOrEmpty(content))
                    yield return content;
            }
        }

        // ── Model list ───────────────────────────────────────────────────────────
        public async Task<IReadOnlyList<string>> GetAvailableModelsAsync(string apiKey, string baseUrl)
        {
            return await Task.FromResult<IReadOnlyList<string>>(new List<string>
            {
                "deepseek-v4-flash",
                "deepseek-v4-pro",
            });
        }

        // ── SK kernel (used only for tool-call pass) ─────────────────────────────
        private static Kernel BuildKernel(string modelId, string apiKey, string baseUrl)
        {
            var builder = Kernel.CreateBuilder();
            builder.AddOpenAIChatCompletion(
                modelId: modelId,
                apiKey: apiKey,
                httpClient: new System.Net.Http.HttpClient
                {
                    BaseAddress = new Uri(baseUrl.TrimEnd('/') + "/")
                });
            return builder.Build();
        }
    }
}
