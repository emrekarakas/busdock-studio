using BusDockStudio.Core.Models;
using BusDockStudio.Core.Services;

namespace BusDockStudio.Tests.Fakes;

internal sealed class FakeClock(DateTimeOffset value) : IClock
{
    public int ReadCount { get; private set; }
    public DateTimeOffset UtcNow
    {
        get
        {
            ReadCount++;
            return value;
        }
    }
}

internal sealed class FakePurgeService : IMessagePurgeService
{
    public Func<PurgeCommand, CancellationToken, Task<PurgeAdapterResult>> Handler { get; set; } =
        (_, _) => Task.FromResult(new PurgeAdapterResult(0));
    public PurgeCommand? LastCommand { get; private set; }
    public int CallCount { get; private set; }

    public async Task<PurgeAdapterResult> PurgeAsync(
        string connectionString,
        PurgeCommand command,
        IProgress<PurgeProgress>? progress,
        CancellationToken cancellationToken)
    {
        LastCommand = command;
        CallCount++;
        return await Handler(command, cancellationToken);
    }
}

internal sealed class FakeConnectionService : IServiceBusConnectionService
{
    public Func<CancellationToken, Task<ConnectionTestResult>> Handler { get; set; } =
        _ => Task.FromResult(new ConnectionTestResult(true, "demo.servicebus.windows.net", "Connected"));

    public Task<ConnectionTestResult> TestAsync(string connectionString, CancellationToken cancellationToken) => Handler(cancellationToken);
}

internal sealed class FakeEntityBrowser : IEntityBrowser
{
    public Func<Task<IReadOnlyList<EntityDescriptor>>> Handler { get; set; } =
        () => Task.FromResult<IReadOnlyList<EntityDescriptor>>([]);

    public Task<IReadOnlyList<EntityDescriptor>> BrowseAsync(string connectionString, CancellationToken cancellationToken) => Handler();
}

internal sealed class FakeMessageBrowser : IMessageBrowser
{
    public List<PeekRequest> Requests { get; } = [];

    public Task<MessagePage> PeekAsync(string connectionString, PeekRequest request, CancellationToken cancellationToken)
    {
        Requests.Add(request);
        return Task.FromResult(new MessagePage([], null, false));
    }
}
