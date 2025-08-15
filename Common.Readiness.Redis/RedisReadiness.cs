using Microsoft.Extensions.Logging;
using StackExchange.Redis;

namespace Common.Readiness.Redis;

public static class RedisReadiness
{
    public static async Task WaitAsync(
        string endpoint,
        string password,
        ILogger logger,
        int maxRetries = 8,
        TimeSpan? initialDelay = null,
        TimeSpan? maxDelay = null)
    {
        int attempt = 0;
        var delay = initialDelay ?? TimeSpan.FromSeconds(1);
        var maxCap = maxDelay ?? TimeSpan.FromSeconds(30);

        var config = new ConfigurationOptions
        {
            EndPoints = { endpoint },
            Password = password,
            AbortOnConnectFail = false,
            ConnectTimeout = 2000
        };

        while (true)
        {
            try
            {
                using var mux = await ConnectionMultiplexer.ConnectAsync(config);
                var db = mux.GetDatabase();
                var pong = await db.PingAsync();
                logger.LogInformation("Redis disponível (ping {Ping}ms).", pong.TotalMilliseconds);
                return;
            }
            catch (Exception ex)
            {
                attempt++;
                if (attempt >= maxRetries)
                {
                    logger.LogError(ex, "Não foi possível conectar ao Redis após {Attempt} tentativas.", attempt);
                    throw;
                }
                logger.LogWarning("Redis indisponível (tentativa {Attempt}), retry em {Delay}s. Motivo: {Reason}",
                    attempt, delay.TotalSeconds, ex.Message);
                await Task.Delay(delay);
                delay = TimeSpan.FromSeconds(Math.Min(delay.TotalSeconds * 2, maxCap.TotalSeconds));
            }
        }
    }
}
