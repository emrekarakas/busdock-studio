using System.Collections.Concurrent;
using BusDockStudio.Core.Services;

namespace BusDockStudio.Infrastructure;

public sealed class InMemorySecretStore : ISecretStore, IDisposable
{
    private readonly ConcurrentDictionary<Guid, char[]> secrets = new();

    public Task SetAsync(Guid profileId, string secret, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        RemoveValue(profileId);
        secrets[profileId] = secret.ToCharArray();
        return Task.CompletedTask;
    }

    public Task<string?> GetAsync(Guid profileId, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        return Task.FromResult(secrets.TryGetValue(profileId, out var secret) ? new string(secret) : null);
    }

    public Task RemoveAsync(Guid profileId, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        RemoveValue(profileId);
        return Task.CompletedTask;
    }

    public void Dispose()
    {
        foreach (var id in secrets.Keys)
        {
            RemoveValue(id);
        }
    }

    private void RemoveValue(Guid id)
    {
        if (secrets.TryRemove(id, out var value))
        {
            Array.Clear(value);
        }
    }
}
