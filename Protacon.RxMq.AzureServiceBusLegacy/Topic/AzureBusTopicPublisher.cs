using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.ServiceBus;
using Microsoft.ServiceBus.Messaging;
using Newtonsoft.Json;
using Newtonsoft.Json.Serialization;
using Protacon.RxMq.Abstractions;

namespace Protacon.RxMq.AzureServiceBusLegacy.Topic
{
    public class AzureBusTopicPublisher : IMqTopicPublisher
    {
        private readonly AzureTopicMqSettings _settings;
        private readonly AzureTopicMqSettings _secondarySettings;
        private readonly Action<string> _logMessage;
        private readonly Action<string> _logError;

        private readonly Dictionary<string, IDisposable> _bindings = new Dictionary<string, IDisposable>();
        private readonly MessagingFactory _factory;
        private readonly MessagingFactory _secondaryFactory;
        private readonly NamespaceManager _namespaceManager;
        private readonly NamespaceManager _secondaryNamespaceManager;
        private const int TriesBeforeInterval = 5;
        private const int IntervalOfBindingRetry = 60;

        private class Binding<T> : IDisposable where T : new()
        {
            private readonly MessagingFactory _messagingFactory;
            private readonly MessagingFactory _secondaryMessagingFactory;
            private readonly Action<string> _logMessage;
            private readonly Action<string> _logError;
            private readonly AzureTopicMqSettings _azureTopicMqSettings;
            private readonly AzureTopicMqSettings _secondaryAzureTopicMqSettings;
            private readonly IList<string> _excludeTopicsFromLogging;

            internal Binding(
                MessagingFactory messagingFactory,
                NamespaceManager namespaceManager,
                AzureTopicMqSettings settings,
                MessagingFactory secondaryMessagingFactory,
                NamespaceManager secondaryNamespaceManager,
                AzureTopicMqSettings secondarySettings,
                string topicName,
                Action<string> logMessage, Action<string> logError)
            {
                _messagingFactory = messagingFactory;
                _secondaryMessagingFactory = secondaryMessagingFactory;
                _logMessage = logMessage;
                _logError = logError;
                _azureTopicMqSettings = settings;
                _secondaryAzureTopicMqSettings = secondarySettings;
                _excludeTopicsFromLogging = new LoggingConfiguration().ExcludeTopicsFromLogging();

                if (!namespaceManager.TopicExists(topicName))
                {
                    var queueDescription = new TopicDescription(topicName);
                    namespaceManager.CreateTopic(settings.TopicBuilderConfig(queueDescription, typeof(T)));
                }

                if (secondaryNamespaceManager != null)
                {
                    if (!secondaryNamespaceManager.TopicExists(topicName))
                    {
                        var queueDescription = new TopicDescription(topicName);
                        secondaryNamespaceManager.CreateTopic(secondarySettings.TopicBuilderConfig(queueDescription, typeof(T)));
                    }
                }
            }

            public Task SendAsync(T message, string topicName)
            {
                var sender = _messagingFactory.CreateMessageSender(topicName);

                var body =
                    JsonConvert.SerializeObject(
                        new {Data = message},
                        Formatting.None,
                        new JsonSerializerSettings
                        {
                            ContractResolver = new CamelCasePropertyNamesContractResolver()
                        }
                    );

                var bytes = Encoding.UTF8.GetBytes(body);
                var stream = new MemoryStream(bytes, writable: false);

                var brokeredMessage = new BrokeredMessage(stream)
                {
                    ContentType = "application/json"
                };

                _azureTopicMqSettings.AzureMessagePropertyBuilder(message)
                    .ToList()
                    .ForEach(x => brokeredMessage.Properties.Add(x.Key, x.Value));

                MessageSender secondarySender = null;
                BrokeredMessage secondaryBrokeredMessage = null;
                if (_secondaryMessagingFactory != null && _secondaryAzureTopicMqSettings != null)
                {
                    secondarySender = _secondaryMessagingFactory.CreateMessageSender(topicName);

                    secondaryBrokeredMessage = new BrokeredMessage(stream)
                    {
                        ContentType = "application/json"
                    };
                    _secondaryAzureTopicMqSettings.AzureMessagePropertyBuilder(message)
                        .ToList()
                        .ForEach(x => secondaryBrokeredMessage.Properties.Add(x.Key, x.Value));
                }
                

                if (!_excludeTopicsFromLogging.Contains(topicName))
                {
                    _logMessage($"{nameof(SendAsync)}/{topicName} sending message '{body}' with Azure MessageId: '{brokeredMessage.MessageId}'");
                    if (secondaryBrokeredMessage != null)
                    {
                        _logMessage($"{nameof(SendAsync)}/{topicName} sending message '{body}' with Azure MessageId: '{secondaryBrokeredMessage.MessageId}'");
                    }                    
                }

                return new Task(() => {
                    var s1 = sender.SendAsync(brokeredMessage)
                        .ContinueWith(task =>
                        {
                            if (task.Exception != null)
                            {
                                _logError($"{nameof(SendAsync)}/{topicName} error occurred: {task.Exception}");
                            }

                            return task;
                        });
                    Task<Task> s2 = null;
                    if (secondarySender != null)
                    {
                        s2 = secondarySender.SendAsync(secondaryBrokeredMessage)
                            .ContinueWith(task =>
                            {
                                if (task.Exception != null)
                                {
                                    _logError($"{nameof(SendAsync)}/{topicName} error occurred: {task.Exception}");
                                }

                                return task;
                            });
                    }
                    if (s2 == null)
                    {
                        s1.Wait();
                    }
                    else
                    {
                        Task.WaitAll(s1, s2);
                    }                    
                });
            }

            public void Dispose()
            {
            }
        }

        public AzureBusTopicPublisher(AzureTopicMqSettings settings, Action<string> logMessage, Action<string> logError, AzureTopicMqSettings secondarySettings = null)
        {
            _settings = settings;
            _logMessage = logMessage;
            _logError = logError;
            _factory = MessagingFactory.CreateFromConnectionString(settings.ConnectionString);
            _namespaceManager =
                NamespaceManager.CreateFromConnectionString(settings.ConnectionString);

            if (secondarySettings != null)
            {
                _secondarySettings = secondarySettings;
                _secondaryFactory = MessagingFactory.CreateFromConnectionString(secondarySettings.ConnectionString);
                _secondaryNamespaceManager =
                    NamespaceManager.CreateFromConnectionString(secondarySettings.ConnectionString);
            }
        }

        public Task SendAsync<T>(T message) where T : new()
        {
            var queueName = _settings.TopicNameBuilder(message.GetType());

            if (!_bindings.ContainsKey(queueName))
                return TryCreateBinding(_settings, _bindings, queueName, typeof(T), message, TriesBeforeInterval, TimeSpan.FromSeconds(IntervalOfBindingRetry));

            return ((Binding<T>)_bindings[queueName]).SendAsync(message, queueName);
        }

        /// <summary>
        /// Creates new Binding for topic in a safe way. If initial tries fail, it will fallback to intervalled retrys.
        /// </summary>
        /// <param name="topic">Name of the topic to bind</param>
        /// <param name="type">Message type for the topic builder</param>
        /// <param name="message">Message to send after binding</param>
        /// <param name="instantRecoveryTries">Number of instant tries for binding. Fallbacks to <see cref="lifeCycleRecoveryInterval"/>interval.</param>
        /// <param name="lifeCycleRecoveryInterval">Interval to try binding in seconds.</param>
        private Task TryCreateBinding<T>(AzureTopicMqSettings settings, Dictionary<string, IDisposable> bindings, string topic, Type type, T message, int instantRecoveryTries, TimeSpan lifeCycleRecoveryInterval) where T : new()
        {
            CancellationTokenSource cancellation = new CancellationTokenSource();
            int lifeCycleTryCount = 0;
            var queueName = settings.TopicNameBuilder(message.GetType());

            for (var i = 1; i <= instantRecoveryTries; i++)
            {
                var binding = TryBinding(topic, type, message);
                if (binding != null)
                {
                    _logMessage($"{nameof(TryCreateBinding)}: Binding successful ('{topic}', binding {i} of {instantRecoveryTries})");
                    bindings.Add(topic, binding);
                    return ((Binding<T>) bindings[queueName]).SendAsync(message, queueName);
                }
            }

            _logMessage($"{nameof(TryCreateBinding)}: Could not create binding in instantRecoveryTries tries ('{topic}', {instantRecoveryTries} tries). " +
                $"Trying again every {lifeCycleRecoveryInterval} sec.");

            RepeatActionEvery(TryLifeCycleBinding, lifeCycleRecoveryInterval, cancellation.Token)
                .Wait();
            return ((Binding<T>)bindings[queueName]).SendAsync(message, queueName);

            void TryLifeCycleBinding()
            {
                lifeCycleTryCount++;
                _logMessage($"{nameof(TryCreateBinding)}: Try {lifeCycleTryCount} of lifeCycleRecoveryInterval ('{topic}')");
                var binding = TryBinding(topic, type, message);
                if (binding != null)
                {
                    _logMessage($"{nameof(TryCreateBinding)}: Binding successful ('{topic}', binding {lifeCycleTryCount} of lifeCycleRecoveryInterval)");
                    bindings.Add(topic, binding);
                    cancellation.Cancel();
                }
            }
        }

        private Binding<T> TryBinding<T>(string topic, Type type, T message) where T : new()
        {
            try
            {
                if (!_namespaceManager.TopicExists(topic))
                {
                    var queueDescription = new TopicDescription(topic);
                    _namespaceManager.CreateTopic(_settings.TopicBuilderConfig(queueDescription, type));
                }
                if (!_secondaryNamespaceManager.TopicExists(topic))
                {
                    var queueDescription = new TopicDescription(topic);
                    _secondaryNamespaceManager.CreateTopic(_secondarySettings.TopicBuilderConfig(queueDescription, type));
                }
                var queueName = _settings.TopicNameBuilder(message.GetType());
                return new Binding<T>(_factory, _namespaceManager, _settings, _secondaryFactory, _secondaryNamespaceManager, _secondarySettings, queueName, _logMessage, _logError);
            }
            catch (Exception e)
            {
                _logError($"{nameof(TryBinding)}: Calling recovery on topic '{topic}' for new Binding. Cause: error occurred {e}");
                return null;
            }
        }

        private static async Task RepeatActionEvery(Action action, TimeSpan interval, CancellationToken cancellationToken)
        {
            while (true)
            {
                action();
                Task task = Task.Delay(interval, cancellationToken);
                try
                {
                    await task;
                }
                catch (TaskCanceledException)
                {
                    return;
                }
            }
        }
    }
}