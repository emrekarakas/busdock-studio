using System.Text.Json;
using BusDockStudio.Core.Models;
using BusDockStudio.Core.Services;

namespace BusDockStudio.Infrastructure;

public sealed class FileApplicationSettings : IApplicationSettings
{
    private static readonly JsonSerializerOptions JsonOptions = new() { WriteIndented = true };
    private readonly object sync = new();
    private readonly string settingsPath;
    private readonly List<ConnectionProfile> connections = [];
    private string theme = "System";

    public FileApplicationSettings(string? settingsPath = null)
    {
        this.settingsPath = settingsPath ?? GetDefaultPath();
        Load();
    }

    public string Theme
    {
        get
        {
            lock (sync)
            {
                return theme;
            }
        }
        set
        {
            lock (sync)
            {
                theme = NormalizeTheme(value);
                Save();
            }
        }
    }

    public Task<IReadOnlyList<ConnectionProfile>> GetConnectionsAsync(CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        lock (sync)
        {
            return Task.FromResult<IReadOnlyList<ConnectionProfile>>(connections.ToArray());
        }
    }

    public Task SaveConnectionAsync(ConnectionProfile profile, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        lock (sync)
        {
            connections.RemoveAll(item => item.Id == profile.Id);
            connections.Add(profile);
            Save();
        }

        return Task.CompletedTask;
    }

    public Task RemoveConnectionAsync(Guid profileId, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        lock (sync)
        {
            connections.RemoveAll(item => item.Id == profileId);
            Save();
        }

        return Task.CompletedTask;
    }

    private void Load()
    {
        if (!File.Exists(settingsPath))
        {
            return;
        }

        try
        {
            var data = JsonSerializer.Deserialize<SettingsData>(File.ReadAllText(settingsPath), JsonOptions);
            if (data is null)
            {
                return;
            }

            theme = NormalizeTheme(data.Theme);
            connections.AddRange(data.Connections
                .Where(IsValid)
                .DistinctBy(item => item.Id));
        }
        catch (JsonException)
        {
            // Keep the invalid file untouched. A later explicit save replaces it atomically.
        }
        catch (IOException)
        {
            // The UI remains usable with empty settings when the file is temporarily unavailable.
        }
    }

    private void Save()
    {
        var directory = Path.GetDirectoryName(settingsPath)
            ?? throw new InvalidOperationException("The settings path has no parent directory.");
        Directory.CreateDirectory(directory);
        RestrictDirectoryPermissions(directory);

        var temporaryPath = Path.Combine(directory, $".{Path.GetFileName(settingsPath)}.{Guid.NewGuid():N}.tmp");
        try
        {
            var json = JsonSerializer.Serialize(new SettingsData(theme, connections.ToArray()), JsonOptions);
            File.WriteAllText(temporaryPath, json);
            RestrictFilePermissions(temporaryPath);
            File.Move(temporaryPath, settingsPath, true);
        }
        finally
        {
            if (File.Exists(temporaryPath))
            {
                File.Delete(temporaryPath);
            }
        }
    }

    private static string GetDefaultPath()
    {
        var root = OperatingSystem.IsMacOS()
            ? Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), "Library", "Application Support")
            : Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData);
        return Path.Combine(root, "BusDock Studio", "settings.json");
    }

    private static bool IsValid(ConnectionProfile profile) =>
        profile.Id != Guid.Empty &&
        !string.IsNullOrWhiteSpace(profile.DisplayName) &&
        !string.IsNullOrWhiteSpace(profile.FullyQualifiedNamespace);

    private static string NormalizeTheme(string? value) => value is "Light" or "Dark" ? value : "System";

    private static void RestrictDirectoryPermissions(string path)
    {
        if (!OperatingSystem.IsWindows())
        {
            File.SetUnixFileMode(path, UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute);
        }
    }

    private static void RestrictFilePermissions(string path)
    {
        if (!OperatingSystem.IsWindows())
        {
            File.SetUnixFileMode(path, UnixFileMode.UserRead | UnixFileMode.UserWrite);
        }
    }

    private sealed record SettingsData(string Theme, IReadOnlyList<ConnectionProfile> Connections)
    {
        public SettingsData() : this("System", []) { }
    }
}
