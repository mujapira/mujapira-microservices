using Confluent.Kafka;
using Contracts.Common;
using Microsoft.Extensions.Options;
using System.Diagnostics;
using System.Text.Json;

namespace IdentityService.Services;

public class KafkaProducer : IKafkaProducer, IDisposable
{
    private readonly IProducer<Null, string> _producer;
    public KafkaProducer(IOptions<KafkaSettings> opts)
    {
        var cfg = new ProducerConfig
        {
            BootstrapServers = opts.Value.BootstrapServers
        };
        _producer = new ProducerBuilder<Null, string>(cfg).Build();
    }

    public async Task Produce<T>(string topic, T message)
    {
        var payload = JsonSerializer.Serialize(message);

        await _producer.ProduceAsync(topic, new Message<Null, string> { Value = payload });
    }
    public void ProduceFireAndForget<T>(string topic, T message)
    {
        var payload = JsonSerializer.Serialize(message);

        _producer.Produce(topic, new Message<Null, string> { Value = payload }, deliveryReport =>
        {
            if (deliveryReport.Error.IsError)
            {
                Console.WriteLine($"Erro ao enviar para {topic}: {deliveryReport.Error.Reason}");
            }
            else
            {
                Console.WriteLine($"Mensagem enviada para {topic} [{deliveryReport.Partition}] offset {deliveryReport.Offset}");
            }
        });
    }

    public void Dispose()
    {
        _producer.Flush(TimeSpan.FromSeconds(5));

        _producer.Dispose();

        GC.SuppressFinalize(this);
    }
}
