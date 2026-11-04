using BusDockStudio.Core.Models;
using BusDockStudio.Core.Services;

namespace BusDockStudio.Core.UseCases;

public sealed class PurgeCoordinator(IMessagePurgeService purgeService, IClock clock) : IDisposable
{
    private readonly SemaphoreSlim operationGate = new(1, 1);

    public async Task<PurgeResult> ExecuteAsync(
        string connectionString,
        PurgeRequest request,
        IProgress<PurgeProgress>? progress,
        CancellationToken cancellationToken)
    {
        var cutoff = clock.UtcNow;

        if (request.Source.EntityKind == EntityKind.Topic)
        {
            return Rejected(cutoff, "Topics cannot be purged directly; choose a subscription.");
        }

        if (request.Source.IsPartitioned)
        {
            return Rejected(cutoff, "Batch purge is not supported for partitioned entities.");
        }

        if (!string.Equals(request.TypedConfirmation.Trim(), request.Source.EntityName, StringComparison.Ordinal))
        {
            return Rejected(cutoff, "The typed entity name does not match exactly.");
        }

        if (!await operationGate.WaitAsync(0, cancellationToken).ConfigureAwait(false))
        {
            return Rejected(cutoff, "A purge operation is already running.");
        }

        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeout.CancelAfter(request.Timeout);

        try
        {
            var result = await purgeService.PurgeAsync(
                connectionString,
                new PurgeCommand(request.Source, cutoff),
                progress,
                timeout.Token).ConfigureAwait(false);

            return new(PurgeStatus.Succeeded, result.DeletedCount, cutoff,
                $"Purged {result.DeletedCount:N0} eligible messages. Locked, deferred, or scheduled messages may remain.");
        }
        catch (PartialPurgeException exception)
        {
            return new(PurgeStatus.PartiallySucceeded, exception.DeletedCount, cutoff,
                $"Purge partially completed: {exception.DeletedCount:N0} deletions were confirmed before the failure.", true);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            return new(PurgeStatus.Cancelled, 0, cutoff,
                "Purge was cancelled and may have partially completed.", true);
        }
        catch (OperationCanceledException)
        {
            return new(PurgeStatus.TimedOut, 0, cutoff,
                "Purge timed out and may have partially completed.", true);
        }
        catch (Exception exception)
        {
            return new(PurgeStatus.Failed, 0, cutoff,
                $"Purge failed ({exception.GetType().Name}) and may have partially completed.", true);
        }
        finally
        {
            operationGate.Release();
        }
    }

    private static PurgeResult Rejected(DateTimeOffset cutoff, string message) =>
        new(PurgeStatus.Rejected, 0, cutoff, message);

    public void Dispose() => operationGate.Dispose();
}
