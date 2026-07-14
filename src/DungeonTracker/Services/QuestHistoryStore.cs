using System.Text.Json;
using DungeonTracker.Models;

namespace DungeonTracker.Services;

public sealed class QuestHistoryStore
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase
    };

    private readonly object _lock = new();
    private string _dataFolder = string.Empty;
    private string _historyPath = string.Empty;
    private QuestHistoryFile _data = new();

    public string DataFolder
    {
        get
        {
            lock (_lock)
            {
                return _dataFolder;
            }
        }
    }

    public IReadOnlyList<QuestRunRecord> Runs
    {
        get
        {
            lock (_lock)
            {
                return _data.Runs.OrderByDescending(r => r.CompletedAtUtc).ToList();
            }
        }
    }

    public bool SwitchTo(string dataFolder)
    {
        lock (_lock)
        {
            if (string.Equals(_dataFolder, dataFolder, StringComparison.OrdinalIgnoreCase))
                return false;

            Directory.CreateDirectory(dataFolder);
            _dataFolder = dataFolder;
            _historyPath = Path.Combine(dataFolder, "quest-history.json");
            _data = Load();
            return true;
        }
    }

    public void AddRun(QuestRunRecord run)
    {
        lock (_lock)
        {
            _data.Runs.Add(run);
            SaveLocked();
        }
    }

    public void Clear()
    {
        lock (_lock)
        {
            _data.Runs.Clear();
            SaveLocked();
        }
    }

    private QuestHistoryFile Load()
    {
        try
        {
            if (string.IsNullOrWhiteSpace(_historyPath) || !File.Exists(_historyPath))
                return new QuestHistoryFile();

            var json = File.ReadAllText(_historyPath);
            return JsonSerializer.Deserialize<QuestHistoryFile>(json, JsonOptions) ?? new QuestHistoryFile();
        }
        catch
        {
            return new QuestHistoryFile();
        }
    }

    private void SaveLocked()
    {
        if (string.IsNullOrWhiteSpace(_historyPath))
            return;

        Directory.CreateDirectory(_dataFolder);
        var json = JsonSerializer.Serialize(_data, JsonOptions);
        File.WriteAllText(_historyPath, json);
    }
}
