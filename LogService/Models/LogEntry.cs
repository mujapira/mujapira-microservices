using MongoDB.Bson;
using MongoDB.Bson.Serialization.Attributes;
using System.Text.Json.Serialization;
using Contracts.Logs;

namespace LogService.Models;

public class LogEntry
{
    [BsonId, BsonRepresentation(BsonType.ObjectId)]
    public string? Id { get; set; }

    [JsonPropertyName("timestamp")]
    [BsonElement("timestamp")]
    public DateTime Timestamp { get; set; } = DateTime.UtcNow;

    [JsonPropertyName("level")]
    [BsonElement("level")]
    [BsonRepresentation(BsonType.String)]
    [JsonConverter(typeof(JsonStringEnumConverter))]
    public Contracts.Logs.LogLevel Level { get; set; } = Contracts.Logs.LogLevel.Info;

    [JsonPropertyName("message")]
    [BsonElement("message")]
    public string Message { get; set; } = string.Empty;

    [JsonPropertyName("source")]
    [BsonElement("source")]
    [BsonRepresentation(BsonType.String)]
    [JsonConverter(typeof(JsonStringEnumConverter))]
    public RegisteredMicroservices Source { get; set; }

    [JsonPropertyName("metadata")]
    [BsonElement("metadata")]
    public Dictionary<string, object>? Metadata { get; set; }
}
