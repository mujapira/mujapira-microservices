namespace Contracts.Mail
{
    public class SendMailDto
    {
        public string To { get; set; } = default!;
        public string Subject { get; set; } = default!;
        public string Body { get; set; } = default!;
        public bool IsHtml { get; set; } = true;
    }
}
