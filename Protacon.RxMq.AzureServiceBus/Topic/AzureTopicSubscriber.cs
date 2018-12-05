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
    public class AzureTopicSubscriber: IMqTopicSubscriber
    {
        private readonly AzureBusTopicSettings _settings;
        private readonly AzureBusTopicManagement _queueManagement;
        private readonly ILogger<AzureTopicSubscriber> _logging;
        private readonly Dictionary<Type, IDisposable> _bindings = new Dictionary<Type, IDisposable>();
        
        private readonly BlockingCollection<IBinding> _errorActions = new BlockingCollection<IBinding>(1);
        private readonly CancellationTokenSource _source;

        private class Binding<T>: IDisposable, IBinding where T: new()
        {
            internal Binding(AzureBusTopicSettings settings, ILogger<AzureTopicSubscriber> logging,
                AzureBusTopicManagement queueManagement, BlockingCollection<IBinding> errorActions)
            {
                var topicName = settings.TopicNameBuilder(typeof(T));
                var subscriptionName = $"{topicName}.{settings.TopicSubscriberId}";

                queueManagement.CreateSubscriptionIfMissing(topicName, subscriptionName, typeof(T));

                var subscriptionClient = new SubscriptionClient(settings.ConnectionString, topicName, subscriptionName);
                UpdateRules(subscriptionClient, settings);

                subscriptionClient.RegisterMessageHandler(
                    async (message, _) =>
                    {
                        try
                        {
                            var body = Encoding.UTF8.GetString(message.Body);

                            logging.LogInformation($"Received '{subscriptionName}': {body}");

                            var asObject = AsObject(body);

                            Subject.OnNext(asObject);
                        }
                        catch (Exception ex)
                        {
                            logging.LogError(ex, $"Message {subscriptionName}': {message} -> consumer error: {ex}");
                        }
                    }, new MessageHandlerOptions(async e =>
                    {
                        logging.LogError(e.Exception, $"At route '{subscriptionName}' error occurred: {e.Exception}.");
                        if (e.Exception is ServiceBusCommunicationException || e.Exception is MessagingEntityNotFoundException)
                        {
                            errorActions.Add(this);
                        }
                    }));
            }

            public void ReCreate(AzureBusTopicSettings settings, AzureBusTopicManagement queueManagement)
            {
                var topicName = settings.TopicNameBuilder(typeof(T));
                var subscriptionName = $"{topicName}.{settings.TopicSubscriberId}";

                queueManagement.CreateSubscriptionIfMissing(topicName, subscriptionName, typeof(T));

                var subscriptionClient = new SubscriptionClient(settings.ConnectionString, topicName, subscriptionName);
                UpdateRules(subscriptionClient, settings);
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

            private static T AsObject(string body)
            {
                var parsed = JObject.Parse(body);

                if (parsed["data"] == null)
                    throw new InvalidOperationException("Library expects data wrapped as { data: { ... } }");

                return parsed["data"].ToObject<T>();
            }

            public ReplaySubject<T> Subject { get; } = new ReplaySubject<T>(TimeSpan.FromSeconds(30));

            public void Dispose()
            {
                Subject?.Dispose();
            }
        }

        public AzureTopicSubscriber(IOptions<AzureBusTopicSettings> settings, AzureBusTopicManagement queueManagement, ILogger<AzureTopicSubscriber> logging)
        {
            _settings = settings.Value;
            _queueManagement = queueManagement;
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
                            action.ReCreate(_settings, _queueManagement);
                        }
                        catch (Exception exception)
                        {
                            logging.LogError(exception, "Unable to recreate subscription.");
                        }
                    }
                    catch (OperationCanceledException exception)
                    {
                        _logging.LogDebug(exception, $"Stopping {nameof(AzureTopicSubscriber)}");
                    }
                    catch (Exception exception)
                    {
                        _logging.LogError(exception, "Something went wrong while doing error actions.");
                    }
                }
            }, _source.Token);
        }
        
        public IObservable<T> Messages<T>() where T: new()
        {
            if(!_bindings.ContainsKey(typeof(T)))
                _bindings.Add(typeof(T), new Binding<T>(_settings, _logging, _queueManagement, _errorActions));

            return ((Binding<T>) _bindings[typeof(T)]).Subject;
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
            void ReCreate(AzureBusTopicSettings settings, AzureBusTopicManagement queueManagement);
        }
    }
}
