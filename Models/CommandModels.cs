namespace cmdrix.Models
{
    public enum SafetyLevel
    {
        Safe,
        Warning,
        Danger
    }

    public class CommandPreview
    {
        public string Command { get; set; } = string.Empty;
        public string Description { get; set; } = string.Empty;
        public SafetyLevel SafetyLevel { get; set; }
        public string WorkingDirectory { get; set; } = string.Empty;
    }

    public class Note
    {
        public string Id { get; set; } = Guid.NewGuid().ToString();
        public string Content { get; set; } = string.Empty;
        public DateTime CreatedAt { get; set; } = DateTime.Now;
        public List<string> Tags { get; set; } = new List<string>();
    }

    public class AppConfig
    {
        public System.Drawing.Color BackgroundColor { get; set; } = System.Drawing.Color.Black;
        public byte BackgroundOpacity { get; set; } = 204; // 0.8 * 255
        public string GeminiApiKey { get; set; } = string.Empty;
        public string CurrentDirectory { get; set; } = Environment.CurrentDirectory;
    }

    public class GeminiRequest
    {
        public List<GeminiContent> Contents { get; set; } = new List<GeminiContent>();
    }

    public class GeminiContent
    {
        public List<GeminiPart> Parts { get; set; } = new List<GeminiPart>();
    }

    public class GeminiPart
    {
        public string? Text { get; set; }
        public GeminiInlineData? InlineData { get; set; }
    }

    public class GeminiInlineData
    {
        public string MimeType { get; set; } = string.Empty;
        public string Data { get; set; } = string.Empty;
    }

    public class GeminiResponse
    {
        public List<GeminiCandidate> Candidates { get; set; } = new List<GeminiCandidate>();
    }

    public class GeminiCandidate
    {
        public GeminiContent Content { get; set; } = new GeminiContent();
    }
}