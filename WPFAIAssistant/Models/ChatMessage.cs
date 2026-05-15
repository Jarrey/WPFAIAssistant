namespace WPFAIAssistant.Models
{
    public enum MessageRole { User, Assistant, System }

    public class ChatMessage
    {
        public MessageRole Role { get; set; }
        public string Content { get; set; } = string.Empty;
        public DateTime Timestamp { get; set; } = DateTime.Now;
    }
}
