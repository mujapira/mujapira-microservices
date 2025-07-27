using LogService.Models;
using LogService.Settings;
using Microsoft.Extensions.Options;
using MongoDB.Driver;

namespace LogService.Services;

public class LogService : ILogService
{
    private readonly IMongoCollection<LogEntry> _logCollection;

    public LogService(IOptions<MongoDbSettings> settings)
    {
        var client = new MongoClient(settings.Value.ConnectionString);
        var database = client.GetDatabase(settings.Value.DatabaseName);
        _logCollection = database.GetCollection<LogEntry>(settings.Value.CollectionName);
    }

    public async Task SaveAsync(LogEntry log)
    {
        await _logCollection.InsertOneAsync(log);
    }

    public async Task<List<LogEntry>> GetAllAsync(string? source = null, int limit = 100)
    {
        var filter = source is null
            ? Builders<LogEntry>.Filter.Empty
            : Builders<LogEntry>.Filter.Eq(le => le.Source, source);

        return await _logCollection
            .Find(filter)
            .SortByDescending(le => le.Timestamp)
            .Limit(limit)
            .ToListAsync();
    }
}
