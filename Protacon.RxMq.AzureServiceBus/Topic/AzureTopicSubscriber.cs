using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Reactive.Subjects;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Azure.ServiceBus;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Newtonsoft.Json.Linq;
using Protacon.RxMq.Abstractions;

namespace Protacon.RxMq.AzureServiceBus.Topic
{
    public class AzureTopicSubscriber : IMqTopicSubscriber
    {
        internal class SubscriptionClientOptions {
            public string TopicName { get; set; }
            public string ConnectionString { get; set; }
            public string SubscriptionName { get; set; }
            public int PrefetchCount { get; set; }
            public ReceiveMode ReceiveMode { get; set; }
        }
        
        private readonly AzureBusTopicSettings _settings;
        private readonly AzureBusTopicManagement _topicManagement;
        private readonly ILogger<AzureTopicSubscriber> _logging;
        private readonly ConcurrentDictionary<Type, IDisposable> _bindings = new ConcurrentDictionary<Type, IDisposable>();

        private readonly BlockingCollection<IBinding> _errorActions = new BlockingCollection<IBinding>(1);
        private readonly CancellationTokenSource _source;
        
        private class Binding<T> : IDisposable, IBinding where T : new()
        {
            private readonly IList<string> _excludeTopicsFromLogging;

            internal Binding(AzureBusTopicSettings settings, ILogger<AzureTopicSubscriber> logging,
                AzureBusTopicManagement topicManagement, BlockingCollection<IBinding> errorActions)
            {
                _excludeTopicsFromLogging = new LoggingConfiguration().ExcludeTopicsFromLogging();
                var (topicName, prefetchCount, receiveModeCode) = settings.TopicConfigBuilder(typeof(T));
                var subscriptionName = $"{topicName}.{settings.TopicSubscriberId}";

                topicManagement.CreateSubscriptionIfMissing(topicName, subscriptionName, typeof(T));

                var subscriptionClient = CreateClient(new SubscriptionClientOptions
                {
                    ConnectionString = settings.ConnectionString,
                    TopicName = topicName,
                    SubscriptionName = subscriptionName,
                    PrefetchCount = prefetchCount ?? settings.DefaultPrefetchCount,
                    ReceiveMode = ParseReceiveMode(receiveModeCode, settings.DefaultReceiveMode, logging)
                });
                
                UpdateRules(subscriptionClient, settings);

                subscriptionClient.RegisterMessageHandler(
                    async (message, _) =>
                    {
                        try
                        {
                            long arrival = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
                            var body = Encoding.UTF8.GetString(message.Body);

                            if (!_excludeTopicsFromLogging.Contains(topicName))
                            {
                                logging.LogInformation("Received '{subscription}': {body} with Azure MessageId: '{messageId}'. Arrival time in UnixTimeMilliseconds: '{arrival}'", subscriptionName, body, message.MessageId, arrival);
                            }

                            var asObject = AsObject(body, arrival, settings.AddArrival);

                            Subject.OnNext(asObject);
                        }
                        catch (Exception ex)
                        {
                            logging.LogError(ex, "Message {subscription}': {message} -> consumer error: {exception}", subscriptionName, message, ex);
                        }
                    }, new MessageHandlerOptions(async e =>
                    {
                        logging.LogError(e.Exception, "At route '{subscription}' error occurred: {exception}.", subscriptionName, e.Exception);
                        if (e.Exception is ServiceBusCommunicationException || e.Exception is MessagingEntityNotFoundException)
                        {
                            errorActions.Add(this);
                        }
                    }));

                Client = subscriptionClient;
                logging.LogInformation("Created SubscriptionClient for '{topicName}' with prefetchCount '{prefetchCount}' and receiveMode '{receiveMode}'", 
                    topicName, subscriptionClient.PrefetchCount, subscriptionClient.ReceiveMode);
            }

            public void ReCreate(AzureBusTopicSettings settings, AzureBusTopicManagement topicManagement)
            {
                var (topicName, prefetchCount, receiveModeCode) = settings.TopicConfigBuilder(typeof(T));
                var subscriptionName = $"{topicName}.{settings.TopicSubscriberId}";

                topicManagement.CreateSubscriptionIfMissing(topicName, subscriptionName, typeof(T));

                var subscriptionClient = CreateClient(new SubscriptionClientOptions
                {
                    ConnectionString = settings.ConnectionString,
                    TopicName = topicName,
                    SubscriptionName = subscriptionName,
                    PrefetchCount = prefetchCount ?? settings.DefaultPrefetchCount,
                    ReceiveMode = ParseReceiveMode(receiveModeCode, settings.DefaultReceiveMode, null)
                });
                
                UpdateRules(subscriptionClient, settings);
                Client = subscriptionClient;
            }

            private void UpdateRules(SubscriptionClient subscriptionClient, AzureBusTopicSettings settings)
            {
                subscriptionClient.GetRulesAsync()
                    .Result
                    .ToList()
                    .ForEach(x => subscriptionClient.RemoveRuleAsync(x.Name).Wait());

                settings.AzureSubscriptionRules
                    .ToList()
                    .ForEach(x => subscriptionClient.AddRuleAsync(x.Key, x.Value).Wait());
            }

            private static T AsObject(string body, long arrival, bool addArrival = false)
            {
                var parsed = JObject.Parse(body);

                if (parsed["data"] == null)
                    throw new InvalidOperationException("Library expects data wrapped as { data: { ... } }");

                if (addArrival) {
                    parsed["data"]["rxmqarrival"] = arrival;
                }

                return parsed["data"].ToObject<T>();
            }

            private static SubscriptionClient CreateClient(SubscriptionClientOptions options)
            {
                var client = new SubscriptionClient(options.ConnectionString, options.TopicName, options.SubscriptionName, options.ReceiveMode);
                client.PrefetchCount = options.PrefetchCount;

                return client;
            }
            
            private static ReceiveMode ParseReceiveMode(string topicReceiveModeCode, string defaultReceiveModeCode, ILogger<AzureTopicSubscriber> logging = null)
            {
                if (Enum.TryParse<ReceiveMode>(topicReceiveModeCode, out var topicMode))
                {
                    return topicMode;
                }
                logging?.LogDebug("Invalid receive mode '{topicReceiveModeCode}' provided from topic. Trying settings.", topicReceiveModeCode);
                
                if (Enum.TryParse<ReceiveMode>(defaultReceiveModeCode, out var defaultMode))
                {
                    return defaultMode;
                }
                logging?.LogWarning("Invalid receive mode '{defaultReceiveModeCode}' provided from settings. Defaulting to 'PeekLock'", defaultReceiveModeCode);
                
                return ReceiveMode.PeekLock;
            }

            public ReplaySubject<T> Subject { get; } = new ReplaySubject<T>(TimeSpan.FromSeconds(30));
            public SubscriptionClient Client { get; set; }

            public void Dispose()
            {
                Subject?.Dispose();
                Client = null;
            }
        }

        public AzureTopicSubscriber(IOptions<AzureBusTopicSettings> settings, AzureBusTopicManagement topicManagement, ILogger<AzureTopicSubscriber> logging)
        {
            _settings = settings.Value;
            _topicManagement = topicManagement;
            _logging = logging;

            _source = new CancellationTokenSource();
            Task.Factory.StartNew(() =>
            {
                while (!_source.IsCancellationRequested)
                {
                    try
                    {
                        var action = _errorActions.Take(_source.Token);
                        try
                        {
                            action.ReCreate(_settings, _topicManagement);
                        }
                        catch (Exception exception)
                        {
                            logging.LogError(exception, "Unable to recreate subscription.");
                        }
                    }
                    catch (OperationCanceledException exception)
                    {
                        _logging.LogDebug(exception, "Stopping {className}", nameof(AzureTopicSubscriber));
                    }
                    catch (Exception exception)
                    {
                        _logging.LogError(exception, "Something went wrong while doing error actions.");
                    }
                }
            }, _source.Token);
        }

        public IObservable<T> Messages<T>() where T : new()
        {
            if (!_bindings.ContainsKey(typeof(T)))
            {
                _bindings.TryAdd(typeof(T), new Binding<T>(_settings, _logging, _topicManagement, _errorActions));
            }

            return ((Binding<T>)_bindings[typeof(T)]).Subject;
        }
        
        public SubscriptionClient Client<T>() where T : new()
        {
            if (!_bindings.ContainsKey(typeof(T)))
            {
                _bindings.TryAdd(typeof(T), new Binding<T>(_settings, _logging, _topicManagement, _errorActions));
            }

            return ((Binding<T>)_bindings[typeof(T)]).Client;
        }

        public void Dispose()
        {
            _source.Cancel();
            _bindings.Select(x => x.Value)
                .ToList()
                .ForEach(x => x.Dispose());
        }

        private interface IBinding
        {
            void ReCreate(AzureBusTopicSettings settings, AzureBusTopicManagement topicManagement);
        }
    }
}
