using Confluent.Kafka;
using Microsoft.Extensions.Logging;

namespace Common.Readiness.Kafka;

public static class KafkaReadiness
{
    public static async Task WaitAsync(
        string bootstrapServers,
        ILogger logger,
        int maxRetries = 8,
        TimeSpan? initialDelay = null,
        TimeSpan? maxDelay = null)
    {
        var config = new AdminClientConfig { BootstrapServers = bootstrapServers };
        int attempt = 0;
        var delay = initialDelay ?? TimeSpan.FromSeconds(1);
        var maxCap = maxDelay ?? TimeSpan.FromSeconds(30);

        while (true)
        {
            try
            {
                using var admin = new AdminClientBuilder(config).Build();
                var meta = admin.GetMetadata(TimeSpan.FromSeconds(2));
                logger.LogInformation("Kafka disponível. Tópicos: {TopicCount}", meta.Topics?.Count ?? 0);
                return;
            }
            catch (Exception ex)
            {
                attempt++;
                if (attempt >= maxRetries)
                {
                    logger.LogError(ex, "Falha ao conectar no Kafka após {Attempt} tentativas.", attempt);
                    throw;
                }
                logger.LogWarning("Kafka indisponível (tentativa {Attempt}). Retry em {Delay}s. Motivo: {Reason}",
                    attempt, delay.TotalSeconds, ex.Message);
                await Task.Delay(delay);
                delay = TimeSpan.FromSeconds(Math.Min(delay.TotalSeconds * 2, maxCap.TotalSeconds));
            }
        }
    }
}
