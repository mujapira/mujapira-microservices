using System.Diagnostics;
using Microsoft.AspNetCore.Builder;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.DependencyInjection;

namespace Common.Observability;

public static class ObservabilityExtensions
{
    public static IApplicationBuilder UseRequestLogging(this IApplicationBuilder app, string loggerName = "Request")
    {
        return app.Use(async (ctx, next) =>
        {
            var log = ctx.RequestServices
                                     .GetRequiredService<ILoggerFactory>()
                                     .CreateLogger(loggerName);

            var correlationId = GetOrCreateCorrelationId(ctx);
            using var scope = log.BeginScope(new Dictionary<string, object?>
            {
                ["CorrelationId"] = correlationId,
                ["Method"] = ctx.Request.Method,
                ["Path"] = ctx.Request.Path.Value,
                ["RemoteIP"] = ctx.Connection.RemoteIpAddress?.ToString()
            });

            var sw = Stopwatch.StartNew();
            try { await next(); }
            finally
            {
                sw.Stop();
                log.LogInformation("HTTP {Method} {Path} => {Status} ({ElapsedMs} ms)",
                    ctx.Request.Method, ctx.Request.Path.Value, ctx.Response.StatusCode, sw.ElapsedMilliseconds);
            }
        });
    }

    private static string GetOrCreateCorrelationId(Microsoft.AspNetCore.Http.HttpContext ctx)
    {
        const string header = "X-Correlation-Id";
        if (!ctx.Request.Headers.TryGetValue(header, out var value) || string.IsNullOrWhiteSpace(value))
        {
            value = Activity.Current?.Id ?? Guid.NewGuid().ToString("N");
            ctx.Response.Headers[header] = value;
        }
        return value!;
    }
}
