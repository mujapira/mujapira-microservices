using Contracts.Logs;
using LogService.Models;
using Microsoft.Extensions.Logging;
using MongoDB.Driver;
using static LogService.Controllers.LogsController;

namespace LogService.Services;

public class LogService(
    IMongoCollection<LogEntry> logCollection,
    ILogger<LogService> logger) : ILogService
{
    private readonly IMongoCollection<LogEntry> _logCollection = logCollection;
    private readonly ILogger<LogService> _logger = logger;

    public Task Save(LogEntry entry)
        => _logCollection.InsertOneAsync(entry);

    public async Task<List<LogEntry>> GetLogs(LogQuery q)
    {
        var b = Builders<LogEntry>.Filter;
        var filter = b.Empty;

        if (q.Sources is { Count: > 0 })
            filter &= b.In(x => x.Source, q.Sources);

        if (q.Levels is { Count: > 0 })
            filter &= b.In(x => x.Level, q.Levels);

        if (q.From.HasValue)
            filter &= b.Gte(x => x.Timestamp, DateTime.SpecifyKind(q.From.Value, DateTimeKind.Utc));

        if (q.To.HasValue)
            filter &= b.Lte(x => x.Timestamp, DateTime.SpecifyKind(q.To.Value, DateTimeKind.Utc));

        if (!string.IsNullOrWhiteSpace(q.MessageContains))
            filter &= b.Regex(x => x.Message, new MongoDB.Bson.BsonRegularExpression(q.MessageContains, "i"));

        if (!string.IsNullOrWhiteSpace(q.MetadataKey) && !string.IsNullOrWhiteSpace(q.MetadataValue))
        {
            // tenta casar tipos comuns para não restringir a string
            object typed = q.MetadataValue!;
            if (long.TryParse(q.MetadataValue, out var l)) typed = l;
            else if (double.TryParse(q.MetadataValue, out var d)) typed = d;
            else if (bool.TryParse(q.MetadataValue, out var bo)) typed = bo;

            filter &= b.Eq($"Metadata.{q.MetadataKey}", typed);
        }

        return await logCollection
            .Find(filter)
            .SortByDescending(x => x.Timestamp)
            .Skip(q.Skip)
            .Limit(q.Limit)
            .ToListAsync();
    }
}
