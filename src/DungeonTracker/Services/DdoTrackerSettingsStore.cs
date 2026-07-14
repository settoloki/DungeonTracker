using System.Text.Json;
using DungeonTracker.Models;

namespace DungeonTracker.Services;

public sealed class DdoTrackerSettingsStore
{
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
            return JsonSerializer.Deserialize<DdoTrackerSettings>(json, JsonOptions) ?? new DdoTrackerSettings();
        }
        catch
        {
            return new DdoTrackerSettings();
        }
    }

    private void SaveLocked()
    {
        var json = JsonSerializer.Serialize(_settings, JsonOptions);
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
}
