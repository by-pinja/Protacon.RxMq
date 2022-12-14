namespace Protacon.RxMq.Abstractions.DefaultMessageRouting
{
    public interface ITopicItem
    {
        string TopicName { get; }
    }

    public interface IConfigurableTopicItem : ITopicItem
    {
        int PrefetchCount { get; }
    }
}
