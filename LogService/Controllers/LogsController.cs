using Contracts.Common;
using Contracts.Logs;
using LogService.Models;
using LogService.Services;
using Microsoft.AspNetCore.Mvc;
using System.ComponentModel.DataAnnotations;

namespace LogService.Controllers;

[ApiController]
[Route("api/[controller]")]
public class LogsController(ILogService logService) : ControllerBase
{
    private readonly ILogService _logService = logService;

    public class LogQueryParameters : PagingDto
    {
        public List<RegisteredMicroservices>? Sources { get; set; }

        /// <summary>Você pode passar múltiplos ?levels=Error&levels=Fatal</summary>
        public List<Contracts.Logs.LogLevel>? Levels { get; set; }

        /// <summary>Data/hora inicial (ISO)</summary>
        public DateTime? From { get; set; }

        /// <summary>Data/hora final (ISO)</summary>
        public DateTime? To { get; set; }

        /// <summary>Busca parcial no texto da mensagem</summary>
        public string? MessageContains { get; set; }

        /// <summary>Filtra por metadata chave/valor</summary>
        public string? MetadataKey { get; set; }
        public string? MetadataValue { get; set; }
    }
    public class LogQuery
    {
        public List<string>? Sources { get; set; }
        public List<string>? Levels { get; set; }
        public DateTime? From { get; set; }
        public DateTime? To { get; set; }
        public string? MessageContains { get; set; }
        public string? MetadataKey { get; set; }
        public string? MetadataValue { get; set; }
        public int Skip { get; set; }
        public int Limit { get; set; }
    }

    [HttpPost]
    public async Task<IActionResult> Post([FromBody] LogMessageDto dto)
    {
        if (!ModelState.IsValid)
            return BadRequest(ModelState);

        var entry = new LogEntry
        {
            Source = dto.Source,
            Level = dto.Level,
            Message = dto.Message,
            Timestamp = DateTime.UtcNow,
            Metadata = dto.Metadata
        };

        await _logService.Save(entry);
        return Ok(new { success = true });
    }


    [HttpGet]
    [HttpGet]
    public async Task<IActionResult> GetLogs(
            [FromQuery] LogQueryParameters q)
    {
        var query = new LogQuery
        {
            Sources = q.Sources?.Select(s => s.ToString()).ToList(),
            Levels = q.Levels?.Select(l => l.ToString()).ToList(),
            From = q.From,
            To = q.To,
            MessageContains = q.MessageContains,
            MetadataKey = q.MetadataKey,
            MetadataValue = q.MetadataValue,
            Skip = q.Skip,
            Limit = q.Limit
        };

        var result = await _logService.GetLogs(query);
        return Ok(result);
    }
}
