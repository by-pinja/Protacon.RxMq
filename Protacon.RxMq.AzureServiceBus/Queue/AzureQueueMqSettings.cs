using System;
using Protacon.RxMq.Abstractions.DefaultMessageRouting;

namespace Protacon.RxMq.AzureServiceBus.Queue
{
    public class AzureBusQueueSettings: AzureMqSettingsBase
    {
        public int DefaultPrefetchCount { get; set; } = 0;
        public Func<Type, string> QueueNameBuilderForSubscriber { get; set; } = type =>
        {
            var instance = Activator.CreateInstance(type);

            if (instance is IQueueItem)
            {
                return ((IQueueItem)instance).QueueName;
            }

            throw new InvalidOperationException($"Default implementation of queue name builder expects used objects to extend '{nameof(IQueueItem)}'");
        };

        public Func<object, string> QueueNameBuilderForPublisher { get; set; } = instance =>
        {
            if (instance is IQueueItem)
            {
                return ((IQueueItem)instance).QueueName;
            }

            throw new InvalidOperationException($"Default implementation of queue name builder expects used objects to extend '{nameof(IQueueItem)}'");
        };

        public Action<Microsoft.Azure.Management.ServiceBus.Fluent.Queue.Definition.IBlank, Type> AzureQueueBuilder { get; set; } = (queu, messageType) =>
        {
            queu
                .WithSizeInMB(1024)
                .WithDefaultMessageTTL(TimeSpan.FromSeconds(60*5))
                .WithMessageMovedToDeadLetterQueueOnMaxDeliveryCount(10)
                .WithMessageLockDurationInSeconds(30)
                .Create();
        };
    }
}
