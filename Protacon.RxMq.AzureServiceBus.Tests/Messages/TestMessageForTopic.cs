using System;
using Protacon.RxMq.Abstractions;
using Protacon.RxMq.Abstractions.DefaultMessageRouting;

namespace Protacon.RxMq.AzureServiceBus.Tests.Messages
{
    public class TestMessageForTopic: ITopicItem
    {
        public Guid ExampleId { get; set; }
        public string Something { get; set; }
        public string TenantId { get; set; }
        public string TopicName => "v1.testtopic";
        public long? Arrival { get; set; }
    }
}
