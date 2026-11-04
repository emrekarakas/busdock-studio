using Azure.Messaging.ServiceBus.Administration;
using BusDockStudio.Core.Models;
using BusDockStudio.Core.Services;

namespace BusDockStudio.AzureServiceBus;

public sealed class AzureServiceBusConnectionService : IServiceBusConnectionService
{
    public async Task<ConnectionTestResult> TestAsync(string connectionString, CancellationToken cancellationToken)
    {
        try
        {
            var namespaceName = GetNamespaceName(connectionString);
            var administration = new ServiceBusAdministrationClient(connectionString);
            await administration.GetNamespacePropertiesAsync(cancellationToken).ConfigureAwait(false);
            return new(true, namespaceName, "Connection succeeded.");
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception exception)
        {
            return new(false, null, $"Connection failed ({exception.GetType().Name}). Check the credential and permissions.");
        }
    }

    private static string GetNamespaceName(string connectionString)
    {
        var endpointPart = connectionString.Split(';', StringSplitOptions.RemoveEmptyEntries)
            .FirstOrDefault(part => part.StartsWith("Endpoint=", StringComparison.OrdinalIgnoreCase));
        var endpointValue = endpointPart?["Endpoint=".Length..];
        return Uri.TryCreate(endpointValue, UriKind.Absolute, out var endpoint)
            ? endpoint.Host
            : "Service Bus namespace";
    }
}
