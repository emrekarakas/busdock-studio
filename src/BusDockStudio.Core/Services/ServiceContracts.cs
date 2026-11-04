using BusDockStudio.Core.Models;

namespace BusDockStudio.Core.Services;

public interface IServiceBusConnectionService
{
    Task<ConnectionTestResult> TestAsync(string connectionString, CancellationToken cancellationToken);
}

public interface IEntityBrowser
{
    Task<IReadOnlyList<EntityDescriptor>> BrowseAsync(string connectionString, CancellationToken cancellationToken);
}

public interface IMessageBrowser
{
    Task<MessagePage> PeekAsync(string connectionString, PeekRequest request, CancellationToken cancellationToken);
}

public interface IMessagePurgeService
{
    Task<PurgeAdapterResult> PurgeAsync(
        string connectionString,
        PurgeCommand command,
        IProgress<PurgeProgress>? progress,
        CancellationToken cancellationToken);
}

public interface ISecretStore
{
    Task SetAsync(Guid profileId, string secret, CancellationToken cancellationToken);
    Task<string?> GetAsync(Guid profileId, CancellationToken cancellationToken);
    Task RemoveAsync(Guid profileId, CancellationToken cancellationToken);
}

public interface IApplicationSettings
{
    Task<IReadOnlyList<ConnectionProfile>> GetConnectionsAsync(CancellationToken cancellationToken);
    Task SaveConnectionAsync(ConnectionProfile profile, CancellationToken cancellationToken);
    Task RemoveConnectionAsync(Guid profileId, CancellationToken cancellationToken);
    string Theme { get; set; }
}

public interface IClock
{
    DateTimeOffset UtcNow { get; }
}
