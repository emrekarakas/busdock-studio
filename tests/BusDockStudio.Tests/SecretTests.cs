using BusDockStudio.Infrastructure;

namespace BusDockStudio.Tests;

public sealed class SecretTests
{
    [Fact]
    public void MasksConnectionSecrets()
    {
        const string value = "Endpoint=sb://demo/;SharedAccessKeyName=reader;SharedAccessKey=very-secret;Token=abc";

        var masked = SecretRedactor.Mask(value);

        Assert.DoesNotContain("very-secret", masked, StringComparison.Ordinal);
        Assert.DoesNotContain("Token=abc", masked, StringComparison.Ordinal);
        Assert.Contains("SharedAccessKey=***", masked, StringComparison.Ordinal);
    }

    [Fact]
    public async Task MemoryStoreRemovesSecret()
    {
        using var store = new InMemorySecretStore();
        var id = Guid.NewGuid();
        await store.SetAsync(id, "secret", CancellationToken.None);
        await store.RemoveAsync(id, CancellationToken.None);

        Assert.Null(await store.GetAsync(id, CancellationToken.None));
    }
}
