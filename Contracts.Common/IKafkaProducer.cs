namespace Contracts.Common;

public interface IKafkaProducer
{
    Task Produce(string message);
}
