using MongoDB.Bson;
using MongoDB.Bson.Serialization.Attributes;
using System;
using System.Text.Json.Serialization;

namespace LogService.Models;

public class LogEntry
{
    [BsonId]
    [BsonRepresentation(BsonType.ObjectId)]
    public string? Id { get; set; }

    [JsonPropertyName("timestamp")]
    [BsonElement("timestamp")]
    public DateTime Timestamp { get; set; } = DateTime.UtcNow;

    [JsonPropertyName("level")]
    [BsonElement("level")]
    public string Level { get; set; } = "INFO";

    [JsonPropertyName("message")]
    [BsonElement("message")]
    public string Message { get; set; } = "";

    [JsonPropertyName("source")]
    [BsonElement("source")]
    public string? Source { get; set; }

    [JsonPropertyName("metadata")]
    [BsonElement("metadata")]
    public Dictionary<string, object>? Metadata { get; set; }
}
