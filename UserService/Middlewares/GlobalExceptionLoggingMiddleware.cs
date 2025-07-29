using System.Text.Json;
using UserService.Services;
using UserService.Models;

namespace UserService.Middlewares
{
    public class GlobalExceptionLoggingMiddleware
    {
        private readonly RequestDelegate _next;
        private readonly IKafkaProducer _producer;

        public GlobalExceptionLoggingMiddleware(RequestDelegate next, IKafkaProducer producer)
        {
            _next = next;
            _producer = producer;
        }

        public async Task InvokeAsync(HttpContext context)
        {
            try
            {
                await _next(context);
            }
            catch (Exception ex)
            {
                var logEvent = new LogMessage
                {
                    Source = "UserService",
                    Level = "ERROR",
                    Message = ex.Message,
                    Metadata = new Dictionary<string, object>
                    {
                        { "Path", context.Request.Path },
                        { "StackTrace", ex.StackTrace ?? string.Empty }
                    },
                    Timestamp = DateTime.UtcNow
                };

                var json = JsonSerializer.Serialize(logEvent);
                await _producer.ProduceAsync(json);

                context.Response.StatusCode = StatusCodes.Status500InternalServerError;
                context.Response.ContentType = "application/json";
                await context.Response.WriteAsJsonAsync(new { error = "Internal Server Error" });
            }
        }
    }
}