namespace UserService.Models
{
    public class LogMessage
    {
        public string Source { get; set; } = null!;
        public string Level { get; set; } = null!;
        public string Message { get; set; } = null!;
        public DateTime Timestamp { get; set; }
        public Dictionary<string, object>? Metadata { get; set; }
    }
}
