namespace Contracts.Common;

public interface IKafkaProducer
{
    Task Produce<T>(string topic, T message);
}

