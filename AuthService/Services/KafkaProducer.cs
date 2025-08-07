using Confluent.Kafka;
using Contracts.Common;
using Microsoft.Extensions.Options;
using System.Text.Json;

namespace AuthService.Services;

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

    public void Dispose()
    {
        _producer.Flush(TimeSpan.FromSeconds(5));

        _producer.Dispose();

        GC.SuppressFinalize(this);
    }
}
