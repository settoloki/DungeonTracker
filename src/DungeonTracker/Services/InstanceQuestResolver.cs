using VoK.Sdk;
using VoK.Sdk.Ddo;
using VoK.Sdk.Ddo.Enums;
using VoK.Sdk.Enums;
using VoK.Sdk.Properties;

namespace DungeonTracker.Services;

public sealed class InstanceQuestResolver
{
    private const double PortalEntryQuestLifetimeSeconds = 300;

    private uint? _portalEntryQuestDid;
    private DateTime? _portalEntryQuestSetUtc;

    public void NotePortalActivated(IPortalInfo? portalInfo)
    {
        var candidate = PickPortalQuest(portalInfo);
        if (candidate == 0)
            return;

        _portalEntryQuestDid = candidate;
        _portalEntryQuestSetUtc = DateTime.UtcNow;
    }

    public void NotePortalClosed(IDdoGameDataProvider provider, IPortalInfo? lastPortalInfo)
    {
        var candidate = PickPortalQuest(lastPortalInfo);
        if (candidate == 0)
            candidate = ReadPortalSelectionQuest(provider, lastPortalInfo);

        if (candidate == 0)
            return;

        _portalEntryQuestDid = candidate;
        _portalEntryQuestSetUtc = DateTime.UtcNow;
    }

    public void ClearPortalEntryQuest()
    {
        _portalEntryQuestDid = null;
        _portalEntryQuestSetUtc = null;
    }

    public InstanceDiagnostics Probe(IDdoGameDataProvider provider)
    {
        var inTown = provider.InTown();
        var instanceQuestDid = ReadUInt(provider.GetInstanceQuestDid());
        var mapInfo = TryGetMapInfo(provider);
        var mapQuestDid = mapInfo?.CurrentQuestDid ?? 0;
        var areaDid = mapInfo?.AreaDid ?? 0;
        var mapIndoors = mapInfo?.IsIndoors == true;
        var objectiveDid = ReadUInt(provider.GetCurrentQuestObjectiveDid());
        var activeQuestProp = ReadCharacterQuestProperty(provider, DdoProperty.Dungeon_ActiveQuest);
        var playerActiveQuestProp = ReadCharacterQuestProperty(provider, DdoProperty.Dungeon_Player_ActiveQuest);
        var portalEntryQuestDid = ReadPortalEntryQuestDid();
        var hasDungeonDifficulty = HasCachedDungeonDifficulty(provider);

        var resolved = ResolveQuestDidInternal(
            provider,
            inTown,
            instanceQuestDid,
            mapQuestDid,
            activeQuestProp,
            playerActiveQuestProp,
            portalEntryQuestDid,
            objectiveDid,
            hasDungeonDifficulty,
            out var source);

        return new InstanceDiagnostics
        {
            InTown = inTown,
            InstanceQuestDid = instanceQuestDid,
            MapQuestDid = mapQuestDid,
            AreaDid = areaDid,
            MapIndoors = mapIndoors,
            ObjectiveDid = objectiveDid,
            ActiveQuestProp = activeQuestProp,
            PlayerActiveQuestProp = playerActiveQuestProp,
            PortalEntryQuestDid = portalEntryQuestDid,
            HasDungeonDifficulty = hasDungeonDifficulty,
            ResolvedQuestDid = resolved,
            ResolvedSource = source
        };
    }

    public uint ResolveQuestDid(IDdoGameDataProvider provider)
    {
        var diagnostics = Probe(provider);
        return diagnostics.ResolvedQuestDid;
    }

    public bool IsInTrackableArea(IDdoGameDataProvider provider)
    {
        var diagnostics = Probe(provider);
        return diagnostics.ResolvedQuestDid != 0 || diagnostics.LooksLikeInstanceArea;
    }

    private uint ResolveQuestDidInternal(
        IDdoGameDataProvider provider,
        bool? inTown,
        uint instanceQuestDid,
        uint mapQuestDid,
        uint activeQuestProp,
        uint playerActiveQuestProp,
        uint portalEntryQuestDid,
        uint objectiveDid,
        bool hasDungeonDifficulty,
        out string source)
    {
        source = "none";

        if (inTown == true)
            return 0;

        if (instanceQuestDid != 0 && IsQuestEntity(provider, instanceQuestDid))
        {
            source = "GetInstanceQuestDid";
            return instanceQuestDid;
        }

        if (activeQuestProp != 0 && IsQuestEntity(provider, activeQuestProp))
        {
            source = "Dungeon_ActiveQuest";
            return activeQuestProp;
        }

        if (playerActiveQuestProp != 0 && IsQuestEntity(provider, playerActiveQuestProp))
        {
            source = "Dungeon_Player_ActiveQuest";
            return playerActiveQuestProp;
        }

        if (portalEntryQuestDid != 0
            && IsQuestEntity(provider, portalEntryQuestDid)
            && LooksLikeInstanceArea(inTown, objectiveDid, hasDungeonDifficulty, mapQuestDid, portalEntryQuestDid))
        {
            source = "portal-entry";
            return portalEntryQuestDid;
        }

        if (mapQuestDid != 0
            && IsQuestEntity(provider, mapQuestDid)
            && LooksLikeInstanceArea(inTown, objectiveDid, hasDungeonDifficulty, mapQuestDid, portalEntryQuestDid))
        {
            source = "map.CurrentQuestDid";
            return mapQuestDid;
        }

        return 0;
    }

    private static bool LooksLikeInstanceArea(
        bool? inTown,
        uint objectiveDid,
        bool hasDungeonDifficulty,
        uint mapQuestDid,
        uint portalEntryQuestDid)
    {
        if (inTown == false)
            return true;

        if (objectiveDid != 0)
            return true;

        if (hasDungeonDifficulty)
            return true;

        if (portalEntryQuestDid != 0 && mapQuestDid == portalEntryQuestDid)
            return true;

        return false;
    }

    private uint ReadPortalEntryQuestDid()
    {
        if (_portalEntryQuestDid is not uint questDid || questDid == 0)
            return 0;

        if (_portalEntryQuestSetUtc == null)
            return 0;

        if ((DateTime.UtcNow - _portalEntryQuestSetUtc.Value).TotalSeconds > PortalEntryQuestLifetimeSeconds)
        {
            ClearPortalEntryQuest();
            return 0;
        }

        return questDid;
    }

    private static uint PickPortalQuest(IPortalInfo? portalInfo)
    {
        var quests = portalInfo?.AvailableQuests;
        if (quests == null || quests.Count == 0)
            return 0;

        if (quests.Count == 1)
            return quests[0];

        return 0;
    }

    private static uint ReadPortalSelectionQuest(IDdoGameDataProvider provider, IPortalInfo? portalInfo)
    {
        var selected = ReadUInt(provider.GetCurrentQuestDid());
        if (selected == 0)
            return 0;

        var quests = portalInfo?.AvailableQuests;
        if (quests == null || quests.Count == 0)
            return selected;

        return quests.Contains(selected) ? selected : 0;
    }

    private static uint ReadCharacterQuestProperty(IDdoGameDataProvider provider, DdoProperty propertyId)
    {
        try
        {
            var character = provider.GetCurrentCharacter();
            var properties = character?.PropertyCollection;
            if (properties == null)
                return 0;

            var value = properties.GetUInt32PropertyValue((uint)propertyId)
                ?? (uint?)properties.GetInt32PropertyValue((uint)propertyId);
            return value is > 0 ? value.Value : 0;
        }
        catch
        {
            return 0;
        }
    }

    private static bool HasCachedDungeonDifficulty(IDdoGameDataProvider provider)
    {
        try
        {
            var character = provider.GetCurrentCharacter();
            var properties = character?.PropertyCollection;
            if (properties == null)
                return false;

            foreach (var propertyId in new[]
                     {
                         (uint)DdoProperty.Dungeon_CachedDifficulty,
                         (uint)DdoProperty.Inventory_DungeonDifficulty,
                         (uint)DdoProperty.Dungeon_DifficultyLevel
                     })
            {
                var value = properties.GetUInt32PropertyValue(propertyId)
                    ?? (uint?)properties.GetInt32PropertyValue(propertyId);
                if (value is > 0 and not 0xFFFFFFFF)
                    return true;
            }
        }
        catch
        {
            // Ignore probe failures.
        }

        return false;
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

    private static bool IsQuestEntity(IDdoGameDataProvider provider, uint questDid)
    {
        try
        {
            var properties = provider.PropertyMaster.GetPropertyCollection(questDid);
            if (properties == null)
                return false;

            if (properties.GetStringInfoProperty((uint)DdoProperty.Quest_Name) != null)
                return true;

            if (properties.GetStringInfoProperty((uint)DdoProperty.Name) != null)
                return true;

            return ReadBoolProperty(properties, DdoProperty.Quest_IsAdventureArea) != null
                || ReadBoolProperty(properties, DdoProperty.Quest_IsDungeonQuest) != null;
        }
        catch
        {
            return false;
        }
    }

    private static bool? ReadBoolProperty(IPropertyCollection properties, DdoProperty propertyId)
    {
        var byteValue = properties.GetBytePropertyValue((uint)propertyId);
        if (byteValue.HasValue)
            return byteValue.Value != 0;

        var intValue = properties.GetInt32PropertyValue((uint)propertyId);
        if (intValue.HasValue)
            return intValue.Value != 0;

        return null;
    }

    private static uint ReadUInt(uint? value) => value is > 0 ? value.Value : 0;
}

public sealed class InstanceDiagnostics
{
    public bool? InTown { get; init; }
    public uint InstanceQuestDid { get; init; }
    public uint MapQuestDid { get; init; }
    public uint AreaDid { get; init; }
    public bool MapIndoors { get; init; }
    public uint ObjectiveDid { get; init; }
    public uint ActiveQuestProp { get; init; }
    public uint PlayerActiveQuestProp { get; init; }
    public uint PortalEntryQuestDid { get; init; }
    public bool HasDungeonDifficulty { get; init; }
    public uint ResolvedQuestDid { get; init; }
    public string ResolvedSource { get; init; } = "none";

    public bool LooksLikeInstanceArea =>
        InTown == false || ObjectiveDid != 0 || HasDungeonDifficulty || PortalEntryQuestDid != 0;

    public string FormatCompact()
    {
        var inTown = InTown switch
        {
            true => "town",
            false => "instance",
            _ => "?"
        };

        return
            $"in={inTown} inst=0x{InstanceQuestDid:X8} map=0x{MapQuestDid:X8} obj=0x{ObjectiveDid:X8} " +
            $"active=0x{ActiveQuestProp:X8} portal=0x{PortalEntryQuestDid:X8} " +
            $"resolved=0x{ResolvedQuestDid:X8} ({ResolvedSource})";
    }
}
