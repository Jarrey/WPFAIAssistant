namespace WPFAIAssistant.Services
{
    public interface IAIService
    {
        IAsyncEnumerable<string> StreamChatAsync(
            string userMessage,
            string modelId,
            string apiKey,
            string baseUrl,
            IReadOnlyList<Models.ChatMessage> history,
            Action<string>? onThinkingChunk = null,
            string? systemPromptExtra = null,
            CancellationToken cancellationToken = default);

        Task<IReadOnlyList<string>> GetAvailableModelsAsync(string apiKey, string baseUrl);
    }
}
