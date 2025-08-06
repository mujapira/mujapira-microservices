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
        var builder = Builders<LogEntry>.Filter;
        var filter = builder.Empty;

        if (q.Sources != null && q.Sources.Count != 0)
        {
            filter &= builder.In(e => e.Source.ToString(), q.Sources);
        }

        if (q.Levels != null && q.Levels.Count != 0)
        {
            filter &= builder.In(e => e.Level.ToString(), q.Levels);
        }

        // datas
        if (q.From.HasValue)
            filter &= builder.Gte(x => x.Timestamp, q.From.Value);

        if (q.To.HasValue)
            filter &= builder.Lte(x => x.Timestamp, q.To.Value);

        // busca parcial na mensagem
        if (!string.IsNullOrWhiteSpace(q.MessageContains))
        {
            filter &= builder.Regex(
                x => x.Message,
                new MongoDB.Bson.BsonRegularExpression(q.MessageContains, "i")
            );
        }

        // metadata
        if (!string.IsNullOrWhiteSpace(q.MetadataKey) &&
            !string.IsNullOrWhiteSpace(q.MetadataValue))
        {
            filter &= builder.Eq($"Metadata.{q.MetadataKey}", q.MetadataValue);
        }

        return await logCollection
            .Find(filter)
            .Skip(q.Skip)
            .Limit(q.Limit)
            .SortByDescending(x => x.Timestamp)
            .ToListAsync();
    }
}
