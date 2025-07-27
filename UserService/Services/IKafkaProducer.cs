namespace UserService.Services;

public interface IKafkaProducer
{
    Task ProduceAsync(string message);
}
