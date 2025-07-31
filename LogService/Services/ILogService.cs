using Contracts.Logs;
using LogService.Models;

namespace LogService.Services;

public interface ILogService
{
    Task Save(LogEntry entry);
    Task<List<LogMessageDto>> GetAll(string? source = null, int limit = 100);
}
