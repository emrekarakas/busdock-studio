namespace BusDockStudio.Core.Models;

public enum EntityKind
{
    Queue,
    Topic,
    Subscription
}

public enum MessageSubqueue
{
    Active,
    DeadLetter,
    TransferDeadLetter
}

public sealed record EntityDescriptor(
    string Name,
    EntityKind Kind,
    string? ParentName = null,
    bool IsPartitioned = false,
    long? ApproximateActiveCount = null,
    long? ApproximateDeadLetterCount = null)
{
    public string EntityPath => Kind == EntityKind.Subscription
        ? $"{ParentName}/Subscriptions/{Name}"
        : Name;
}

public sealed record MessageSource(
    string EntityName,
    EntityKind EntityKind,
    string? TopicName,
    MessageSubqueue Subqueue,
    bool IsPartitioned,
    long? ApproximateCount = null)
{
    public string DisplayName => EntityKind == EntityKind.Subscription
        ? $"{TopicName}/{EntityName}"
        : EntityName;
}
