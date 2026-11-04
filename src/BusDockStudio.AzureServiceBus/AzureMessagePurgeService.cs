using Azure.Messaging.ServiceBus;
using BusDockStudio.Core.Models;
using BusDockStudio.Core.Services;
using System.Reflection;

namespace BusDockStudio.AzureServiceBus;

public sealed class AzureMessagePurgeService : IMessagePurgeService
{
    public async Task<PurgeAdapterResult> PurgeAsync(
        string connectionString,
        PurgeCommand command,
        IProgress<PurgeProgress>? progress,
        CancellationToken cancellationToken)
    {
        await using var client = new ServiceBusClient(connectionString);
        await using var receiver = CreateReceiver(client, command.Source);
        progress?.Report(new(0, "Deleting eligible messages on the service..."));

        var deleted = await InvokePurgeAsync(receiver, command.CutoffUtc, cancellationToken).ConfigureAwait(false);
        progress?.Report(new(deleted, "Purge completed."));
        return new(deleted);
    }

    private static async Task<long> InvokePurgeAsync(
        ServiceBusReceiver receiver,
        DateTimeOffset cutoffUtc,
        CancellationToken cancellationToken)
    {
        // The stable 7.21 package contains the broker-side purge operation but does not yet expose
        // it publicly. Keep this compatibility bridge isolated and fail closed if its signature changes.
        var method = typeof(ServiceBusReceiver).GetMethod(
            "PurgeMessagesAsync",
            BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic,
            binder: null,
            types: [typeof(DateTimeOffset?), typeof(CancellationToken)],
            modifiers: null)
            ?? throw new NotSupportedException("This Azure SDK version does not provide broker-side purge.");

        var operation = method.Invoke(receiver, [(DateTimeOffset?)cutoffUtc, cancellationToken]) as Task
            ?? throw new NotSupportedException("The Azure SDK purge operation has an unsupported signature.");
        await operation.ConfigureAwait(false);

        var result = operation.GetType().GetProperty("Result")?.GetValue(operation);
        return result switch
        {
            int value => value,
            long value => value,
            not null when result.GetType().GetProperty("DeletedCount")?.GetValue(result) is long value => value,
            not null when result.GetType().GetProperty("DeletedCount")?.GetValue(result) is int value => value,
            _ => throw new NotSupportedException("The Azure SDK purge result has an unsupported shape.")
        };
    }

    private static ServiceBusReceiver CreateReceiver(ServiceBusClient client, MessageSource source)
    {
        var options = new ServiceBusReceiverOptions
        {
            SubQueue = source.Subqueue switch
            {
                MessageSubqueue.Active => SubQueue.None,
                MessageSubqueue.DeadLetter => SubQueue.DeadLetter,
                MessageSubqueue.TransferDeadLetter => SubQueue.TransferDeadLetter,
                _ => throw new ArgumentOutOfRangeException(nameof(source))
            }
        };

        return source.EntityKind switch
        {
            EntityKind.Queue => client.CreateReceiver(source.EntityName, options),
            EntityKind.Subscription when source.TopicName is not null =>
                client.CreateReceiver(source.TopicName, source.EntityName, options),
            _ => throw new InvalidOperationException("Only queues and subscriptions can be purged.")
        };
    }
}
