using System.ComponentModel.DataAnnotations;
using System.Text.Json.Serialization;

namespace Contracts.Logs
{
    [JsonConverter(typeof(JsonStringEnumConverter))]
    public enum LogLevel
    {
        Trace,
        Debug,
        Info,
        Warn,
        Error,
        Fatal
    }

    public enum RegisteredMicroservices
    {
        AuthService,
        UserService,
    }

    public record LogMessageDto(
        RegisteredMicroservices Source,
        LogLevel Level,
        string Message,
        DateTime Timestamp,
        Dictionary<string, object>? Metadata = null
    );
}
