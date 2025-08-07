using Contracts.Mail;

namespace MailService.Services
{
    public interface IMailService
    {
        Task Send(SendMailDto dto);
    }
}
