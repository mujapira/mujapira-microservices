using Confluent.Kafka;
using Microsoft.Extensions.Options;
using Contracts.Common;

namespace AuthService.Services;

public class KafkaProducer : IKafkaProducer, IDisposable
{
    private readonly IProducer<Null, string> _producer;
    private readonly string _topic;

    public KafkaProducer(IOptions<KafkaSettings> opts)
    {
        var cfg = new ProducerConfig { BootstrapServers = opts.Value.BootstrapServers };
        _producer = new ProducerBuilder<Null, string>(cfg).Build();
        _topic = opts.Value.Topic;
    }

    public async Task Produce(string message)
    {
        await _producer.ProduceAsync(_topic, new Message<Null, string> { Value = message });
    }

    public void Dispose()
    {
        _producer.Flush(TimeSpan.FromSeconds(5));

        _producer.Dispose();

        GC.SuppressFinalize(this);
    }
}
