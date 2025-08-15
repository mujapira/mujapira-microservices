using System.Text.Json;
using Contracts.Logs;
using Contracts.Common;

namespace IdentityService.Middlewares
{
    public class GlobalExceptionLoggingMiddleware(RequestDelegate next, IKafkaProducer producer)
    {
        private readonly RequestDelegate _next = next;
        private readonly IKafkaProducer _producer = producer;

        public async Task InvokeAsync(HttpContext context)
        {
            try
            {
                await _next(context);
            }
            catch (Exception ex)
            {
                var logEvent = new LogMessageDto
                (
                    Source: RegisteredMicroservices.UserService,
                    Level: Contracts.Logs.LogLevel.Error,
                    Message: ex.Message,
                    Metadata: new Dictionary<string, object>
                    {
                        { "Path", context.Request.Path },
                        { "StackTrace", ex.StackTrace ?? string.Empty }
                    },
                    Timestamp: DateTime.UtcNow
                );

                var json = JsonSerializer.Serialize(logEvent);

                await _producer.Produce(LogKafkaTopics.Users.GetTopicName(), json);

                context.Response.StatusCode = StatusCodes.Status500InternalServerError;
                context.Response.ContentType = "application/json";
                await context.Response.WriteAsJsonAsync(new { error = "Internal Server Error" });
            }
        }
    }
}