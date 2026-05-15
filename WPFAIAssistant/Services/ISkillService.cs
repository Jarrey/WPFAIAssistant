using WPFAIAssistant.Models;

namespace WPFAIAssistant.Services
{
    public interface ISkillService
    {
        string SkillsDirectory { get; }
        Task<IReadOnlyList<SkillEntry>> ScanDirectoryAsync(string directory);
        Task<IReadOnlyList<SkillEntry>> ScanAllKnownDirectoriesAsync();
        Task<SkillEntry> LoadFileAsync(string filePath);
        string BuildSystemPrompt(IEnumerable<SkillEntry> skills);
    }
}
