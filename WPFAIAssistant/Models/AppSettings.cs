namespace WPFAIAssistant.Models
{
    public class AppSettings
    {
        public string ApiKey { get; set; } = string.Empty;
        public string BaseUrl { get; set; } = "https://api.deepseek.com/v1";
        public string ModelId { get; set; } = "deepseek-chat";
    }
}
