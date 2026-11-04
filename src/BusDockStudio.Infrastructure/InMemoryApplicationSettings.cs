using BusDockStudio.Core.Models;
using BusDockStudio.Core.Services;

namespace BusDockStudio.Infrastructure;

public sealed class InMemoryApplicationSettings : IApplicationSettings
{
    private readonly List<ConnectionProfile> connections = [];

    public string Theme { get; set; } = "System";

    public Task<IReadOnlyList<ConnectionProfile>> GetConnectionsAsync(CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        return Task.FromResult<IReadOnlyList<ConnectionProfile>>(connections.ToArray());
    }

    public Task SaveConnectionAsync(ConnectionProfile profile, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        connections.RemoveAll(item => item.Id == profile.Id);
        connections.Add(profile);
        return Task.CompletedTask;
    }

    public Task RemoveConnectionAsync(Guid profileId, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        connections.RemoveAll(item => item.Id == profileId);
        return Task.CompletedTask;
    }
}
