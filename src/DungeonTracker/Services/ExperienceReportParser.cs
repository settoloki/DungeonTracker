using System.Globalization;
using System.Text.RegularExpressions;

namespace DungeonTracker.Services;

public static partial class ExperienceReportParser
{
    private static readonly string[] DifficultyTokens =
    [
        "Epic Reaper", "Epic Elite", "Epic Hard", "Epic Casual", "Epic",
        "Reaper", "Elite", "Hard", "Normal", "Casual", "Solo"
    ];

    public static ParsedExperienceReport Parse(string? title, string? text, string? uiDifficultyLabel = null)
    {
        var combined = $"{title}\n{text}".Trim();
        if (string.IsNullOrWhiteSpace(combined))
            return ParsedExperienceReport.Empty;

        var difficulty = ParseDifficulty(combined, uiDifficultyLabel);
        var totalXp = ParseTotalXp(combined);
        var questSeconds = ParseQuestDurationSeconds(combined);
        var objectives = ParseObjectiveLines(combined);

        return new ParsedExperienceReport
        {
            Title = title?.Trim() ?? string.Empty,
            RawText = combined,
            Difficulty = difficulty,
            TotalXp = totalXp,
            QuestDurationSeconds = questSeconds,
            Objectives = objectives,
            HasUsableData = !string.IsNullOrWhiteSpace(difficulty)
                || totalXp > 0
                || questSeconds > 0
                || objectives.Count > 0
        };
    }

    private static string? ParseDifficulty(string combined, string? uiDifficultyLabel)
    {
        if (!string.IsNullOrWhiteSpace(uiDifficultyLabel)
            && !QuestLevelResolver.IsUnknownDifficulty(uiDifficultyLabel))
            return uiDifficultyLabel.Trim();

        var reaperMatch = ReaperDifficultyRegex().Match(combined);
        if (reaperMatch.Success)
            return $"R{reaperMatch.Groups[1].Value}";

        var epicReaperMatch = EpicReaperDifficultyRegex().Match(combined);
        if (epicReaperMatch.Success)
            return $"Epic R{epicReaperMatch.Groups[1].Value}";

        foreach (var token in DifficultyTokens)
        {
            if (combined.Contains(token, StringComparison.OrdinalIgnoreCase))
                return token;
        }

        return null;
    }

    private static long ParseTotalXp(string combined)
    {
        foreach (Match match in TotalXpRegex().Matches(combined))
        {
            if (long.TryParse(match.Groups[1].Value, NumberStyles.AllowThousands, CultureInfo.InvariantCulture, out var xp)
                && xp > 0)
                return xp;
        }

        long best = 0;
        foreach (Match match in XpLineRegex().Matches(combined))
        {
            if (long.TryParse(match.Groups[1].Value, NumberStyles.AllowThousands, CultureInfo.InvariantCulture, out var xp)
                && xp > best)
                best = xp;
        }

        return best;
    }

    private static double ParseQuestDurationSeconds(string combined)
    {
        var match = QuestTimeRegex().Match(combined);
        if (!match.Success)
            return 0;

        if (int.TryParse(match.Groups[1].Value, out var minutes)
            && int.TryParse(match.Groups[2].Value, out var seconds))
            return minutes * 60 + seconds;

        return 0;
    }

    private static List<ParsedObjectiveXp> ParseObjectiveLines(string combined)
    {
        var results = new List<ParsedObjectiveXp>();
        foreach (var line in combined.Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
        {
            var match = ObjectiveXpRegex().Match(line);
            if (!match.Success)
                continue;

            if (!long.TryParse(match.Groups[2].Value, NumberStyles.AllowThousands, CultureInfo.InvariantCulture, out var xp))
                continue;

            results.Add(new ParsedObjectiveXp(match.Groups[1].Value.Trim(), xp));
        }

        return results;
    }

    [GeneratedRegex(@"\bR\s*(\d{1,2})\b", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex ReaperDifficultyRegex();

    [GeneratedRegex(@"\bEpic\s+R\s*(\d{1,2})\b", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex EpicReaperDifficultyRegex();

    [GeneratedRegex(@"(?:Total|Quest|Base|Awarded|Experience)[^\d]{0,40}([\d,]+)\s*(?:XP|Experience)", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex TotalXpRegex();

    [GeneratedRegex(@"([\d,]+)\s*(?:XP|Experience)", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex XpLineRegex();

    [GeneratedRegex(@"(?:Time|Duration|Elapsed)[^\d]{0,20}(\d{1,3}):(\d{2})", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex QuestTimeRegex();

    [GeneratedRegex(@"^(.+?)\s+([\d,]+)\s*(?:XP|Experience)\b", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex ObjectiveXpRegex();
}

public sealed class ParsedExperienceReport
{
    public static ParsedExperienceReport Empty { get; } = new();

    public string Title { get; init; } = string.Empty;
    public string RawText { get; init; } = string.Empty;
    public string? Difficulty { get; init; }
    public long TotalXp { get; init; }
    public double QuestDurationSeconds { get; init; }
    public IReadOnlyList<ParsedObjectiveXp> Objectives { get; init; } = Array.Empty<ParsedObjectiveXp>();
    public bool HasUsableData { get; init; }

    public bool LooksComplete =>
        RawText.Contains("XP awarded", StringComparison.OrdinalIgnoreCase)
        || RawText.Contains("Bonuses locked in", StringComparison.OrdinalIgnoreCase)
        || RawText.Contains("Completed.", StringComparison.OrdinalIgnoreCase)
        || RawText.Contains("adventure is complete", StringComparison.OrdinalIgnoreCase)
        || TotalXp > 0;
}

public sealed record ParsedObjectiveXp(string Label, long Xp);
