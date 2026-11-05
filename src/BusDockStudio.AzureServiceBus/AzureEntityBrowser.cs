using Azure.Messaging.ServiceBus.Administration;
using BusDockStudio.Core.Models;
using BusDockStudio.Core.Services;

namespace BusDockStudio.AzureServiceBus;

public sealed class AzureEntityBrowser : IEntityBrowser
{
    public async Task<IReadOnlyList<EntityDescriptor>> BrowseAsync(
        string connectionString,
        CancellationToken cancellationToken)
    {
        var client = new ServiceBusAdministrationClient(connectionString);
        var entities = new List<EntityDescriptor>();

        await foreach (var queue in client.GetQueuesAsync(cancellationToken))
        {
            var runtime = await client.GetQueueRuntimePropertiesAsync(queue.Name, cancellationToken).ConfigureAwait(false);
            entities.Add(new(
                queue.Name,
                EntityKind.Queue,
                IsPartitioned: queue.EnablePartitioning,
                ApproximateActiveCount: runtime.Value.ActiveMessageCount,
                ApproximateDeadLetterCount: runtime.Value.DeadLetterMessageCount,
                ApproximateTransferDeadLetterCount: runtime.Value.TransferDeadLetterMessageCount));
        }

        await foreach (var topic in client.GetTopicsAsync(cancellationToken))
        {
            entities.Add(new(topic.Name, EntityKind.Topic, IsPartitioned: topic.EnablePartitioning));

            await foreach (var subscription in client.GetSubscriptionsAsync(topic.Name, cancellationToken))
            {
                var runtime = await client.GetSubscriptionRuntimePropertiesAsync(
                    topic.Name,
                    subscription.SubscriptionName,
                    cancellationToken).ConfigureAwait(false);
                entities.Add(new(
                    subscription.SubscriptionName,
                    EntityKind.Subscription,
                    topic.Name,
                    topic.EnablePartitioning,
                    runtime.Value.ActiveMessageCount,
                    runtime.Value.DeadLetterMessageCount,
                    runtime.Value.TransferDeadLetterMessageCount));
            }
        }

        return entities;
    }
}
