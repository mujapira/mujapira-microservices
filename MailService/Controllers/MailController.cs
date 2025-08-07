using Contracts.Mail;
using Microsoft.AspNetCore.Mvc;
using MailService.Services;
using Microsoft.AspNetCore.Authorization;

namespace MailService.Controllers
{
    [Authorize]
    [ApiController]
    [Route("api/[controller]")]
    public class MailController : ControllerBase
    {
        private readonly IMailService _mail;

        public MailController(IMailService mail)
            => _mail = mail;

        [HttpPost("send")]
        public async Task<IActionResult> Send([FromBody] SendMailDto dto)
        {
            if (!ModelState.IsValid)
                return BadRequest(ModelState);

            await _mail.Send(dto);
            return Ok(new { message = "E-mail enviado com sucesso" });
        }
    }
}
