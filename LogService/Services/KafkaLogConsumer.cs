using Confluent.Kafka;
using Confluent.Kafka.Admin;
using LogService.Configurations;
using LogService.Models;
using Microsoft.Extensions.Options;
using MongoDB.Bson;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace LogService.Services;

public class LogMessageDto
{
    public string Source { get; set; } = null!;
    public string Level { get; set; } = null!;
    public string Message { get; set; } = null!;
    public DateTime Timestamp { get; set; }
    public Dictionary<string, object>? Metadata { get; set; }
}

public class KafkaLogConsumer : BackgroundService
{
    private readonly KafkaSettings _kafkaSettings;
    private readonly IServiceProvider _serviceProvider;
    private readonly ILogger<KafkaLogConsumer> _logger;
    private readonly JsonSerializerOptions _jsonOptions = new(JsonSerializerDefaults.Web)
    {
        PropertyNameCaseInsensitive = true,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull
    };

    public KafkaLogConsumer(
        IOptions<KafkaSettings> kafkaOptions,
        IServiceProvider serviceProvider,
        ILogger<KafkaLogConsumer> logger)
    {
        _kafkaSettings = kafkaOptions.Value;
        _serviceProvider = serviceProvider;
        _logger = logger;
    }

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
        consumer.Subscribe(_kafkaSettings.Topic);

        _logger.LogInformation("Kafka consumer iniciado, escutando tópico '{Topic}'", _kafkaSettings.Topic);

        // 3) Loop em background, paralelamente ao host HTTP
        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                var cr = consumer.Consume(stoppingToken);

                // 4) Desserializa
                var dto = JsonSerializer.Deserialize<LogMessageDto>(cr.Message.Value, _jsonOptions);
                if (dto == null)
                {
                    _logger.LogWarning("Mensagem Kafka inválida: {Value}", cr.Message.Value);
                    continue;
                }

                // 5) Mapeia para entidade Mongo
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
                await logService.SaveAsync(entry);

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
        var adminConfig = new AdminClientConfig { BootstrapServers = _kafkaSettings.BootstrapServers };
        using var admin = new AdminClientBuilder(adminConfig).Build();

        try
        {
            await admin.CreateTopicsAsync(new[]
            {
                new TopicSpecification
                {
                    Name              = _kafkaSettings.Topic,
                    NumPartitions     = 3,
                    ReplicationFactor = 1
                }
            });

            _logger.LogInformation("Tópico '{Topic}' criado ou já existia.", _kafkaSettings.Topic);
        }
        catch (CreateTopicsException e)
            when (e.Results.Any(r => r.Error.Code == ErrorCode.TopicAlreadyExists))
        {
            _logger.LogInformation("Tópico '{Topic}' já existia — pulando criação.", _kafkaSettings.Topic);
        }
        catch (Exception e)
        {
            _logger.LogError(e, "Falha ao criar tópico '{Topic}'.", _kafkaSettings.Topic);
        }
    }
}
