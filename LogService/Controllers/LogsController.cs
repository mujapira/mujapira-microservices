using Contracts.Logs;
using LogService.Models;
using LogService.Services;
using Microsoft.AspNetCore.Mvc;

namespace LogService.Controllers;

[ApiController]
[Route("api/[controller]")]
public class LogsController(ILogService logService) : ControllerBase
{
    private readonly ILogService _logService = logService;

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
    public async Task<IActionResult> Get(
        [FromQuery] RegisteredMicroservices? source,
        [FromQuery] int limit = 100)
    {
        var sourceFilter = source?.ToString();
        var logs = await _logService.GetAll(sourceFilter, limit);
        return Ok(logs);
    }
}
