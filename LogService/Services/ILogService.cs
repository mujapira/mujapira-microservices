using Contracts.Logs;
using LogService.Models;
using Microsoft.AspNetCore.Mvc;
using static LogService.Controllers.LogsController;

namespace LogService.Services;

public interface ILogService
{
    Task Save(LogEntry entry);
    Task<List<LogEntry>> GetLogs(LogQuery query);
}
