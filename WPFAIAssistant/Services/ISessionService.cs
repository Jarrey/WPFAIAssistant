using WPFAIAssistant.Models;

namespace WPFAIAssistant.Services
{
    public interface ISessionService
    {
        Task<IReadOnlyList<ChatSession>> LoadAllAsync();
        Task<ChatSession?> LoadAsync(string id);
        Task SaveAsync(ChatSession session);
        Task DeleteAsync(string id);
    }
}
