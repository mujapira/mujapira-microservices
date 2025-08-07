using Contracts.Common;
using Contracts.Mail;
using MailKit.Net.Smtp;
using MailKit.Security;
using MailService.Settings;
using Microsoft.Extensions.Options;
using MimeKit;
using System.Net.Mail;
using Contracts.Logs;

namespace MailService.Services
{
    public class MailService : IMailService, IAsyncDisposable
    {
        private readonly MailKit.Net.Smtp.SmtpClient _smtp;
        private readonly SmtpSettings _opts;
        private readonly IKafkaProducer _producer;

        public MailService(IOptions<SmtpSettings> opts, IKafkaProducer producer)
        {
            _opts = opts.Value;
            _smtp = new MailKit.Net.Smtp.SmtpClient();
            _smtp.Connect(_opts.Host, _opts.Port, SecureSocketOptions.StartTls);
            _smtp.Authenticate(_opts.User, _opts.AppPassword);
            _producer = producer;
        }

        public async Task Send(SendMailDto dto)
        {
            var msg = new MimeMessage();
            msg.From.Add(MailboxAddress.Parse(_opts.From));
            msg.To.Add(MailboxAddress.Parse(dto.To));
            msg.Subject = dto.Subject;
            msg.Body = new TextPart(dto.IsHtml ? "html" : "plain")
            {
                Text = dto.Body
            };

            var logDto = new LogMessageDto
            (
                Message: "E-mail enviado",
                Level: Contracts.Logs.LogLevel.Info,
                Timestamp: DateTime.UtcNow,
                Source: RegisteredMicroservices.MailService,
                Metadata: new Dictionary<string, object>
                {
                    { "To", dto.To },
                    { "Subject", dto.Subject },
                    { "Body", dto.Body }
                }
            );

            await _producer.Produce(LogKafkaTopics.Users.GetTopicName(), logDto);

            try
            {

                await _smtp.SendAsync(msg);
            }
            catch
            {

            }
        }

        public async ValueTask DisposeAsync()
        {
            await _smtp.DisconnectAsync(true);
            _smtp.Dispose();
        }
    }
}
