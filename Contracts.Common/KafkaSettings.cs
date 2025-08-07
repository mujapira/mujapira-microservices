using System.ComponentModel;
using System.Reflection;

namespace Contracts.Common
{
    public class KafkaSettings
    {
        public string BootstrapServers { get; set; } = null!;
        public string Topic { get; set; } = null!;
    }
    public enum LogKafkaTopics
    {
        [Description("logs")] Logs,
        [Description("users")] Users,
        [Description("auth")] Auth,
        [Description("mail")] Mail
    }

    public enum MailKafkaTopics
    {
        [Description("user-registered")] UserRegistered
    }

    public static class KafkaTopicsExtensions
    {
        public static string GetTopicName(this Enum topic)
        {
            var fi = topic.GetType().GetField(topic.ToString())!;
            var attr = fi.GetCustomAttribute<DescriptionAttribute>();
            return attr?.Description ?? topic.ToString().ToLowerInvariant();
        }
    }

}
