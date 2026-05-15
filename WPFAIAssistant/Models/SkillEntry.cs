namespace WPFAIAssistant.Models
{
    public class SkillEntry
    {
        public string Name { get; set; } = string.Empty;
        public string FilePath { get; set; } = string.Empty;
        public string Content { get; set; } = string.Empty;
        public bool IsEnabled { get; set; } = true;
        public string Source { get; set; } = string.Empty;
    }
}
