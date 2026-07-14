namespace DungeonTracker.Services;

/// <summary>
/// Append-only log of tracker state changes for development and debugging.
/// </summary>
public sealed class DevelopmentLog
{
    private readonly object _lock = new();
    private string _dataFolder = string.Empty;
    private string _logPath = string.Empty;

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

    public bool SwitchTo(string dataFolder)
    {
        lock (_lock)
        {
            if (string.Equals(_dataFolder, dataFolder, StringComparison.OrdinalIgnoreCase))
                return false;

            Directory.CreateDirectory(dataFolder);
            _dataFolder = dataFolder;
            _logPath = Path.Combine(dataFolder, "dungeon-tracker-dev.log");
            return true;
        }
    }

    public void Log(string category, string message)
    {
        var line = $"{DateTime.UtcNow:yyyy-MM-dd HH:mm:ss.fff}Z\t{category}\t{message}{Environment.NewLine}";
        lock (_lock)
        {
            if (string.IsNullOrWhiteSpace(_logPath))
                return;

            Directory.CreateDirectory(_dataFolder);
            File.AppendAllText(_logPath, line);
        }
    }

    public void LogChange(string category, string field, object? oldValue, object? newValue)
    {
        if (Equals(oldValue, newValue))
            return;

        Log(category, $"{field}: {FormatValue(oldValue)} -> {FormatValue(newValue)}");
    }

    private static string FormatValue(object? value) =>
        value switch
        {
            null => "null",
            string s when string.IsNullOrWhiteSpace(s) => "(empty)",
            _ => value.ToString() ?? "null"
        };
}
