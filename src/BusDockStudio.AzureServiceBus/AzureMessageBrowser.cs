using System.Globalization;
using Azure.Messaging.ServiceBus;
using BusDockStudio.Core.Models;
using BusDockStudio.Core.Services;
using BusDockStudio.Core.UseCases;

namespace BusDockStudio.AzureServiceBus;

public sealed class AzureMessageBrowser : IMessageBrowser
{
    public async Task<MessagePage> PeekAsync(
        string connectionString,
        PeekRequest request,
        CancellationToken cancellationToken)
    {
        await using var client = new ServiceBusClient(connectionString);
        await using var receiver = CreateReceiver(client, request.Source);
        var messages = await receiver.PeekMessagesAsync(
            request.PageSize,
            request.FromSequenceNumber,
            cancellationToken).ConfigureAwait(false);

        var mapped = messages.Select(Map).ToArray();
        var next = PeekPagination.GetNextSequenceNumber(mapped);
        return new(mapped, next, mapped.Length == request.PageSize);
    }

    private static ServiceBusReceiver CreateReceiver(ServiceBusClient client, MessageSource source)
    {
        var options = new ServiceBusReceiverOptions { SubQueue = MapSubqueue(source.Subqueue) };
        return source.EntityKind switch
        {
            EntityKind.Queue => client.CreateReceiver(source.EntityName, options),
            EntityKind.Subscription when source.TopicName is not null =>
                client.CreateReceiver(source.TopicName, source.EntityName, options),
            _ => throw new InvalidOperationException("Messages can only be browsed on queues and subscriptions.")
        };
    }

    private static SubQueue MapSubqueue(MessageSubqueue value) => value switch
    {
        MessageSubqueue.Active => SubQueue.None,
        MessageSubqueue.DeadLetter => SubQueue.DeadLetter,
        MessageSubqueue.TransferDeadLetter => SubQueue.TransferDeadLetter,
        _ => throw new ArgumentOutOfRangeException(nameof(value))
    };

    private static MessageRecord Map(ServiceBusReceivedMessage message)
    {
        var system = new Dictionary<string, string>(StringComparer.Ordinal)
        {
            ["SequenceNumber"] = message.SequenceNumber.ToString(CultureInfo.InvariantCulture),
            ["MessageId"] = message.MessageId,
            ["EnqueuedTime"] = message.EnqueuedTime.ToString("O", CultureInfo.InvariantCulture),
            ["ExpiresAt"] = message.ExpiresAt.ToString("O", CultureInfo.InvariantCulture),
            ["DeliveryCount"] = message.DeliveryCount.ToString(CultureInfo.InvariantCulture),
            ["State"] = message.State.ToString()
        };

        if (message.ScheduledEnqueueTime != default)
        {
            system["ScheduledEnqueueTime"] = message.ScheduledEnqueueTime.ToString("O", CultureInfo.InvariantCulture);
        }

        Add(system, "Subject", message.Subject);
        Add(system, "SessionId", message.SessionId);
        Add(system, "CorrelationId", message.CorrelationId);
        Add(system, "ContentType", message.ContentType);
        Add(system, "DeadLetterReason", message.DeadLetterReason);
        Add(system, "DeadLetterErrorDescription", message.DeadLetterErrorDescription);

        var application = message.ApplicationProperties.ToDictionary(
            pair => pair.Key,
            pair => FormatProperty(pair.Value),
            StringComparer.Ordinal);

        return new(
            message.SequenceNumber,
            message.MessageId,
            message.Subject,
            message.EnqueuedTime,
            message.ScheduledEnqueueTime,
            message.ExpiresAt,
            message.DeliveryCount,
            message.State.ToString(),
            message.SessionId,
            message.CorrelationId,
            message.ContentType,
            message.DeadLetterReason,
            message.DeadLetterErrorDescription,
            MessageBodyFormatter.Format(message.Body.ToMemory()),
            MessageRecord.ReadOnly(system),
            MessageRecord.ReadOnly(application));
    }

    private static string FormatProperty(object value) => value switch
    {
        byte[] bytes => Convert.ToBase64String(bytes),
        DateTime date => date.ToUniversalTime().ToString("O", CultureInfo.InvariantCulture),
        DateTimeOffset date => date.ToUniversalTime().ToString("O", CultureInfo.InvariantCulture),
        _ => Convert.ToString(value, CultureInfo.InvariantCulture) ?? string.Empty
    };

    private static void Add(Dictionary<string, string> values, string name, string? value)
    {
        if (!string.IsNullOrEmpty(value))
        {
            values[name] = value;
        }
    }
}
