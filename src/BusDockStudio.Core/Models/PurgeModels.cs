namespace BusDockStudio.Core.Models;

public sealed record PurgeRequest(
    MessageSource Source,
    string NamespaceName,
    string TypedConfirmation,
    long? ApproximateMessageCount,
    TimeSpan Timeout);

public enum PurgeStatus
{
    Succeeded,
    PartiallySucceeded,
    Cancelled,
    TimedOut,
    Rejected,
    Failed
}

public sealed record PurgeProgress(long DeletedCount, string Phase);

public sealed record PurgeResult(
    PurgeStatus Status,
    long DeletedCount,
    DateTimeOffset CutoffUtc,
    string Message,
    bool MayBePartiallyCompleted = false);

public sealed record PurgeCommand(MessageSource Source, DateTimeOffset CutoffUtc);

public sealed record PurgeAdapterResult(long DeletedCount);

public sealed class PartialPurgeException(long deletedCount, string message, Exception? innerException = null)
    : Exception(message, innerException)
{
    public long DeletedCount { get; } = deletedCount;
}
