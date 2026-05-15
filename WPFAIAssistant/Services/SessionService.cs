using System.IO;
using System.Text.Json;
using WPFAIAssistant.Models;

namespace WPFAIAssistant.Services
{
    public class SessionService : ISessionService
    {
        private static readonly string SessionsDir =
            Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "sessions");

        private static readonly JsonSerializerOptions JsonOpts = new()
        {
            WriteIndented = true,
        };

        public SessionService()
        {
            Directory.CreateDirectory(SessionsDir);
        }

        public async Task<IReadOnlyList<ChatSession>> LoadAllAsync()
        {
            var sessions = new List<ChatSession>();
            foreach (var file in Directory.GetFiles(SessionsDir, "*.json"))
            {
                try
                {
                    var json = await File.ReadAllTextAsync(file);
                    var s = JsonSerializer.Deserialize<ChatSession>(json);
                    if (s != null) sessions.Add(s);
                }
                catch { /* skip corrupted files */ }
            }
            // Most recently updated first
            sessions.Sort((a, b) => b.UpdatedAt.CompareTo(a.UpdatedAt));
            return sessions;
        }

        public async Task<ChatSession?> LoadAsync(string id)
        {
            var path = FilePath(id);
            if (!File.Exists(path)) return null;
            var json = await File.ReadAllTextAsync(path);
            return JsonSerializer.Deserialize<ChatSession>(json);
        }

        public async Task SaveAsync(ChatSession session)
        {
            session.UpdatedAt = DateTime.Now;
            var json = JsonSerializer.Serialize(session, JsonOpts);
            await File.WriteAllTextAsync(FilePath(session.Id), json);
        }

        public Task DeleteAsync(string id)
        {
            var path = FilePath(id);
            if (File.Exists(path)) File.Delete(path);
            return Task.CompletedTask;
        }

        private static string FilePath(string id) =>
            Path.Combine(SessionsDir, $"{id}.json");
    }
}
