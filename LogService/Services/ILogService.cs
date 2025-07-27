using LogService.Models;

namespace LogService.Services;

public interface ILogService
{
    Task SaveAsync(LogEntry log);
    Task<List<LogEntry>> GetAllAsync(string? source = null, int limit = 100);
}
