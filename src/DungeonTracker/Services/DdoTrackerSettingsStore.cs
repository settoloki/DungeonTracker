using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using DungeonTracker.Models;

namespace DungeonTracker.Services;

public sealed class DdoTrackerSettingsStore
{
    private const string ProtectedTokenPrefix = "dpapi:";

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase
    };

    private readonly object _lock = new();
    private readonly string _settingsPath;
    private DdoTrackerSettings _settings = new();

    public DdoTrackerSettingsStore(string pluginFolder)
    {
        Directory.CreateDirectory(pluginFolder);
        _settingsPath = Path.Combine(pluginFolder, "ddotracker-settings.json");
        _settings = Load();
    }

    public DdoTrackerSettings Snapshot
    {
        get
        {
            lock (_lock)
            {
                return Clone(_settings);
            }
        }
    }

    public void Update(Action<DdoTrackerSettings> mutate)
    {
        lock (_lock)
        {
            mutate(_settings);
            SaveLocked();
        }
    }

    private DdoTrackerSettings Load()
    {
        try
        {
            if (!File.Exists(_settingsPath))
                return new DdoTrackerSettings();

            var json = File.ReadAllText(_settingsPath);
            var loaded = JsonSerializer.Deserialize<DdoTrackerSettings>(json, JsonOptions) ?? new DdoTrackerSettings();
            loaded.Token = UnprotectToken(loaded.Token);
            return loaded;
        }
        catch
        {
            return new DdoTrackerSettings();
        }
    }

    private void SaveLocked()
    {
        var toSave = Clone(_settings);
        toSave.Token = ProtectToken(toSave.Token);
        var json = JsonSerializer.Serialize(toSave, JsonOptions);
        File.WriteAllText(_settingsPath, json);
    }

    private static DdoTrackerSettings Clone(DdoTrackerSettings source)
    {
        return new DdoTrackerSettings
        {
            Token = source.Token,
            Email = source.Email,
            UserName = source.UserName,
            AutoSync = source.AutoSync,
            SelectedCharacterId = source.SelectedCharacterId,
            SelectedCharacterName = source.SelectedCharacterName,
            Pending = source.Pending
                .Select(item => new DdoTrackerPendingCompletion
                {
                    CharacterId = item.CharacterId,
                    Name = item.Name,
                    Difficulty = item.Difficulty,
                    Setting = item.Setting,
                    DurationSeconds = item.DurationSeconds,
                    CompletedAtUtc = item.CompletedAtUtc,
                    QueuedAtUtc = item.QueuedAtUtc,
                    Attempts = item.Attempts
                })
                .ToList()
        };
    }

    private static string? ProtectToken(string? token)
    {
        if (string.IsNullOrWhiteSpace(token))
            return token;

        if (token.StartsWith(ProtectedTokenPrefix, StringComparison.Ordinal))
            return token;

        try
        {
            var bytes = Encoding.UTF8.GetBytes(token);
            var protectedBytes = ProtectedData.Protect(bytes, optionalEntropy: null, DataProtectionScope.CurrentUser);
            return ProtectedTokenPrefix + Convert.ToBase64String(protectedBytes);
        }
        catch
        {
            return token;
        }
    }

    private static string? UnprotectToken(string? token)
    {
        if (string.IsNullOrWhiteSpace(token))
            return token;

        if (!token.StartsWith(ProtectedTokenPrefix, StringComparison.Ordinal))
            return token;

        try
        {
            var protectedBytes = Convert.FromBase64String(token[ProtectedTokenPrefix.Length..]);
            var bytes = ProtectedData.Unprotect(protectedBytes, optionalEntropy: null, DataProtectionScope.CurrentUser);
            return Encoding.UTF8.GetString(bytes);
        }
        catch
        {
            return null;
        }
    }
}
