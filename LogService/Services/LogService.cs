using Contracts.Logs;
using LogService.Models;
using Microsoft.Extensions.Logging;
using MongoDB.Driver;

namespace LogService.Services;

public class LogService(
    IMongoCollection<LogEntry> logCollection,
    ILogger<LogService> logger) : ILogService
{
    private readonly IMongoCollection<LogEntry> _logCollection = logCollection;
    private readonly ILogger<LogService> _logger = logger;

    public Task Save(LogEntry entry)
        => _logCollection.InsertOneAsync(entry);

    public async Task<List<LogMessageDto>> GetAll(string? source = null, int limit = 100)
    {
        FilterDefinition<LogEntry> filter = FilterDefinition<LogEntry>.Empty;
        if (!string.IsNullOrWhiteSpace(source))
        {
            if (Enum.TryParse<RegisteredMicroservices>(source, true, out var svc))
            {
                filter = Builders<LogEntry>.Filter.Eq(e => e.Source, svc);
            }
            else
            {
                _logger.LogWarning("GetAllAsync: fonte inválida recebida: {Source}", source);
                return [];
            }
        }

        var entries = await _logCollection
            .Find(filter)
            .SortByDescending(e => e.Timestamp)
            .Limit(limit)
            .ToListAsync();

        return [.. entries
            .Select(e => new LogMessageDto(
                e.Source,
                e.Level,
                e.Message,
                e.Timestamp,
                e.Metadata
            ))];
    }
}
