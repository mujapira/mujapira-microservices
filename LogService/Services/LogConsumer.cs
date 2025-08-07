using Confluent.Kafka;
using Confluent.Kafka.Admin;
using Contracts.Logs;
using Microsoft.Extensions.Options;
using MongoDB.Bson;
using System.Text.Json;
using System.Text.Json.Serialization;
using LogService.Models;
using Contracts.Common;

namespace LogService.Services;

public class LogConsumer(
    IOptions<KafkaSettings> kafkaOptions,
    IServiceProvider serviceProvider,
    ILogger<LogConsumer> logger) : BackgroundService
{
    private readonly KafkaSettings _kafkaSettings = kafkaOptions.Value;
    private readonly IServiceProvider _serviceProvider = serviceProvider;
    private readonly ILogger<LogConsumer> _logger = logger;
    private readonly JsonSerializerOptions _jsonOptions = new(JsonSerializerDefaults.Web)
    {
        PropertyNameCaseInsensitive = true,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull
    };

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        // 1) Garante que o tópico exista, mas sem bloquear o host
        await CreateTopic();

        // 2) Configura e lança o consumer
        var consumerConfig = new ConsumerConfig
        {
            BootstrapServers = _kafkaSettings.BootstrapServers,
            GroupId = "log-consumer-group",
            AutoOffsetReset = AutoOffsetReset.Earliest,
            EnableAutoCommit = false
        };

        using var consumer = new ConsumerBuilder<Ignore, string>(consumerConfig).Build();

        var logTopics = Enum
            .GetValues<LogKafkaTopics>()
            .Cast<LogKafkaTopics>()
            .Select(t => t.GetTopicName())
            .ToList();

        consumer.Subscribe(logTopics);

        _logger.LogInformation("Kafka consumer iniciado, escutando tópico '{Topic}'", logTopics);

        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                var cr = consumer.Consume(stoppingToken);

                var dto = JsonSerializer.Deserialize<LogMessageDto>(cr.Message.Value, _jsonOptions);
                if (dto == null)
                {
                    _logger.LogWarning("Mensagem Kafka inválida: {Value}", cr.Message.Value);
                    continue;
                }

                var entry = new LogEntry
                {
                    Source = dto.Source,
                    Level = dto.Level,
                    Message = dto.Message,
                    Timestamp = dto.Timestamp
                };

                if (dto.Metadata != null)
                {
                    entry.Metadata = dto.Metadata.ToDictionary(
                        kv => kv.Key,
                        kv =>
                        {
                            if (kv.Value is JsonElement je)
                            {
                                return je.ValueKind switch
                                {
                                    JsonValueKind.String => je.GetString()!,
                                    JsonValueKind.Number => je.TryGetInt64(out var l) ? (object)l : je.GetDouble(),
                                    JsonValueKind.True => true,
                                    JsonValueKind.False => false,
                                    JsonValueKind.Object or JsonValueKind.Array
                                        => (object)BsonDocument.Parse(je.GetRawText()),
                                    _ => je.GetRawText()
                                };
                            }
                            return kv.Value!;
                        }
                    );
                }

                using var scope = _serviceProvider.CreateScope();
                var logService = scope.ServiceProvider.GetRequiredService<ILogService>();
                await logService.Save(entry);

                // 6) Commit manual
                consumer.Commit(cr);
                _logger.LogInformation(
                    "Offset commitado (partition {P}, offset {O})",
                    cr.Partition.Value, cr.Offset.Value);
            }
            catch (OperationCanceledException)
            {
                // Graceful shutdown
                break;
            }
            catch (ConsumeException ex)
            {
                _logger.LogError(ex, "Erro ao consumir mensagem do Kafka");
            }
            catch (JsonException ex)
            {
                _logger.LogError(ex, "Erro ao desserializar log");
            }
        }

        consumer.Close();
    }

    private async Task CreateTopic()
    {
        var adminConfig = new AdminClientConfig
        {
            BootstrapServers = _kafkaSettings.BootstrapServers
        };
        using var admin = new AdminClientBuilder(adminConfig).Build();

        // monta uma specification para cada tópico do enum LogKafkaTopics
        var specs = Enum.GetValues<LogKafkaTopics>()
                        .Select(t => new TopicSpecification
                        {
                            Name = t.GetTopicName(),
                            NumPartitions = 3,  // mantém 3 partições como antes
                            ReplicationFactor = 1
                        })
                        .ToArray();
        try
        {
            await admin.CreateTopicsAsync(specs);
            _logger.LogInformation(
                "Tópicos de log verificados/criados: {Topics}",
                string.Join(", ", specs.Select(s => s.Name))
            );
        }
        catch (CreateTopicsException e)
            when (e.Results.All(r => r.Error.Code == ErrorCode.TopicAlreadyExists))
        {
            _logger.LogInformation("Tópicos de log já existiam — pulando criação.");
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Falha ao criar/verificar tópicos de log");
        }
    }
}
