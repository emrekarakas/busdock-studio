using BusDockStudio.Core.Models;
using BusDockStudio.Infrastructure;

namespace BusDockStudio.Tests;

public sealed class ApplicationSettingsTests
{
    [Fact]
    public async Task FileSettingsRestoreProfilesAndTheme()
    {
        var directory = Path.Combine(Path.GetTempPath(), $"busdock-settings-{Guid.NewGuid():N}");
        var path = Path.Combine(directory, "settings.json");
        var profile = new ConnectionProfile(Guid.NewGuid(), "Development", "demo.servicebus.windows.net");
        try
        {
            var writer = new FileApplicationSettings(path) { Theme = "Dark" };
            await writer.SaveConnectionAsync(profile, CancellationToken.None);

            var reader = new FileApplicationSettings(path);
            var restored = await reader.GetConnectionsAsync(CancellationToken.None);

            Assert.Equal("Dark", reader.Theme);
            Assert.Equal(profile, Assert.Single(restored));
            Assert.DoesNotContain("SharedAccessKey", await File.ReadAllTextAsync(path), StringComparison.Ordinal);
        }
        finally
        {
            if (Directory.Exists(directory))
            {
                Directory.Delete(directory, true);
            }
        }
    }
}
