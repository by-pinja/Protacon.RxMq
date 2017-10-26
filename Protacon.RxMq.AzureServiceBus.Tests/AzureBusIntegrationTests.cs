using System;
using System.Reactive.Linq;
using FluentAssertions;
using Microsoft.Extensions.Logging;
using NSubstitute;
using Protacon.RxMq.AzureServiceBus.Tests.Messages;
using Xunit;

namespace Protacon.RxMq.AzureServiceBus.Tests
{
    public class AzureBusIntegrationTests
    {
        [Fact]
        public void WhenMessageIsSend_ThenItCanBeReceived()
        {
            var bus = new AzureBusMq(TestSettings.MqSettings, Substitute.For<ILogger<AzureBusMq>>());

            var id = Guid.NewGuid();

            bus.SendAsync(new TestMessage
            {
                ExampleId = Guid.NewGuid()
            }).Wait();

            bus.Messages<TestMessage>()
                .Where(x => x.Message.ExampleId == id)
                .Timeout(TimeSpan.FromSeconds(5));
        }

        [Fact(Skip = "TODO: This is actually kind of hard requirement to fullfill with current state of library.")]
        public void WhenQueueDoesntExistYet_ThenCreateNew()
        {
            var bus = new AzureBusMq(TestSettings.MqSettings, Substitute.For<ILogger<AzureBusMq>>());

            var message = new TestMessage
            {
                ExampleId = Guid.NewGuid()
            };

            message.SetNewRoutingKeyForTesting($"{message.RoutingKey}-{Guid.NewGuid()}");

            bus.Invoking(x => x.SendAsync(message).Wait()).Should().NotThrow<Exception>();
        }
    }
}