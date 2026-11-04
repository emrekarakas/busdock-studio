using System.Collections.ObjectModel;

namespace BusDockStudio.Core.Models;

public enum MessageBodyKind
{
    Json,
    Text,
    Binary
}

public sealed record MessageBody(byte[] Bytes, MessageBodyKind Kind, string DisplayText, string Base64);

public sealed record MessageRecord(
    long SequenceNumber,
    string MessageId,
    string? Subject,
    DateTimeOffset EnqueuedTime,
    DateTimeOffset? ScheduledEnqueueTime,
    DateTimeOffset ExpiresAt,
    int DeliveryCount,
    string State,
    string? SessionId,
    string? CorrelationId,
    string? ContentType,
    string? DeadLetterReason,
    string? DeadLetterErrorDescription,
    MessageBody Body,
    IReadOnlyDictionary<string, string> SystemProperties,
    IReadOnlyDictionary<string, string> ApplicationProperties)
{
    public static IReadOnlyDictionary<string, string> ReadOnly(IDictionary<string, string> values) =>
        new ReadOnlyDictionary<string, string>(values);
}

public sealed record PeekRequest(MessageSource Source, int PageSize, long? FromSequenceNumber = null);

public sealed record MessagePage(IReadOnlyList<MessageRecord> Messages, long? NextSequenceNumber, bool HasMore);
