using Confluent.Kafka;
using Confluent.Kafka.Admin;
using Contracts.Common;
using Contracts.Mail;
using Contracts.Identity;
using Microsoft.Extensions.Options;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace MailService.Services
{
    public class MailConsumer(
     IOptions<KafkaSettings> kafkaOptions,
     IServiceProvider serviceProvider,
     ILogger<MailConsumer> logger) : BackgroundService
    {
        private readonly KafkaSettings _kafkaSettings = kafkaOptions.Value;
        private readonly IServiceProvider _serviceProvider = serviceProvider;
        private readonly ILogger<MailConsumer> _logger = logger;
        private readonly JsonSerializerOptions _jsonOptions = new(JsonSerializerDefaults.Web)
        {
            PropertyNameCaseInsensitive = true,
            DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
        };

        protected override async Task ExecuteAsync(CancellationToken stoppingToken)
        {
            // 1) Garante que o tópico exista, mas sem bloquear o host
            await CreateTopic();

            // 2) Configura e lança o consumer
            var consumerConfig = new ConsumerConfig
            {
                BootstrapServers = _kafkaSettings.BootstrapServers,
                GroupId = "mail-consumer-group",
                AutoOffsetReset = AutoOffsetReset.Earliest,
                EnableAutoCommit = false
            };

            using var consumer = new ConsumerBuilder<Ignore, string>(consumerConfig).Build();

            var topics = Enum.GetValues<MailKafkaTopics>()
                                      .Select(t => t.GetTopicName())
                                      .ToList();

            consumer.Subscribe(topics);

            _logger.LogInformation("Kafka consumer iniciado, escutando tópico '{Topic}'", topics);

            while (!stoppingToken.IsCancellationRequested)
            {
                try
                {
                    // 1) Consome a próxima mensagem
                    var cr = consumer.Consume(stoppingToken);
                    var json = cr.Message.Value;

                    // 2) Desserializa direto no DTO/Record correto
                    var evt = JsonSerializer.Deserialize<CreatedUserEventDto>(json, _jsonOptions);
                    if (evt is null)
                    {
                        _logger.LogWarning("Mensagem Kafka inválida para CreatedUserEvent: {Value}", json);
                        consumer.Commit(cr);
                        continue;
                    }

                    // 3) Processa o evento (envio de e-mails)
                    using var scope = _serviceProvider.CreateScope();
                    var mailService = scope.ServiceProvider.GetRequiredService<IMailService>();

                    // 3a) Boas-vindas ao usuário
                    var welcome = new SendMailDto
                    {
                        To = evt.Email,
                        Subject = "Bem-vindo ao Mujapira!",
                        Body = $"Olá {evt.Name}, seja bem-vindo ao Mujapira!",
                        IsHtml = false
                    };
                    await mailService.Send(welcome);

                    // 3b) Notificação interna
                    var notify = new SendMailDto
                    {
                        To = "mujapira@gmail.com",
                        Subject = "🔔 Novo usuário criado",
                        Body = $"ID: {evt.Id}\nNome: {evt.Name}\nEmail: {evt.Email}\nAdmin: {evt.IsAdmin}",
                        IsHtml = false
                    };
                    await mailService.Send(notify);

                    _logger.LogInformation("E-mails enviados para {Email}", evt.Email);

                    // 4) Commit manual
                    consumer.Commit(cr);
                    _logger.LogInformation("Offset commitado (partition {P}, offset {O})",
                        cr.Partition.Value, cr.Offset.Value);
                }
                catch (OperationCanceledException)
                {
                    break; // shutdown gracioso
                }
                catch (ConsumeException ex)
                {
                    _logger.LogError(ex, "Erro ao consumir mensagem do Kafka");
                }
                catch (JsonException ex)
                {
                    _logger.LogError(ex, "Erro ao desserializar CreatedUserEvent");
                }
            }

            consumer.Close();
        }

        private async Task CreateTopic()
        {
            var adminConfig = new AdminClientConfig { BootstrapServers = _kafkaSettings.BootstrapServers };
            using var admin = new AdminClientBuilder(adminConfig).Build();

            var specs = Enum.GetValues<MailKafkaTopics>()
                            .Select(t => new TopicSpecification
                            {
                                Name = t.GetTopicName(),
                                NumPartitions = 1,
                                ReplicationFactor = 1
                            })
                            .ToArray();
            try
            {
                await admin.CreateTopicsAsync(specs);

                _logger.LogInformation("Tópicos de mail verificados/criados: {Topics}",
                    string.Join(", ", specs.Select(s => s.Name)));
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

        private async Task ProcessUserRegistered(string json)
        {
            _logger.LogDebug("JSON recebido no MailConsumer: {Json}", json);

            var evt = JsonSerializer.Deserialize<CreatedUserEventDto>(json, _jsonOptions);
            if (evt == null) return;

            _logger.LogDebug("evento:", evt);

            using var scope = _serviceProvider.CreateScope();
            var mailService = scope.ServiceProvider.GetRequiredService<IMailService>();

            // 1) E-mail de boas-vindas ao usuário
            var welcome = new SendMailDto
            {
                To = evt.Email,
                Subject = "Bem-vindo ao Mujapira!",
                Body = $"Olá {evt.Name}, seja bem-vindo ao Mujapira!",
                IsHtml = false
            };
            await mailService.Send(welcome);

            // 2) Notificação interna para o admin
            var notify = new SendMailDto
            {
                To = "mujapira@gmail.com",
                Subject = "🔔 Novo usuário registrado",
                Body = $"Usuário ID: {evt.Id}\nNome: {evt.Name}\nEmail: {evt.Email}",
                IsHtml = false
            };
            await mailService.Send(notify);

            _logger.LogInformation("E-mails de UserRegistered enviados: {Email}", evt.Email);
        }
    }
}
