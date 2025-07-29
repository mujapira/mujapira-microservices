using LogService.Models;
using LogService.Services;
using Microsoft.AspNetCore.Mvc;

namespace LogService.Controllers;

[ApiController]
[Route("api/[controller]")]
public class LogsController : ControllerBase
{
    private readonly ILogService _logService;

    public LogsController(ILogService logService)
    {
        _logService = logService;
    }

    [HttpPost]
    public async Task<IActionResult> Post([FromBody] LogEntry entry)
    {
        if (string.IsNullOrWhiteSpace(entry.Message) || string.IsNullOrWhiteSpace(entry.Source))
        {
            return BadRequest("Message and Source are required.");
        }

        entry.Timestamp = DateTime.UtcNow;
        await _logService.SaveAsync(entry);

        return Ok(new { success = true });
    }

    [HttpGet]
    public async Task<IActionResult> Get(
         [FromQuery] string? source = null,
         [FromQuery] int limit = 100)
    {
        var entries = await _logService.GetAllAsync(source, limit);


        var result = entries.Select(e => new
        {
            id = e.Id,
            timestamp = e.Timestamp,
            level = e.Level,
            message = e.Message,
            source = e.Source,
            metadata = e.Metadata == null
                ? null
                : e.Metadata.ToDictionary(
                    kv => kv.Key,
                    kv => kv.Value?.ToString()
                  )
        });

        return Ok(result);
    }
}
