namespace MailService.Settings
{
    public class SmtpSettings
    {
        public required string Host { get; set; }
        public int Port { get; set; }
        public required string User { get; set; }
        public required string AppPassword { get; set; }
        public required string From { get; set; }
    }
}
