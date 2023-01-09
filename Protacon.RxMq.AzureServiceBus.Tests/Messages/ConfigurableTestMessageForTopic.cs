using Protacon.RxMq.Abstractions.DefaultMessageRouting;

namespace Protacon.RxMq.AzureServiceBus.Tests.Messages
{
    public class ConfigurableTestMessageForTopic: IConfigurableTopicItem
    {
        public string TopicName => "v1.ctesttopic";
        public int PrefetchCount => 100;
        public string ReceiveMode => "ReceiveAndDelete";
    }
}