namespace DungeonTracker.Services;

/// <summary>
/// DDO quest levels: each quest has a base level; effective level scales with run difficulty.
/// See https://ddowiki.com/page/Base_level — Casual/Solo = base−1, Normal = base, Hard = base+1, Elite/Reaper = base+2.
/// </summary>
public static class QuestLevelResolver
{
    public static string InferQuestTier(long xpHeroic, long xpEpic, long xpLegendary, int characterLevel = 0)
    {
        if (xpLegendary > 0)
            return "Legendary";

        if (xpEpic > 0)
            return "Epic";

        if (characterLevel >= 20)
        {
            // Epic quests use the Epic row in the catalog even before epic XP is awarded.
            return "Heroic";
        }

        return "Heroic";
    }

    public static int? ComputeEffectiveLevel(int baseLevel, string runDifficulty)
    {
        if (baseLevel <= 0 || string.IsNullOrWhiteSpace(runDifficulty))
            return null;

        var difficulty = runDifficulty.Trim();
        if (IsUnknownDifficulty(difficulty))
            return null;

        var offset = GetDifficultyOffset(difficulty);
        return offset == null ? null : baseLevel + offset.Value;
    }

    public static string FormatLevelLabel(int? baseLevel, int? effectiveLevel)
    {
        if (baseLevel is > 0 && effectiveLevel is > 0)
            return baseLevel == effectiveLevel ? $"L{effectiveLevel}" : $"L{baseLevel}→{effectiveLevel}";

        if (effectiveLevel is > 0)
            return $"L{effectiveLevel}";

        if (baseLevel is > 0)
            return $"L{baseLevel}";

        return "—";
    }

    public static bool IsUnknownDifficulty(string? difficulty)
    {
        if (string.IsNullOrWhiteSpace(difficulty))
            return true;

        return difficulty.Equals("Unknown", StringComparison.OrdinalIgnoreCase)
            || difficulty.StartsWith("Unknown (", StringComparison.OrdinalIgnoreCase);
    }

    private static int? GetDifficultyOffset(string difficulty)
    {
        var normalized = difficulty.Trim();

        // Strip remake prefix; offset is about challenge setting only.
        if (normalized.StartsWith("Epic ", StringComparison.OrdinalIgnoreCase))
            normalized = normalized["Epic ".Length..].Trim();
        else if (normalized.StartsWith("Legendary ", StringComparison.OrdinalIgnoreCase))
            normalized = normalized["Legendary ".Length..].Trim();

        if (normalized.Contains("Casual", StringComparison.OrdinalIgnoreCase)
            || normalized.Equals("Solo", StringComparison.OrdinalIgnoreCase))
            return -1;

        if (normalized.Equals("Normal", StringComparison.OrdinalIgnoreCase)
            || normalized.Equals("Epic", StringComparison.OrdinalIgnoreCase)
            || normalized.Equals("Legendary", StringComparison.OrdinalIgnoreCase))
            return 0;

        if (normalized.Equals("Hard", StringComparison.OrdinalIgnoreCase)
            || normalized.StartsWith("Hard R", StringComparison.OrdinalIgnoreCase))
            return 1;

        if (normalized.Equals("Elite", StringComparison.OrdinalIgnoreCase)
            || normalized.Contains("Reaper", StringComparison.OrdinalIgnoreCase)
            || (normalized.StartsWith("R", StringComparison.OrdinalIgnoreCase)
                && normalized.Length > 1
                && char.IsDigit(normalized[1])))
            return 2;

        return null;
    }

    public static string? InferDifficultyFromLevelOffset(int baseLevel, int effectiveLevel)
    {
        return (effectiveLevel - baseLevel) switch
        {
            -1 => "Casual",
            0 => "Normal",
            1 => "Hard",
            2 => "Elite",
            _ => null
        };
    }
}
