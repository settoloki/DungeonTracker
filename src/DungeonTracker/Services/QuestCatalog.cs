using System.Reflection;
using System.Text.Json;
using DungeonTracker.Models;

namespace DungeonTracker.Services;

public sealed class QuestCatalogLoadResult
{
    public List<QuestCatalogEntry> Entries { get; init; } = [];
    public string LoadedFrom { get; init; } = "not found";
    public IReadOnlyList<string> Diagnostics { get; init; } = Array.Empty<string>();
}

public sealed class QuestCatalog
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = true
    };

    private readonly object _lock = new();
    private List<QuestCatalogEntry> _entries;
    private List<string> _questNames;
    private string _loadedFrom;
    private IReadOnlyList<string> _loadDiagnostics;

    public QuestCatalog(string pluginFolder)
    {
        var loadResult = LoadEntries(pluginFolder);
        _entries = loadResult.Entries;
        _loadedFrom = loadResult.LoadedFrom;
        _loadDiagnostics = loadResult.Diagnostics;
        _questNames = BuildQuestNames(_entries);
    }

    public IReadOnlyList<string> QuestNames
    {
        get
        {
            lock (_lock)
            {
                return _questNames.ToList();
            }
        }
    }

    public IReadOnlyList<QuestCatalogEntry> Entries
    {
        get
        {
            lock (_lock)
            {
                return _entries.ToList();
            }
        }
    }

    public int Count
    {
        get
        {
            lock (_lock)
            {
                return _questNames.Count;
            }
        }
    }

    public string LoadedFrom
    {
        get
        {
            lock (_lock)
            {
                return _loadedFrom;
            }
        }
    }

    public IReadOnlyList<string> LoadDiagnostics
    {
        get
        {
            lock (_lock)
            {
                return _loadDiagnostics;
            }
        }
    }

    /// <summary>
    /// Replace the in-memory catalog from DDO Tracker API JSON (array or <c>{ quests: [...] }</c>).
    /// </summary>
    public bool TryReplaceFromJson(string json, string sourceLabel, out int count)
    {
        count = 0;
        List<QuestCatalogEntry> entries;
        try
        {
            entries = ParseQuestJson(json);
        }
        catch
        {
            return false;
        }

        if (entries.Count == 0)
            return false;

        lock (_lock)
        {
            _entries = entries;
            _questNames = BuildQuestNames(entries);
            _loadedFrom = sourceLabel;
            _loadDiagnostics = [$"replaced from {sourceLabel}: {_questNames.Count} names ({entries.Count} rows)"];
            count = _questNames.Count;
        }

        return true;
    }

    private static List<string> BuildQuestNames(IEnumerable<QuestCatalogEntry> entries) =>
        entries
            .Select(entry => entry.Name.Trim())
            .Where(name => !string.IsNullOrWhiteSpace(name))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .OrderBy(name => name, StringComparer.OrdinalIgnoreCase)
            .ToList();

    public QuestCatalogEntry? FindEntry(string questName, string? questTier = null)
    {
        if (string.IsNullOrWhiteSpace(questName))
            return null;

        var normalized = NormalizeQuestName(questName);
        List<QuestCatalogEntry> matches;
        lock (_lock)
        {
            matches = _entries
                .Where(entry => NamesMatch(entry.Name, normalized))
                .ToList();
        }

        if (matches.Count == 0)
            return null;

        if (!string.IsNullOrWhiteSpace(questTier))
        {
            var tierMatch = matches.FirstOrDefault(entry =>
                entry.Difficulty.Equals(questTier.Trim(), StringComparison.OrdinalIgnoreCase));

            if (tierMatch != null)
                return tierMatch;
        }

        return matches
            .OrderBy(entry => TierPriority(entry.Difficulty))
            .ThenBy(entry => entry.Level)
            .First();
    }

    /// <summary>
    /// Returns the catalog's canonical spelling for API matching.
    /// In-game titles sometimes differ only by case (e.g. "Murder By Night" vs "Murder by Night").
    /// </summary>
    public string ResolveCanonicalName(string questName, string? questTier = null)
    {
        if (string.IsNullOrWhiteSpace(questName))
            return questName;

        var entry = FindEntry(questName, questTier);
        return entry == null || string.IsNullOrWhiteSpace(entry.Name)
            ? questName.Trim()
            : entry.Name.Trim();
    }

    public (int? BaseLevel, int? EffectiveLevel, string QuestTier) ResolveLevels(
        string questName,
        string runDifficulty,
        long xpHeroic,
        long xpEpic,
        long xpLegendary,
        int characterLevel = 0)
    {
        var questTier = ResolveQuestTier(questName, xpHeroic, xpEpic, xpLegendary, characterLevel, runDifficulty);
        var entry = FindEntry(questName, questTier);
        if (entry == null || entry.Level <= 0)
            return (null, null, questTier);

        var effective = QuestLevelResolver.ComputeEffectiveLevel(entry.Level, runDifficulty);
        return (entry.Level, effective, entry.Difficulty);
    }

    /// <summary>
    /// Pick Heroic/Epic/Legendary using XP when that remake exists in catalog for this quest.
    /// Avoids treating Heroic-quest adventure XP in the epic counter as an Epic remake.
    /// </summary>
    public string ResolveQuestTier(
        string questName,
        long xpHeroic,
        long xpEpic,
        long xpLegendary,
        int characterLevel = 0,
        string? runDifficulty = null)
    {
        List<QuestCatalogEntry> matches;
        lock (_lock)
        {
            var normalized = NormalizeQuestName(questName);
            matches = string.IsNullOrWhiteSpace(questName)
                ? new List<QuestCatalogEntry>()
                : _entries.Where(entry => NamesMatch(entry.Name, normalized)).ToList();
        }

        bool HasTier(string tier) =>
            matches.Any(entry => entry.Difficulty.Equals(tier, StringComparison.OrdinalIgnoreCase));

        var remakeHint = runDifficulty ?? string.Empty;
        if ((xpLegendary > 0 || remakeHint.Contains("Legendary", StringComparison.OrdinalIgnoreCase))
            && (matches.Count == 0 || HasTier("Legendary")))
            return "Legendary";

        if ((xpEpic > 0 || remakeHint.StartsWith("Epic", StringComparison.OrdinalIgnoreCase))
            && HasTier("Epic"))
            return "Epic";

        if (HasTier("Heroic"))
            return "Heroic";

        return QuestLevelResolver.InferQuestTier(xpHeroic, xpEpic, xpLegendary, characterLevel);
    }

    public string? InferRunDifficultyFromXp(
        string questName,
        long xpHeroic,
        long xpEpic,
        long xpLegendary,
        int characterLevel = 0)
    {
        var questTier = QuestLevelResolver.InferQuestTier(xpHeroic, xpEpic, xpLegendary, characterLevel);
        var entry = FindEntry(questName, questTier);
        if (entry == null)
            return null;

        var xp = xpLegendary > 0 ? xpLegendary : xpEpic > 0 ? xpEpic : xpHeroic;
        if (xp <= 0)
            return null;

        var candidates = new (string Label, int Value)[]
        {
            ("Solo", entry.SoloXp ?? 0),
            ("Casual", entry.SoloXp ?? 0),
            ("Normal", entry.NormalXp ?? 0),
            ("Hard", entry.HardXp ?? 0),
            ("Elite", entry.EliteXp ?? 0),
            ("Reaper", entry.EliteXp ?? 0)
        }.Where(candidate => candidate.Value > 0).ToList();

        if (candidates.Count == 0)
            return null;

        // Absolute match (no VIP / daily bonus).
        var absolute = candidates
            .Select(candidate => (candidate.Label, Delta: Math.Abs(xp - candidate.Value), candidate.Value))
            .OrderBy(candidate => candidate.Delta)
            .First();

        var absoluteTolerance = Math.Max(50, (int)(absolute.Value * 0.12));
        if (absolute.Delta <= absoluteTolerance)
            return absolute.Label;

        // VIP / first-timer bonuses scale all settings equally. Use the setting whose
        // xp/catalogXp ratio is closest to the median ratio across Normal/Hard/Elite.
        var settingCandidates = candidates
            .Where(candidate => candidate.Label is "Normal" or "Hard" or "Elite")
            .Select(candidate => (candidate.Label, Ratio: xp / (double)candidate.Value))
            .OrderBy(candidate => candidate.Ratio)
            .ToList();

        if (settingCandidates.Count < 2)
            return null;

        var medianRatio = settingCandidates.Count % 2 == 1
            ? settingCandidates[settingCandidates.Count / 2].Ratio
            : (settingCandidates[settingCandidates.Count / 2 - 1].Ratio
               + settingCandidates[settingCandidates.Count / 2].Ratio) / 2.0;

        var bestScaled = settingCandidates
            .Select(candidate => (
                candidate.Label,
                Delta: Math.Abs(candidate.Ratio - medianRatio)))
            .OrderBy(candidate => candidate.Delta)
            .First();

        // VIP / first-timer scale all settings equally — the median-ratio setting is the match.
        return bestScaled.Label;
    }

    public int? TryGetReferenceXp(string questName, string difficulty)
    {
        var entry = FindEntry(questName);
        if (entry == null)
            return null;

        if (string.IsNullOrWhiteSpace(difficulty))
            return null;

        var normalized = difficulty.Trim();

        if (normalized.StartsWith('R') && int.TryParse(normalized[1..], out _))
            return (entry.EliteXp ?? 0) > 0 ? entry.EliteXp : entry.HardXp;

        return normalized switch
        {
            "Solo" or "Casual" => (entry.SoloXp ?? 0) > 0 ? entry.SoloXp : null,
            "Normal" => (entry.NormalXp ?? 0) > 0 ? entry.NormalXp : null,
            "Hard" => (entry.HardXp ?? 0) > 0 ? entry.HardXp : null,
            "Elite" or "Reaper" => (entry.EliteXp ?? 0) > 0 ? entry.EliteXp : null,
            _ when normalized.Contains("Hard", StringComparison.OrdinalIgnoreCase) =>
                (entry.HardXp ?? 0) > 0 ? entry.HardXp : null,
            _ when normalized.Contains("Elite", StringComparison.OrdinalIgnoreCase) =>
                (entry.EliteXp ?? 0) > 0 ? entry.EliteXp : null,
            _ when normalized.Contains("Normal", StringComparison.OrdinalIgnoreCase) =>
                (entry.NormalXp ?? 0) > 0 ? entry.NormalXp : null,
            _ => null
        };
    }

    private static int TierPriority(string tier)
    {
        return tier.ToUpperInvariant() switch
        {
            "HEROIC" => 0,
            "EPIC" => 1,
            "LEGENDARY" => 2,
            _ => 3
        };
    }

    private static QuestCatalogLoadResult LoadEntries(string pluginFolder)
    {
        var diagnostics = new List<string>();
        var bestEntries = new List<QuestCatalogEntry>();
        var bestSource = "not found";

        foreach (var (source, entries, note) in EnumerateLoadCandidates(pluginFolder))
        {
            diagnostics.Add(note);
            if (entries.Count <= bestEntries.Count)
                continue;

            bestEntries = entries;
            bestSource = source;
        }

        if (bestEntries.Count == 0)
            diagnostics.Add("No quest catalog entries loaded from any source.");

        return new QuestCatalogLoadResult
        {
            Entries = bestEntries,
            LoadedFrom = bestSource,
            Diagnostics = diagnostics
        };
    }

    private static IEnumerable<(string Source, List<QuestCatalogEntry> Entries, string Note)> EnumerateLoadCandidates(string pluginFolder)
    {
        var (embeddedEntries, embeddedNote) = LoadFromEmbeddedResource();
        yield return ("embedded resource", embeddedEntries, embeddedNote);

        foreach (var path in CandidatePaths(pluginFolder))
        {
            if (!File.Exists(path))
            {
                yield return (path, [], $"missing: {path}");
                continue;
            }

            var (entries, note) = TryLoadQuestFile(path);
            yield return (path, entries, note);
        }
    }

    private static (List<QuestCatalogEntry> Entries, string Note) TryLoadQuestFile(string path)
    {
        try
        {
            var entries = ParseQuestFile(path);
            return (
                entries,
                entries.Count > 0
                    ? $"loaded {entries.Count} entries from {path}"
                    : $"empty after parse: {path}");
        }
        catch (Exception ex)
        {
            return ([], $"parse failed for {path}: {ex.Message}");
        }
    }

    private static (List<QuestCatalogEntry> Entries, string Note) LoadFromEmbeddedResource()
    {
        try
        {
            var assembly = typeof(QuestCatalog).Assembly;
            var resourceNames = assembly.GetManifestResourceNames();
            var resourceName = resourceNames
                .FirstOrDefault(name => name.EndsWith("quests.json", StringComparison.OrdinalIgnoreCase));

            if (resourceName == null)
            {
                var available = resourceNames.Length == 0
                    ? "none"
                    : string.Join(", ", resourceNames);
                return ([], $"embedded resource not found (available: {available})");
            }

            using var stream = assembly.GetManifestResourceStream(resourceName);
            if (stream == null)
                return ([], $"embedded stream missing for {resourceName}");

            using var reader = new StreamReader(stream);
            var entries = ParseQuestJson(reader.ReadToEnd());
            return (
                entries,
                entries.Count > 0
                    ? $"embedded resource {resourceName}: {entries.Count} entries"
                    : $"embedded resource {resourceName}: empty after parse");
        }
        catch (Exception ex)
        {
            // Null XP fields previously blew up typed deserialize — try tolerant parse.
            try
            {
                var assembly = typeof(QuestCatalog).Assembly;
                var resourceName = assembly.GetManifestResourceNames()
                    .FirstOrDefault(name => name.EndsWith("quests.json", StringComparison.OrdinalIgnoreCase));
                if (resourceName != null)
                {
                    using var stream = assembly.GetManifestResourceStream(resourceName);
                    if (stream != null)
                    {
                        using var reader = new StreamReader(stream);
                        var fallback = ParseQuestJsonFallback(reader.ReadToEnd());
                        if (fallback.Count > 0)
                            return (fallback, $"embedded fallback after error ({ex.Message}): {fallback.Count} entries");
                    }
                }
            }
            catch
            {
                // Ignore nested failure.
            }

            return ([], $"embedded resource failed: {ex.Message}");
        }
    }

    private static List<QuestCatalogEntry> ParseQuestFile(string path)
    {
        return ParseQuestJson(File.ReadAllText(path));
    }

    private static List<QuestCatalogEntry> ParseQuestJson(string json)
    {
        if (!TryGetQuestArrayJson(json, out var arrayJson))
            return [];

        try
        {
            var entries = JsonSerializer.Deserialize<List<QuestCatalogEntry>>(arrayJson, JsonOptions);
            if (entries != null && entries.Any(entry => !string.IsNullOrWhiteSpace(entry.Name)))
            {
                return entries
                    .Where(entry => !string.IsNullOrWhiteSpace(entry.Name))
                    .ToList();
            }
        }
        catch
        {
            // Fall through to tolerant parse (null XP fields, etc.).
        }

        return ParseQuestJsonFallback(json);
    }

    private static List<QuestCatalogEntry> ParseQuestJsonFallback(string json)
    {
        using var document = JsonDocument.Parse(json);
        if (!TryGetQuestArrayElement(document.RootElement, out var array))
            return [];

        var entries = new List<QuestCatalogEntry>();
        foreach (var element in array.EnumerateArray())
        {
            if (!element.TryGetProperty("name", out var nameElement))
                continue;

            var name = nameElement.GetString();
            if (string.IsNullOrWhiteSpace(name))
                continue;

            entries.Add(new QuestCatalogEntry
            {
                Name = name,
                Difficulty = element.TryGetProperty("difficulty", out var tierElement)
                    ? tierElement.GetString() ?? string.Empty
                    : string.Empty,
                Level = element.TryGetProperty("level", out var levelElement) && levelElement.TryGetInt32(out var level)
                    ? level
                    : 0,
                SoloXp = ReadNullableIntProperty(element, "soloXP"),
                NormalXp = ReadNullableIntProperty(element, "normalXP"),
                HardXp = ReadNullableIntProperty(element, "hardXP"),
                EliteXp = ReadNullableIntProperty(element, "eliteXP")
            });
        }

        return entries;
    }

    private static bool TryGetQuestArrayJson(string json, out string arrayJson)
    {
        arrayJson = string.Empty;
        try
        {
            using var document = JsonDocument.Parse(json);
            if (!TryGetQuestArrayElement(document.RootElement, out var array))
                return false;

            arrayJson = array.GetRawText();
            return true;
        }
        catch
        {
            return false;
        }
    }

    private static bool TryGetQuestArrayElement(JsonElement root, out JsonElement array)
    {
        if (root.ValueKind == JsonValueKind.Array)
        {
            array = root;
            return true;
        }

        if (root.ValueKind == JsonValueKind.Object
            && root.TryGetProperty("quests", out var quests)
            && quests.ValueKind == JsonValueKind.Array)
        {
            array = quests;
            return true;
        }

        array = default;
        return false;
    }

    private static int? ReadNullableIntProperty(JsonElement element, string propertyName)
    {
        if (!element.TryGetProperty(propertyName, out var valueElement))
            return null;

        if (valueElement.ValueKind is JsonValueKind.Null or JsonValueKind.Undefined)
            return null;

        return valueElement.TryGetInt32(out var value) ? value : null;
    }

    private static IEnumerable<string> CandidatePaths(string pluginFolder)
    {
        foreach (var root in ExpandSearchRoots(pluginFolder))
        {
            yield return Path.Combine(root, "Data", "quests.json");
            yield return Path.Combine(root, "quests.json");
        }
    }

    private static IEnumerable<string> ExpandSearchRoots(string pluginFolder)
    {
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        foreach (var start in new[]
                 {
                     pluginFolder,
                     Path.GetDirectoryName(typeof(QuestCatalog).Assembly.Location),
                     AppContext.BaseDirectory
                 })
        {
            if (string.IsNullOrWhiteSpace(start))
                continue;

            string? current;
            try
            {
                current = Path.GetFullPath(start);
            }
            catch
            {
                continue;
            }

            // Walk up so obj/bin plugin folders still find plugins/DungeonTracker/quests.json.
            for (var depth = 0; depth < 8 && !string.IsNullOrWhiteSpace(current); depth++)
            {
                if (seen.Add(current))
                    yield return current;

                var parent = Path.GetDirectoryName(current);
                if (string.IsNullOrWhiteSpace(parent) || parent == current)
                    break;
                current = parent;
            }
        }
    }

    private static string NormalizeQuestName(string questName)
    {
        return questName
            .Trim()
            .Replace('\u2019', '\'')
            .Replace('\u2018', '\'');
    }

    private static bool NamesMatch(string catalogName, string questName)
    {
        return NormalizeQuestName(catalogName).Equals(NormalizeQuestName(questName), StringComparison.OrdinalIgnoreCase);
    }
}
