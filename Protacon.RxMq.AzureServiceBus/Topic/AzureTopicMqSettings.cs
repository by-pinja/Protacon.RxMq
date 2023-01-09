using System;
using System.Collections.Generic;
using Microsoft.Azure.ServiceBus;
using Protacon.RxMq.Abstractions.DefaultMessageRouting;

namespace Protacon.RxMq.AzureServiceBus.Topic
{
    public class AzureBusTopicSettings : AzureMqSettingsBase
    {
        public string TopicSubscriberId { get; set; } = Environment.MachineName;
        public int DefaultPrefetchCount { get; set; } = 0;
        public string DefaultReceiveMode { get; set; } = "PeekLock";
        public bool AddArrival { get; set; }

        public Func<Type, string> TopicNameBuilder { get; set; } = type =>
        {
            var instance = Activator.CreateInstance(type);

            if (instance is ITopicItem t)
            {
                return t.TopicName;
            }

            throw new InvalidOperationException($"Default implementation of topic name builder expects used objects to extend '{nameof(ITopicItem)}'");
        };

        public Func<Type, Tuple<string, int?, string>> TopicConfigBuilder { get; set; } = type =>
        {
            var instance = Activator.CreateInstance(type);

            if (instance is IConfigurableTopicItem cti)
            {
                return new Tuple<string, int?, string>(cti.TopicName, cti.PrefetchCount, cti.ReceiveMode);
            }

            if (instance is ITopicItem t)
            {
                return new Tuple<string, int?, string>(t.TopicName, null, "");
            }

            throw new InvalidOperationException($"Default implementation of topic configuration builder expects used objects to extend '{nameof(ITopicItem)}' or '{nameof(IConfigurableTopicItem)}'");
        };

        public Action<Microsoft.Azure.Management.ServiceBus.Fluent.Topic.Definition.IBlank, Type> AzureTopicBuilder { get; set; } = (create, messageType) =>
        {
            create
                .WithSizeInMB(1024)
                .WithDefaultMessageTTL(TimeSpan.FromSeconds(60 * 5))
                .Create();
        };

        public Action<Microsoft.Azure.Management.ServiceBus.Fluent.Subscription.Definition.IBlank, Type> AzureSubscriptionBuilder { get; set; } = (create, messageType) =>
        {
            create
                .WithDefaultMessageTTL(TimeSpan.FromSeconds(60 * 5))
                .Create();
        };

        public Dictionary<string, Filter> AzureSubscriptionRules { get; set; } = new Dictionary<string, Filter> { { "getEverything", new TrueFilter() } };
        public Func<object, Dictionary<string, object>> AzureMessagePropertyBuilder { get; set; } = message => new Dictionary<string, object>();
    }
}
