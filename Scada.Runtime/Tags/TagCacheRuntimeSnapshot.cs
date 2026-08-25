namespace Scada.Runtime.Tags;

public sealed record TagCacheRuntimeSnapshot(
    long Updates,
    long CallbackInvocations,
    long SubscriberExceptions,
    int ValueCount,
    int SubscriptionCount)
{
    public bool MetricsAvailable { get; init; }
}
