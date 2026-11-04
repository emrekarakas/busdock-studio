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

    private static MainWindowViewModel Create(FakeConnectionService connection) => new(
        connection,
        new FakeEntityBrowser(),
        new FakeMessageBrowser(),
        new FakePurgeService(),
        new InMemorySecretStore(),
        new InMemoryApplicationSettings(),
        new FakeClock(DateTimeOffset.UtcNow));
}
