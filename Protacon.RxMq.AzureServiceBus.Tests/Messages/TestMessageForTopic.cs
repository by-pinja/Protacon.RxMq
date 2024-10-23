using System;
using Newtonsoft.Json;
using Protacon.RxMq.Abstractions.DefaultMessageRouting;

namespace Protacon.RxMq.AzureServiceBus.Tests.Messages
{
    public class TestMessageForTopic: ITopicItem
    {
        public Guid ExampleId { get; set; }
        public string Something { get; set; }
        public string TenantId { get; set; }
        public string TopicName => "v1.testtopic";

        [JsonProperty("rxmqarrival")]
        public long? RxMqArrival { get; set; }
    }
}
