using Microsoft.Extensions.Logging;
using Npgsql;

namespace Common.Readiness.Postgres;

public static class PostgresReadiness
{
    public static async Task WaitAsync(
        string connectionString,
        ILogger logger,
        int maxRetries = 8,
        TimeSpan? initialDelay = null,
        TimeSpan? maxDelay = null)
    {
        int attempt = 0;
        var delay = initialDelay ?? TimeSpan.FromSeconds(1);
        var maxCap = maxDelay ?? TimeSpan.FromSeconds(30);

        while (true)
        {
            try
            {
                await using var conn = new NpgsqlConnection(connectionString);
                await conn.OpenAsync();
                await using var cmd = conn.CreateCommand();
                cmd.CommandText = "SELECT 1";
                await cmd.ExecuteScalarAsync();

                logger.LogInformation("Postgres disponível.");
                return;
            }
            catch (Exception ex)
            {
                attempt++;
                if (attempt >= maxRetries)
                {
                    logger.LogError(ex, "Falha ao conectar no Postgres após {Attempt} tentativas.", attempt);
                    throw;
                }
                logger.LogWarning("Postgres indisponível (tentativa {Attempt}). Retry em {Delay}s. Motivo: {Reason}",
                    attempt, delay.TotalSeconds, ex.Message);
                await Task.Delay(delay);
                delay = TimeSpan.FromSeconds(Math.Min(delay.TotalSeconds * 2, maxCap.TotalSeconds));
            }
        }
    }
}
