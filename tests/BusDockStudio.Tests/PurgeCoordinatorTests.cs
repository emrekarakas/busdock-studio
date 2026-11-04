using BusDockStudio.Core.Models;
using BusDockStudio.Core.UseCases;
using BusDockStudio.Tests.Fakes;

namespace BusDockStudio.Tests;

public sealed class PurgeCoordinatorTests
{
    private static readonly DateTimeOffset Cutoff = new(2026, 11, 4, 12, 0, 0, TimeSpan.Zero);

    [Fact]
    public async Task SuccessUsesOneFixedCutoffAndReportsActualCount()
    {
        var service = new FakePurgeService
        {
            Handler = (_, _) => Task.FromResult(new PurgeAdapterResult(27))
        };
        var clock = new FakeClock(Cutoff);
        using var coordinator = new PurgeCoordinator(service, clock);

        var result = await coordinator.ExecuteAsync("secret", Request(), null, CancellationToken.None);

        Assert.Equal(PurgeStatus.Succeeded, result.Status);
        Assert.Equal(27, result.DeletedCount);
        Assert.Equal(Cutoff, result.CutoffUtc);
        Assert.Equal(Cutoff, service.LastCommand?.CutoffUtc);
        Assert.Equal(1, clock.ReadCount);
    }

    [Fact]
    public async Task ReportsPartialSuccess()
    {
        var service = new FakePurgeService
        {
            Handler = (_, _) => throw new PartialPurgeException(12, "network")
        };
        using var coordinator = new PurgeCoordinator(service, new FakeClock(Cutoff));

        var result = await coordinator.ExecuteAsync("secret", Request(), null, CancellationToken.None);

        Assert.Equal(PurgeStatus.PartiallySucceeded, result.Status);
        Assert.Equal(12, result.DeletedCount);
        Assert.True(result.MayBePartiallyCompleted);
    }

    [Fact]
    public async Task CancellationWarnsAboutPartialCompletion()
    {
        var started = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var service = new FakePurgeService
        {
            Handler = async (_, token) =>
            {
                started.SetResult();
                await Task.Delay(Timeout.InfiniteTimeSpan, token);
                return new(0);
            }
        };
        using var coordinator = new PurgeCoordinator(service, new FakeClock(Cutoff));
        using var cancellation = new CancellationTokenSource();

        var operation = coordinator.ExecuteAsync("secret", Request(), null, cancellation.Token);
        await started.Task;
        await cancellation.CancelAsync();
        var result = await operation;

        Assert.Equal(PurgeStatus.Cancelled, result.Status);
        Assert.True(result.MayBePartiallyCompleted);
    }

    [Fact]
    public async Task TimeoutWarnsAboutPartialCompletion()
    {
        var service = new FakePurgeService
        {
            Handler = async (_, token) =>
            {
                await Task.Delay(Timeout.InfiniteTimeSpan, token);
                return new(0);
            }
        };
        using var coordinator = new PurgeCoordinator(service, new FakeClock(Cutoff));

        var result = await coordinator.ExecuteAsync("secret", Request(timeout: TimeSpan.FromMilliseconds(10)), null, CancellationToken.None);

        Assert.Equal(PurgeStatus.TimedOut, result.Status);
        Assert.True(result.MayBePartiallyCompleted);
    }

    [Fact]
    public async Task RejectsPartitionedEntityWithoutCallingAdapter()
    {
        var service = new FakePurgeService();
        using var coordinator = new PurgeCoordinator(service, new FakeClock(Cutoff));

        var result = await coordinator.ExecuteAsync("secret", Request(partitioned: true), null, CancellationToken.None);

        Assert.Equal(PurgeStatus.Rejected, result.Status);
        Assert.Equal(0, service.CallCount);
    }

    [Fact]
    public async Task RequiresExactTypedEntityName()
    {
        var service = new FakePurgeService();
        using var coordinator = new PurgeCoordinator(service, new FakeClock(Cutoff));

        var result = await coordinator.ExecuteAsync("secret", Request(confirmation: "orders-typo"), null, CancellationToken.None);

        Assert.Equal(PurgeStatus.Rejected, result.Status);
        Assert.Equal(0, service.CallCount);
    }

    [Fact]
    public async Task PreventsConcurrentPurge()
    {
        var started = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var release = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var service = new FakePurgeService
        {
            Handler = async (_, _) =>
            {
                started.SetResult();
                await release.Task;
                return new(1);
            }
        };
        using var coordinator = new PurgeCoordinator(service, new FakeClock(Cutoff));

        var first = coordinator.ExecuteAsync("secret", Request(), null, CancellationToken.None);
        await started.Task;
        var second = await coordinator.ExecuteAsync("secret", Request(), null, CancellationToken.None);
        release.SetResult();
        await first;

        Assert.Equal(PurgeStatus.Rejected, second.Status);
        Assert.Equal(1, service.CallCount);
    }

    private static PurgeRequest Request(
        bool partitioned = false,
        string confirmation = "orders",
        TimeSpan? timeout = null)
    {
        var source = new MessageSource("orders", EntityKind.Queue, null, MessageSubqueue.Active, partitioned, 42);
        return new(source, "demo.servicebus.windows.net", confirmation, 42, timeout ?? TimeSpan.FromSeconds(5));
    }
}
