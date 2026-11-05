using BusDockStudio.Core.Models;
using BusDockStudio.Desktop.ViewModels;
using BusDockStudio.Infrastructure;
using BusDockStudio.Tests.Fakes;

namespace BusDockStudio.Tests;

public sealed class MainWindowViewModelTests
{
    [Fact]
    public async Task AddConnectionShowsLoadingThenSuccess()
    {
        var completion = new TaskCompletionSource<ConnectionTestResult>(TaskCreationOptions.RunContinuationsAsynchronously);
        var connection = new FakeConnectionService { Handler = _ => completion.Task };
        using var viewModel = Create(connection);
        viewModel.ConnectionString = "Endpoint=sb://demo/;SharedAccessKeyName=x;SharedAccessKey=y";

        var operation = viewModel.AddConnectionCommand.ExecuteAsync(null);
        Assert.True(viewModel.IsBusy);
        completion.SetResult(new(true, "demo.servicebus.windows.net", "Connected"));
        await operation;

        Assert.False(viewModel.IsBusy);
        Assert.Empty(viewModel.ErrorMessage);
        Assert.Single(viewModel.Connections);
        Assert.Empty(viewModel.ConnectionString);
    }

    [Fact]
    public async Task AddConnectionSurfacesSafeErrorState()
    {
        var connection = new FakeConnectionService
        {
            Handler = _ => Task.FromResult(new ConnectionTestResult(false, null, "Connection failed (UnauthorizedAccessException)."))
        };
        using var viewModel = Create(connection);
        viewModel.ConnectionString = "secret";

        await viewModel.AddConnectionCommand.ExecuteAsync(null);

        Assert.False(viewModel.IsBusy);
        Assert.Contains("Connection failed", viewModel.ErrorMessage, StringComparison.Ordinal);
        Assert.Empty(viewModel.Connections);
    }

    [Fact]
    public void PurgeUsesAcknowledgementAndResetsWhenTargetChanges()
    {
        using var viewModel = Create(new FakeConnectionService());
        viewModel.SelectedNode = SourceNode("orders", MessageSubqueue.Active);

        viewModel.OpenPurgeCommand.Execute(null);

        Assert.True(viewModel.IsPurgePanelVisible);
        Assert.False(viewModel.IsPurgeAcknowledged);

        viewModel.IsPurgeAcknowledged = true;
        viewModel.SelectedNode = SourceNode("orders", MessageSubqueue.DeadLetter);

        Assert.False(viewModel.IsPurgePanelVisible);
        Assert.False(viewModel.IsPurgeAcknowledged);
    }

    [Fact]
    public async Task ReaddingNamespaceUpdatesSavedConnectionWithoutDuplicate()
    {
        var secrets = new InMemorySecretStore();
        var settings = new InMemoryApplicationSettings();
        using var viewModel = new MainWindowViewModel(
            new FakeConnectionService(),
            new FakeEntityBrowser(),
            new FakeMessageBrowser(),
            new FakePurgeService(),
            secrets,
            settings,
            new FakeClock(DateTimeOffset.UtcNow));

        viewModel.ConnectionName = "Development";
        viewModel.ConnectionString = "Endpoint=sb://demo/;SharedAccessKeyName=x;SharedAccessKey=one";
        await viewModel.AddConnectionCommand.ExecuteAsync(null);
        var originalId = Assert.Single(viewModel.Connections).Id;

        viewModel.ConnectionName = "Development updated";
        viewModel.ConnectionString = "Endpoint=sb://demo/;SharedAccessKeyName=x;SharedAccessKey=two";
        await viewModel.AddConnectionCommand.ExecuteAsync(null);

        var updated = Assert.Single(viewModel.Connections);
        Assert.Equal(originalId, updated.Id);
        Assert.Equal("Development updated", updated.DisplayName);
        Assert.EndsWith("SharedAccessKey=two", await secrets.GetAsync(updated.Id, CancellationToken.None), StringComparison.Ordinal);
    }

    [Fact]
    public async Task EntitySearchPreservesGroupsAndMatchesSubscriptions()
    {
        var entities = new EntityDescriptor[]
        {
            new("orders", EntityKind.Queue, ApproximateActiveCount: 12, ApproximateDeadLetterCount: 3,
                ApproximateTransferDeadLetterCount: 1),
            new("audit", EntityKind.Queue),
            new("events", EntityKind.Topic),
            new("billing-worker", EntityKind.Subscription, "events"),
            new("metrics-worker", EntityKind.Subscription, "events")
        };
        var browser = new FakeEntityBrowser
        {
            Handler = () => Task.FromResult<IReadOnlyList<EntityDescriptor>>(entities)
        };
        using var secrets = new InMemorySecretStore();
        using var viewModel = new MainWindowViewModel(
            new FakeConnectionService(),
            browser,
            new FakeMessageBrowser(),
            new FakePurgeService(),
            secrets,
            new InMemoryApplicationSettings(),
            new FakeClock(DateTimeOffset.UtcNow));
        var profile = new ConnectionProfile(Guid.NewGuid(), "Development", "demo.servicebus.windows.net");
        await secrets.SetAsync(profile.Id, "secret", CancellationToken.None);

        viewModel.SelectedConnection = profile;
        if (viewModel.LoadEntitiesCommand.ExecutionTask is { } loading)
        {
            await loading;
        }

        viewModel.EntitySearchText = "billing";

        Assert.Equal("Queues (0)", viewModel.EntityNodes[0].Title);
        Assert.True(viewModel.EntityNodes[0].IsGroup);
        Assert.Equal(13, viewModel.EntityNodes[0].FontSize);
        var topics = viewModel.EntityNodes[1];
        Assert.Equal("Topics (1)", topics.Title);
        var topic = Assert.Single(topics.Children);
        Assert.Equal("events", topic.Title);
        Assert.Equal("billing-worker", Assert.Single(topic.Children).Title);

        viewModel.EntitySearchText = "orders";
        var order = Assert.Single(viewModel.EntityNodes[0].Children);
        Assert.Equal("Active (12)", order.Children[0].Title);
        Assert.Equal("Dead-letter (3)", order.Children[1].Title);
        Assert.Equal("Transfer dead-letter (1)", order.Children[2].Title);
    }

    [Fact]
    public async Task EntityRefreshPreservesExpandedNodes()
    {
        var browser = new FakeEntityBrowser
        {
            Handler = () => Task.FromResult<IReadOnlyList<EntityDescriptor>>([
                new("orders", EntityKind.Queue, ApproximateActiveCount: 5)
            ])
        };
        using var secrets = new InMemorySecretStore();
        using var viewModel = new MainWindowViewModel(
            new FakeConnectionService(),
            browser,
            new FakeMessageBrowser(),
            new FakePurgeService(),
            secrets,
            new InMemoryApplicationSettings(),
            new FakeClock(DateTimeOffset.UtcNow));
        var profile = new ConnectionProfile(Guid.NewGuid(), "Development", "demo.servicebus.windows.net");
        await secrets.SetAsync(profile.Id, "secret", CancellationToken.None);

        viewModel.SelectedConnection = profile;
        if (viewModel.LoadEntitiesCommand.ExecutionTask is { } initialLoad)
        {
            await initialLoad;
        }

        viewModel.EntityNodes[0].IsExpanded = true;
        viewModel.EntityNodes[0].Children[0].IsExpanded = true;
        await viewModel.LoadEntitiesCommand.ExecuteAsync(null);

        Assert.True(viewModel.EntityNodes[0].IsExpanded);
        Assert.True(viewModel.EntityNodes[0].Children[0].IsExpanded);
    }

    private static EntityTreeNode SourceNode(string entityName, MessageSubqueue subqueue) => new(
        subqueue.ToString(),
        new MessageSource(entityName, EntityKind.Queue, null, subqueue, false, 3));

    private static MainWindowViewModel Create(FakeConnectionService connection) => new(
        connection,
        new FakeEntityBrowser(),
        new FakeMessageBrowser(),
        new FakePurgeService(),
        new InMemorySecretStore(),
        new InMemoryApplicationSettings(),
        new FakeClock(DateTimeOffset.UtcNow));
}
