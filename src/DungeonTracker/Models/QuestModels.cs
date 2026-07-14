namespace DungeonTracker.Models;

public enum TrackingPhase
{
    Idle,
    Tracking,
    Completed
}

public enum RunKind
{
    Dungeon,
    AdventureArea
}

public sealed class TrackingStatus
{
    public TrackingPhase Phase { get; init; } = TrackingPhase.Idle;
    public string Message { get; init; } = "Waiting for dungeon entry";
}

public sealed class QuestRunRecord
{
    public uint QuestDid { get; set; }
    public string QuestName { get; set; } = string.Empty;
    public string Difficulty { get; set; } = "Unknown";
    public string QuestTier { get; set; } = string.Empty;
    public int? BaseQuestLevel { get; set; }
    public int? EffectiveQuestLevel { get; set; }
    public RunKind RunKind { get; set; } = RunKind.Dungeon;
    public string CharacterName { get; set; } = string.Empty;
    public string ServerName { get; set; } = string.Empty;
    public DateTime StartedAtUtc { get; set; }
    public DateTime CompletedAtUtc { get; set; }
    public double DurationSeconds { get; set; }
    public long XpHeroic { get; set; }
    public long XpEpic { get; set; }
    public long XpLegendary { get; set; }
    public long XpReaper { get; set; }
    public long XpTotal { get; set; }
    public double XpPerMinute { get; set; }
    /// <summary>Completed, Failed, or Abandoned.</summary>
    public string Outcome { get; set; } = "Completed";
}

public sealed class ActiveQuestRun
{
    public uint QuestDid { get; init; }
    public string QuestName { get; init; } = string.Empty;
    public string Difficulty { get; set; } = "Unknown";
    public string QuestTier { get; set; } = string.Empty;
    public int? BaseQuestLevel { get; set; }
    public int? EffectiveQuestLevel { get; set; }
    public RunKind RunKind { get; init; } = RunKind.Dungeon;
    public string CharacterName { get; init; } = string.Empty;
    public string ServerName { get; init; } = string.Empty;
    public DateTime StartedAtUtc { get; init; }
    /// <summary>Banked in-instance seconds from completed segments (excludes paused time outside).</summary>
    public double AccumulatedActiveSeconds { get; set; }
    /// <summary>When the current in-instance segment started; null while the timer is paused.</summary>
    public DateTime? ActiveSegmentStartedAtUtc { get; set; }
    public long StartXpHeroic { get; init; }
    public long StartXpEpic { get; init; }
    public long StartXpLegendary { get; init; }
    public long StartXpReaper { get; init; }

    public bool IsTimerPaused => ActiveSegmentStartedAtUtc == null;

    public double GetElapsedSeconds(DateTime utcNow)
    {
        var elapsed = AccumulatedActiveSeconds;
        if (ActiveSegmentStartedAtUtc is DateTime segmentStart)
        {
            var segment = (utcNow - segmentStart).TotalSeconds;
            if (segment > 0)
                elapsed += segment;
        }

        return elapsed < 0 ? 0 : elapsed;
    }
}

public sealed class QuestHistoryFile
{
    public List<QuestRunRecord> Runs { get; set; } = new();
}
