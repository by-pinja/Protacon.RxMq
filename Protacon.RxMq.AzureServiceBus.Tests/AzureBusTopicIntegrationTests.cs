using System;
using System.Collections.Generic;
using System.Reactive.Linq;
using System.Threading;
using FluentAssertions;
using Microsoft.Azure.ServiceBus;
using Microsoft.Extensions.Logging;
using NSubstitute;
using Protacon.RxMq.AzureServiceBus.Tests.Messages;
using Protacon.RxMq.AzureServiceBus.Topic;
using Xunit;

namespace Protacon.RxMq.AzureServiceBus.Tests
{
    public class AzureBusTopicIntegrationTests
    {
        [Fact]
        public async void WhenMessageIsSend_ThenItCanBeReceived()
        {
            var settings = TestSettings.TopicSettingsOptions();
            var subscriber = new AzureTopicSubscriber(settings, new AzureBusTopicManagement(settings), Substitute.For<ILogger<AzureTopicSubscriber>>());
            var publisher = new AzureTopicPublisher(settings, new AzureBusTopicManagement(settings), Substitute.For<ILogger<AzureTopicPublisher>>());

            var id = Guid.NewGuid();
            var listener = subscriber.Messages<TestMessageForTopic>();

            await publisher.SendAsync(new TestMessageForTopic
            {
                ExampleId = id
            });

            await listener
                .Where(x => x.ExampleId == id)
                .Timeout(TimeSpan.FromSeconds(10))
                .FirstAsync();
        }

        [Fact]
        public async void WhenFiltersAreSet_ThenDontReturnInvalidTenantMessages()
        {
            // Arrrange.
            var correctTenantId = Guid.NewGuid();
            var invalidTenantId = Guid.NewGuid();

            var settings = TestSettings.TopicSettingsOptions(s =>
            {
                s.AzureMessagePropertyBuilder = message => new Dictionary<string, object> { { "tenant", ((TestMessageForTopic)message).TenantId } };
                s.AzureSubscriptionRules.Clear();
                s.AzureSubscriptionRules.Add("filter", new SqlFilter($"user.tenant='{correctTenantId}'"));
            });

            var publisher = new AzureTopicPublisher(settings, new AzureBusTopicManagement(settings), Substitute.For<ILogger<AzureTopicPublisher>>());
            var subscriber = new AzureTopicSubscriber(settings, new AzureBusTopicManagement(settings), Substitute.For<ILogger<AzureTopicSubscriber>>());

            var id = Guid.NewGuid();
            var invalidTenantMessageId = Guid.NewGuid();
            var listener = subscriber.Messages<TestMessageForTopic>();

            // Act.
            await publisher.SendAsync(new TestMessageForTopic
            {
                ExampleId = id,
                TenantId = correctTenantId.ToString(),
                Something = "valid"
            });

            await publisher.SendAsync(new TestMessageForTopic
            {
                ExampleId = invalidTenantMessageId,
                TenantId = invalidTenantId.ToString(),
                Something = "invalid"
            });

            // Assert.
            await listener
                .Where(x => x.ExampleId == id)
                .Timeout(TimeSpan.FromSeconds(10))
                .FirstAsync();

            listener
                .Where(x => x.ExampleId == invalidTenantMessageId)
                .Timeout(TimeSpan.FromSeconds(10))
                .Invoking(x => x.FirstAsync().Wait())
                .Should()
                .Throw<TimeoutException>();
        }

        [Fact]
        public async void WhenTopicIsRemoved_ThenTopicIsCreatedAgain()
        {
            var settings = TestSettings.TopicSettingsOptions();

            var subscriber = new AzureTopicSubscriber(settings, new AzureBusTopicManagement(settings), Substitute.For<ILogger<AzureTopicSubscriber>>());
            var publisher = new AzureTopicPublisher(settings, new AzureBusTopicManagement(settings), Substitute.For<ILogger<AzureTopicPublisher>>());

            var id = Guid.NewGuid();
            var listener = subscriber.Messages<TestMessageForTopic>();

            var message = new TestMessageForTopic
            {
                ExampleId = id
            };

            var nameSpace = NamespaceUtils.GetNamespace(settings.Value);
            var topic = await nameSpace.Topics.GetByNameAsync(message.TopicName);
            topic.Should().NotBeNull("Topic should exist at this point");

            await nameSpace.Topics.DeleteByNameAsync(message.TopicName);

            Thread.Sleep(TimeSpan.FromSeconds(30));

            await publisher.SendAsync(message);

            await listener
                .Where(x => x.ExampleId == id)
                .Timeout(TimeSpan.FromSeconds(60))
                .FirstAsync();
        }

        [Fact]
        public async void WhenTopicContainsValue_ObjectSettingsAreUsed()
        {
            var settings = TestSettings.TopicSettingsOptions();
            var subscriber = new AzureTopicSubscriber(settings, new AzureBusTopicManagement(settings), Substitute.For<ILogger<AzureTopicSubscriber>>());
            var clientWithoutMode = subscriber.Client<TestMessageForTopic>();
            var clientWithMode = subscriber.Client<ConfigurableTestMessageForTopic>();
            
            Assert.Equal(ReceiveMode.PeekLock, clientWithoutMode.ReceiveMode);
            Assert.Equal(0, clientWithoutMode.PrefetchCount);
            Assert.Equal(100, clientWithMode.PrefetchCount);
        }
    }
}
