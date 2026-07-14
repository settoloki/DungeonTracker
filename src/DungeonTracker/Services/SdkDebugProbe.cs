using System.Text;
using VoK.Sdk;
using VoK.Sdk.Ddo;
using VoK.Sdk.Ddo.Enums;
using VoK.Sdk.Enums;
using VoK.Sdk.Properties;

namespace DungeonTracker.Services;

public sealed class SdkDebugSnapshot
{
    public DateTime CapturedAtUtc { get; init; } = DateTime.UtcNow;

    public string CharacterName { get; init; } = "—";
    public ulong CharacterId { get; init; }
    public string ServerName { get; init; } = "—";
    public bool? InTown { get; init; }
    public uint InstanceQuestDid { get; init; }
    public uint JournalQuestDid { get; init; }
    public uint ObjectiveDid { get; init; }
    public uint MapQuestDid { get; init; }
    public uint MapAreaDid { get; init; }
    public uint MapChapterDid { get; init; }
    public bool MapValid { get; init; }
    public bool MapIndoors { get; init; }
    public uint MapRegion { get; init; }
    public uint MapImageId { get; init; }
    public int MapLandblockX { get; init; }
    public int MapLandblockY { get; init; }
    public uint ActiveQuestProp { get; init; }
    public uint PlayerActiveQuestProp { get; init; }
    public string CachedDifficulty { get; init; } = "—";
    public string SelectorDifficulty { get; init; } = "—";
    public string InventoryDifficulty { get; init; } = "—";
    public string UiDifficultyName { get; init; } = "—";
    public string DifficultyLevel { get; init; } = "—";
    public string ResolvedDifficulty { get; init; } = "—";
    public int? ReaperLevel { get; init; }
    public long XpHeroic { get; init; }
    public long XpEpic { get; init; }
    public long XpLegendary { get; init; }
    public int? CharacterLevel { get; init; }
    public int PartyCount { get; init; }
    public string PartyMembers { get; init; } = "—";
    public uint ResolvedQuestDid { get; init; }
    public string ResolvedSource { get; init; } = "none";
    public uint PortalEntryQuestDid { get; init; }
    public bool HasDungeonDifficulty { get; init; }
    public bool TrackingActive { get; init; }
    public string ActiveRunLabel { get; init; } = "—";
    public bool ObjectiveSeenDuringRun { get; init; }
    public int ObjectiveClearPolls { get; init; }
    public int InstanceAbsentPolls { get; init; }
    public bool EntryWatchActive { get; init; }
    public string LastDungeonEvent { get; init; } = "—";
    public DateTime? LastDungeonEventUtc { get; init; }
    public string LastScriptEvent { get; init; } = "—";
    public DateTime? LastScriptEventUtc { get; init; }
    public string LastRewardEvent { get; init; } = "—";
    public DateTime? LastRewardEventUtc { get; init; }
    public string LastRewardClosedEvent { get; init; } = "—";
    public DateTime? LastRewardClosedEventUtc { get; init; }
    public string LastChestEvent { get; init; } = "—";
    public DateTime? LastChestEventUtc { get; init; }
    public string LastPortalEvent { get; init; } = "—";
    public DateTime? LastPortalEventUtc { get; init; }
    public string LastAcceptedQuest { get; init; } = "—";
    public uint LastAcceptedQuestDid { get; init; }
    public string StatusMessage { get; init; } = "—";
    public int QuestCatalogCount { get; init; }
    public string QuestCatalogSource { get; init; } = "—";
    public string CatalogLookup { get; init; } = "—";
    public string CharacterDataFolder { get; init; } = "—";
    public string PluginFolder { get; init; } = "—";
    public string LivePortalDifficulty { get; init; } = "—";
    public string ArmedPortalDifficulty { get; init; } = "—";
    public string SummaryTitle { get; init; } = "—";
    public string SummaryText { get; init; } = "—";
    public bool SummaryLooksComplete { get; init; }
    public string ExperienceReportDifficulty { get; init; } = "—";
    public long ExperienceReportXp { get; init; }
    public string ExperienceReportSource { get; init; } = "—";
    public int? ChallengeScore { get; init; }

    public string FormatReport()
    {
        var sb = new StringBuilder();
        sb.AppendLine($"Quest catalog: {QuestCatalogCount} names ({QuestCatalogSource})");
        sb.AppendLine($"Plugin folder: {PluginFolder}");
        sb.AppendLine($"Data folder: {CharacterDataFolder}");
        sb.AppendLine($"Catalog lookup: {CatalogLookup}");
        sb.AppendLine();
        sb.AppendLine("=== Character / Server ===");
        sb.AppendLine($"Character: {CharacterName} (0x{CharacterId:X16})");
        sb.AppendLine($"Server: {ServerName}");
        sb.AppendLine($"Level: {FormatNullable(CharacterLevel)}");
        sb.AppendLine($"XP Heroic: {XpHeroic:N0}  Epic: {XpEpic:N0}  Legendary: {XpLegendary:N0}");
        sb.AppendLine();
        sb.AppendLine("=== Zone / Instance ===");
        sb.AppendLine($"InTown: {FormatNullable(InTown)}");
        sb.AppendLine($"GetInstanceQuestDid: 0x{InstanceQuestDid:X8}");
        sb.AppendLine($"GetCurrentQuestDid (journal): 0x{JournalQuestDid:X8}  ({LastAcceptedQuest})");
        sb.AppendLine($"GetCurrentQuestObjectiveDid: 0x{ObjectiveDid:X8}");
        sb.AppendLine($"Resolved instance quest: 0x{ResolvedQuestDid:X8} ({ResolvedSource})");
        sb.AppendLine($"Portal entry quest: 0x{PortalEntryQuestDid:X8}");
        sb.AppendLine($"Dungeon_ActiveQuest: 0x{ActiveQuestProp:X8}");
        sb.AppendLine($"Dungeon_Player_ActiveQuest: 0x{PlayerActiveQuestProp:X8}");
        sb.AppendLine($"Has dungeon difficulty cached: {HasDungeonDifficulty}");
        sb.AppendLine();
        sb.AppendLine("=== Map ===");
        sb.AppendLine($"Map valid: {MapValid}  indoors: {MapIndoors}  region: {MapRegion}");
        sb.AppendLine($"AreaDid: 0x{MapAreaDid:X8}  CurrentQuestDid: 0x{MapQuestDid:X8}  ChapterDid: 0x{MapChapterDid:X8}");
        sb.AppendLine($"MapImageId: 0x{MapImageId:X8}  landblock: ({MapLandblockX}, {MapLandblockY})");
        sb.AppendLine();
        sb.AppendLine("=== Difficulty (solid sources) ===");
        sb.AppendLine($"Resolver: {ResolvedDifficulty}");
        sb.AppendLine($"Portal live QuestGenericDifficulty: {LivePortalDifficulty}");
        sb.AppendLine($"Armed portal difficulty: {ArmedPortalDifficulty}");
        sb.AppendLine($"QuestSelector_QuestDifficulty: {SelectorDifficulty}");
        sb.AppendLine($"Inventory_DungeonDifficulty: {InventoryDifficulty}");
        sb.AppendLine($"Dungeon_UI_DifficultyName: {UiDifficultyName}");
        sb.AppendLine($"Dungeon_CachedDifficulty (weak): {CachedDifficulty}");
        sb.AppendLine($"Dungeon_DifficultyLevel (CR offset): {DifficultyLevel}");
        sb.AppendLine($"Dungeon_CachedReaperLevel: {FormatNullable(ReaperLevel)}");
        sb.AppendLine();
        sb.AppendLine("=== Summary props (observe only — not used for complete) ===");
        sb.AppendLine($"Dungeon_Summary_Title: {SummaryTitle}");
        sb.AppendLine($"Dungeon_Summary_Text: {Truncate(SummaryText, 240)}");
        sb.AppendLine($"Parsed difficulty: {ExperienceReportDifficulty}");
        sb.AppendLine($"Parsed XP: {ExperienceReportXp:N0}");
        sb.AppendLine($"Report source: {ExperienceReportSource}");
        sb.AppendLine($"Challenge score: {FormatNullable(ChallengeScore)}");
        sb.AppendLine($"Looks complete: {SummaryLooksComplete}");
        sb.AppendLine("Completion signal: OnAddAlert quest/adventure completed only.");
        sb.AppendLine();
        sb.AppendLine("=== Party ===");
        sb.AppendLine($"Party count: {PartyCount}");
        sb.AppendLine($"Members: {PartyMembers}");
        sb.AppendLine();
        sb.AppendLine("=== Tracker state ===");
        sb.AppendLine($"Status: {StatusMessage}");
        sb.AppendLine($"Tracking active: {TrackingActive}  run: {ActiveRunLabel}");
        sb.AppendLine($"Objective seen: {ObjectiveSeenDuringRun}  clear polls: {ObjectiveClearPolls}");
        sb.AppendLine($"Instance absent polls: {InstanceAbsentPolls}  entry watch: {EntryWatchActive}");
        sb.AppendLine();
        sb.AppendLine("=== Recent events ===");
        sb.AppendLine($"Portal: {LastPortalEvent} @ {FormatTime(LastPortalEventUtc)}");
        sb.AppendLine($"DungeonEvent: {LastDungeonEvent} @ {FormatTime(LastDungeonEventUtc)}");
        sb.AppendLine($"Script: {LastScriptEvent} @ {FormatTime(LastScriptEventUtc)}");
        sb.AppendLine($"Chest: {LastChestEvent} @ {FormatTime(LastChestEventUtc)}");        sb.AppendLine($"Reward opened: {LastRewardEvent} @ {FormatTime(LastRewardEventUtc)}");
        sb.AppendLine($"Reward closed: {LastRewardClosedEvent} @ {FormatTime(LastRewardClosedEventUtc)}");
        return sb.ToString();
    }

    private static string Truncate(string value, int maxLength)
    {
        if (string.IsNullOrWhiteSpace(value))
            return "—";

        var normalized = value.Replace('\r', ' ').Replace('\n', ' ').Trim();
        return normalized.Length <= maxLength ? normalized : normalized[..maxLength] + "...";
    }

    private static string FormatNullable<T>(T? value) where T : struct => value?.ToString() ?? "null";

    private static string FormatNullable(bool? value) => value switch
    {
        true => "true",
        false => "false",
        _ => "null"
    };

    private static string FormatTime(DateTime? utc) => utc?.ToLocalTime().ToString("HH:mm:ss") ?? "—";
}

public sealed class SdkDebugProbe
{
    private string _lastDungeonEvent = "—";
    private DateTime? _lastDungeonEventUtc;
    private string _lastScriptEvent = "—";
    private DateTime? _lastScriptEventUtc;
    private string _lastRewardEvent = "—";
    private DateTime? _lastRewardEventUtc;
    private string _lastRewardClosedEvent = "—";
    private DateTime? _lastRewardClosedEventUtc;
    private string _lastChestEvent = "—";
    private DateTime? _lastChestEventUtc;
    private string _lastPortalEvent = "—";
    private DateTime? _lastPortalEventUtc;

    public void NotePortalEvent(string message) => SetEvent(ref _lastPortalEvent, ref _lastPortalEventUtc, message);

    public void NoteDungeonEvent(DungeonEvent dungeonEvent) =>
        SetEvent(ref _lastDungeonEvent, ref _lastDungeonEventUtc, dungeonEvent.ToString());

    public void NoteScriptEvent(string message) => SetEvent(ref _lastScriptEvent, ref _lastScriptEventUtc, message);

    public void NoteRewardEvent(ulong containerId) =>
        SetEvent(ref _lastRewardEvent, ref _lastRewardEventUtc, $"opened 0x{containerId:X16}");

    public void NoteRewardClosed(ulong containerId) =>
        SetEvent(ref _lastRewardClosedEvent, ref _lastRewardClosedEventUtc, $"closed 0x{containerId:X16}");

    public void NoteChestEvent(ulong containerId) =>
        SetEvent(ref _lastChestEvent, ref _lastChestEventUtc, $"opened 0x{containerId:X16}");

    public SdkDebugSnapshot Capture(
        IDdoGameDataProvider provider,
        InstanceDiagnostics diagnostics,
        QuestTrackerRuntimeState runtime)
    {
        var character = provider.GetCurrentCharacter();
        var properties = character?.PropertyCollection;
        var mapInfo = TryGetMapInfo(provider);
        var xp = XpTracker.Capture(provider);
        var party = TryGetParty(provider);

        return new SdkDebugSnapshot
        {
            CharacterName = provider.GetCurrentCharacterName() ?? "—",
            CharacterId = provider.GetCurrentCharacterId() ?? 0,
            ServerName = provider.GetServerName() ?? "—",
            InTown = provider.InTown(),
            InstanceQuestDid = ReadUInt(provider.GetInstanceQuestDid()),
            JournalQuestDid = ReadUInt(provider.GetCurrentQuestDid()),
            ObjectiveDid = ReadUInt(provider.GetCurrentQuestObjectiveDid()),
            MapQuestDid = mapInfo?.CurrentQuestDid ?? 0,
            MapAreaDid = mapInfo?.AreaDid ?? 0,
            MapChapterDid = mapInfo?.CurrentChapterDid ?? 0,
            MapValid = mapInfo?.IsValid == true,
            MapIndoors = mapInfo?.IsIndoors == true,
            MapRegion = mapInfo?.Region ?? 0,
            MapImageId = mapInfo?.MapImageId ?? 0,
            MapLandblockX = mapInfo?.PlayerLandblockX ?? 0,
            MapLandblockY = mapInfo?.PlayerLandblockY ?? 0,
            ActiveQuestProp = diagnostics.ActiveQuestProp,
            PlayerActiveQuestProp = diagnostics.PlayerActiveQuestProp,
            CachedDifficulty = ReadPropertyLabelAcrossBags(character, DdoProperty.Dungeon_CachedDifficulty),
            SelectorDifficulty = ReadPropertyLabelAcrossBags(character, DdoProperty.QuestSelector_QuestDifficulty),
            InventoryDifficulty = ReadPropertyLabelAcrossBags(character, DdoProperty.Inventory_DungeonDifficulty),
            UiDifficultyName = ReadPropertyLabelAcrossBags(character, DdoProperty.Dungeon_UI_DifficultyName),
            DifficultyLevel = ReadPropertyLabelAcrossBags(character, DdoProperty.Dungeon_DifficultyLevel),
            ResolvedDifficulty = QuestDifficultyResolver.DescribeResolution(provider),
            ReaperLevel = properties?.GetInt32PropertyValue((uint)DdoProperty.Dungeon_CachedReaperLevel),
            XpHeroic = xp.Heroic,
            XpEpic = xp.Epic,
            XpLegendary = xp.Legendary,
            CharacterLevel = properties?.GetInt32PropertyValue((uint)DdoProperty.Character_TotalLevel),
            PartyCount = party.Count,
            PartyMembers = party.Names,
            ResolvedQuestDid = diagnostics.ResolvedQuestDid,
            ResolvedSource = diagnostics.ResolvedSource,
            PortalEntryQuestDid = diagnostics.PortalEntryQuestDid,
            HasDungeonDifficulty = diagnostics.HasDungeonDifficulty,
            TrackingActive = runtime.TrackingActive,
            ActiveRunLabel = runtime.ActiveRunLabel,
            ObjectiveSeenDuringRun = runtime.ObjectiveSeenDuringRun,
            ObjectiveClearPolls = runtime.ObjectiveClearPolls,
            InstanceAbsentPolls = runtime.InstanceAbsentPolls,
            EntryWatchActive = runtime.EntryWatchActive,
            LastDungeonEvent = _lastDungeonEvent,
            LastDungeonEventUtc = _lastDungeonEventUtc,
            LastScriptEvent = _lastScriptEvent,
            LastScriptEventUtc = _lastScriptEventUtc,
            LastRewardEvent = _lastRewardEvent,
            LastRewardEventUtc = _lastRewardEventUtc,
            LastRewardClosedEvent = _lastRewardClosedEvent,
            LastRewardClosedEventUtc = _lastRewardClosedEventUtc,
            LastChestEvent = _lastChestEvent,
            LastChestEventUtc = _lastChestEventUtc,
            LastPortalEvent = _lastPortalEvent,
            LastPortalEventUtc = _lastPortalEventUtc,
            LastAcceptedQuest = runtime.LastAcceptedQuestName,
            LastAcceptedQuestDid = runtime.LastAcceptedQuestDid,
            StatusMessage = runtime.StatusMessage,
            QuestCatalogCount = runtime.QuestCatalogCount,
            QuestCatalogSource = runtime.QuestCatalogSource,
            CatalogLookup = runtime.CatalogLookup,
            CharacterDataFolder = runtime.CharacterDataFolder,
            PluginFolder = runtime.PluginFolder,
            LivePortalDifficulty = runtime.LivePortalDifficulty,
            ArmedPortalDifficulty = runtime.ArmedPortalDifficulty,
            SummaryTitle = string.IsNullOrWhiteSpace(runtime.SummaryTitle) ? "—" : runtime.SummaryTitle,
            SummaryText = string.IsNullOrWhiteSpace(runtime.SummaryText) ? "—" : runtime.SummaryText,
            SummaryLooksComplete = runtime.SummaryLooksComplete,
            ExperienceReportDifficulty = runtime.ExperienceReportDifficulty,
            ExperienceReportXp = runtime.ExperienceReportXp,
            ExperienceReportSource = runtime.ExperienceReportSource,
            ChallengeScore = runtime.ChallengeScore
        };
    }

    private static void SetEvent(ref string field, ref DateTime? timestamp, string message)
    {
        field = message;
        timestamp = DateTime.UtcNow;
    }

    private static IMapInfo? TryGetMapInfo(IDdoGameDataProvider provider)
    {
        try
        {
            var mapInfo = provider.GetMapInfo();
            return mapInfo?.IsValid == true ? mapInfo : null;
        }
        catch
        {
            return null;
        }
    }

    private static (int Count, string Names) TryGetParty(IDdoGameDataProvider provider)
    {
        try
        {
            var members = provider.GetPartyMembers();
            if (members == null || members.Count == 0)
                return (0, "—");

            var names = members
                .Select(member => member?.Name)
                .Where(name => !string.IsNullOrWhiteSpace(name))
                .ToList();

            return (names.Count, names.Count == 0 ? "—" : string.Join(", ", names!));
        }
        catch
        {
            return (0, "—");
        }
    }

    private static string ReadPropertyLabelAcrossBags(VoK.Sdk.IEntity? character, DdoProperty propertyId)
    {
        if (character == null)
            return "—";

        var instance = ReadPropertyLabel(character.InstanceProperties, propertyId);
        if (instance != "—")
            return $"instance:{instance}";

        var shared = ReadPropertyLabel(character.PropertyCollection, propertyId);
        if (shared != "—")
            return $"character:{shared}";

        var weenie = ReadPropertyLabel(character.WeeniePropertyCollection, propertyId);
        return weenie == "—" ? "—" : $"weenie:{weenie}";
    }

    private static string ReadPropertyLabel(IPropertyCollection? properties, DdoProperty propertyId)
    {
        if (properties == null)
            return "—";

        try
        {
            var uintValue = properties.GetUInt32PropertyValue((uint)propertyId);
            if (uintValue is uint rawPropertyId && rawPropertyId >= 0x10000000)
                return QuestDifficultyResolver.FormatPropertyIdLabel(rawPropertyId);

            var enumProperty = properties.GetEnumProperty((uint)propertyId);
            if (enumProperty?.Value != null)
            {
                var enumText = enumProperty.Value.ToString() ?? "—";
                if (uintValue is uint rawId && rawId >= 0x10000000)
                    return $"{enumText} ({QuestDifficultyResolver.FormatPropertyIdLabel(rawId)})";

                return enumText;
            }

            if (uintValue.HasValue)
                return QuestDifficultyResolver.FormatPropertyIdLabel(uintValue.Value);

            var intValue = properties.GetInt32PropertyValue((uint)propertyId);
            if (intValue.HasValue)
            {
                if (intValue.Value >= 0x10000000)
                    return QuestDifficultyResolver.FormatPropertyIdLabel((uint)intValue.Value);

                return intValue.Value.ToString();
            }
        }
        catch
        {
            // Ignore read failures in debug probe.
        }

        return "—";
    }

    private static uint ReadUInt(uint? value) => value is > 0 ? value.Value : 0;
}

public sealed class QuestTrackerRuntimeState
{
    public bool TrackingActive { get; init; }
    public string ActiveRunLabel { get; init; } = "—";
    public bool ObjectiveSeenDuringRun { get; init; }
    public int ObjectiveClearPolls { get; init; }
    public int InstanceAbsentPolls { get; init; }
    public bool EntryWatchActive { get; init; }
    public string LastAcceptedQuestName { get; init; } = "—";
    public uint LastAcceptedQuestDid { get; init; }
    public string StatusMessage { get; init; } = "—";
    public int QuestCatalogCount { get; init; }
    public string QuestCatalogSource { get; init; } = "—";
    public string CatalogLookup { get; init; } = "—";
    public string CharacterDataFolder { get; init; } = "—";
    public string PluginFolder { get; init; } = "—";
    public string LivePortalDifficulty { get; init; } = "—";
    public string ArmedPortalDifficulty { get; init; } = "—";
    public string SummaryTitle { get; init; } = "—";
    public string SummaryText { get; init; } = "—";
    public bool SummaryLooksComplete { get; init; }
    public string ExperienceReportDifficulty { get; init; } = "—";
    public long ExperienceReportXp { get; init; }
    public string ExperienceReportSource { get; init; } = "—";
    public int? ChallengeScore { get; init; }
}
