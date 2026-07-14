using VoK.Sdk;
using VoK.Sdk.Ddo;
using VoK.Sdk.Ddo.Enums;
using VoK.Sdk.Enums;
using VoK.Sdk.Properties;

namespace DungeonTracker.Services;

/// <summary>
/// Resolves dungeon difficulty from in-zone Volume/Server properties only
/// (SDK: Volume/Server is the same bag — only fetchable while inside the instance).
/// Game internals: Easy→Normal, Medium→Hard, Hard→Elite.
/// Portal sticky / session memory are intentionally unused.
/// </summary>
public static class QuestDifficultyResolver
{
    private static readonly Dictionary<uint, string> DifficultyPropertyMap = new()
    {
        [(uint)DdoProperty.Dungeon_CasualDifficulty] = "Casual",
        [(uint)DdoProperty.Dungeon_SoloDifficulty] = "Solo",
        [(uint)DdoProperty.Dungeon_EasyDifficulty] = "Normal",
        [(uint)DdoProperty.Dungeon_MediumDifficulty] = "Hard",
        [(uint)DdoProperty.Dungeon_HardDifficulty] = "Elite",
        [(uint)DdoProperty.Dungeon_ReaperDifficulty] = "Reaper",
        [(uint)DdoProperty.Dungeon_EpicCasualDifficulty] = "Epic Casual",
        [(uint)DdoProperty.Dungeon_EpicDifficulty] = "Epic Normal",
        [(uint)DdoProperty.Dungeon_EpicHardDifficulty] = "Epic Hard",
        [(uint)DdoProperty.Dungeon_EpicEliteDifficulty] = "Epic Elite",
        [(uint)DdoProperty.Dungeon_EpicReaperDifficulty] = "Epic Reaper"
    };

    /// <summary>
    /// Read live difficulty while inside an instance. Returns Unknown in town or when volume props are absent.
    /// Dungeon_CachedDifficulty alone is ignored when it reports Normal/below — that value commonly sticks
    /// from a prior run and is not a trustworthy Hard/Elite signal.
    /// </summary>
    public static string ResolveForInstance(
        IDdoGameDataProvider provider,
        int? baseQuestLevel = null)
    {
        try
        {
            if (provider.InTown() == true)
                return "Unknown";

            var character = provider.GetCurrentCharacter();

            // 1) Inventory enum (selection) — Server/Volume bag first, then in-zone instance bags.
            var inventory = ReadTrustedDifficulty(provider, character, (uint)DdoProperty.Inventory_DungeonDifficulty);
            if (!string.IsNullOrWhiteSpace(inventory))
                return AppendReaperLevel(character, inventory);

            // 2) UI difficulty name
            var ui = ReadTrustedDifficulty(provider, character, (uint)DdoProperty.Dungeon_UI_DifficultyName);
            if (!string.IsNullOrWhiteSpace(ui))
                return AppendReaperLevel(character, ui);

            // 3) Map area inventory/UI/cached only (no template DID scan — those falsely yield Epic Reaper).
            var area = ReadDifficultyFromArea(provider);
            if (!string.IsNullOrWhiteSpace(area))
                return AppendReaperLevel(character, area);

            // 4) Effective CR vs catalog base (Hard = +1, Elite = +2)
            var fromLevel = TryInferFromLevelOffset(provider, baseQuestLevel);
            if (!string.IsNullOrWhiteSpace(fromLevel))
                return AppendReaperLevel(character, fromLevel);

            // 5) CachedDifficulty only when it is above Normal (sticky Normal is noise).
            var cached = ReadTrustedDifficulty(provider, character, (uint)DdoProperty.Dungeon_CachedDifficulty);
            if (!string.IsNullOrWhiteSpace(cached))
                return AppendReaperLevel(character, cached);
        }
        catch
        {
            // Fall through.
        }

        return "Unknown";
    }

    public static string? TryInferFromLevelOffset(IDdoGameDataProvider provider, int? baseQuestLevel)
    {
        if (baseQuestLevel is not int baseLevel || baseLevel <= 0)
            return null;

        if (provider.InTown() == true)
            return null;

        var effective = TryReadInstanceEffectiveLevel(provider);
        if (effective is not int effectiveLevel)
            return null;

        return QuestLevelResolver.InferDifficultyFromLevelOffset(baseLevel, effectiveLevel);
    }

    public static int? TryReadInstanceEffectiveLevel(IDdoGameDataProvider provider)
    {
        try
        {
            if (provider.InTown() == true)
                return null;

            var serverProps = provider.GetServerProperties();
            if (serverProps is IPropertyCollection bag)
            {
                var asUint = bag.GetUInt32PropertyValue((uint)DdoProperty.Dungeon_DifficultyLevel);
                if (asUint is > 0 and < 200)
                    return (int)asUint.Value;

                var asInt = bag.GetInt32PropertyValue((uint)DdoProperty.Dungeon_DifficultyLevel);
                if (asInt is > 0 and < 200)
                    return asInt.Value;

                var property = bag.GetProperty((uint)DdoProperty.Dungeon_DifficultyLevel);
                var fromProp = property == null ? null : TryReadLevelValue(property);
                if (fromProp is > 0)
                    return fromProp;
            }

            foreach (var property in serverProps)
            {
                if (property.PropertyId != (uint)DdoProperty.Dungeon_DifficultyLevel)
                    continue;

                var level = TryReadLevelValue(property);
                if (level is > 0)
                    return level;
            }

            var character = provider.GetCurrentCharacter();
            foreach (var entityBag in EnumerateInZoneBags(character))
            {
                var asUint = entityBag.GetUInt32PropertyValue((uint)DdoProperty.Dungeon_DifficultyLevel);
                if (asUint is > 0 and < 200)
                    return (int)asUint.Value;

                var asInt = entityBag.GetInt32PropertyValue((uint)DdoProperty.Dungeon_DifficultyLevel);
                if (asInt is > 0 and < 200)
                    return asInt.Value;

                var property = entityBag.GetProperty((uint)DdoProperty.Dungeon_DifficultyLevel);
                var fromProp = property == null ? null : TryReadLevelValue(property);
                if (fromProp is > 0)
                    return fromProp;
            }
        }
        catch
        {
            // Ignore.
        }

        return null;
    }

    public static string DescribeLiveSources(IDdoGameDataProvider provider)
    {
        var inTown = provider.InTown();
        var parts = new List<string> { $"inTown={inTown?.ToString() ?? "?"}" };

        if (inTown == true)
            return string.Join(", ", parts) + ", (volume difficulty only available in-zone)";

        void Note(string name, string? value)
        {
            if (!string.IsNullOrWhiteSpace(value))
                parts.Add($"{name}={value}");
        }

        var character = provider.GetCurrentCharacter();
        var serverCount = 0;
        try { serverCount = provider.GetServerProperties()?.Count() ?? 0; } catch { /* ignore */ }
        parts.Add($"serverProps={serverCount}");

        Note("volume.inventory", ReadFromVolume(provider, (uint)DdoProperty.Inventory_DungeonDifficulty));
        Note("volume.uiName", ReadFromVolume(provider, (uint)DdoProperty.Dungeon_UI_DifficultyName));
        Note("volume.cached", ReadFromVolume(provider, (uint)DdoProperty.Dungeon_CachedDifficulty));
        Note("instance.inventory", ReadDifficulty(character?.InstanceProperties, (uint)DdoProperty.Inventory_DungeonDifficulty));
        Note("instance.uiName", ReadUiDifficultyLabel(character?.InstanceProperties));
        Note("instance.cached", ReadDifficulty(character?.InstanceProperties, (uint)DdoProperty.Dungeon_CachedDifficulty));
        Note("detailed.inventory", ReadDifficulty(character?.DetailedProperties, (uint)DdoProperty.Inventory_DungeonDifficulty));
        Note("detailed.cached", ReadDifficulty(character?.DetailedProperties, (uint)DdoProperty.Dungeon_CachedDifficulty));
        Note("weenie.inventory", ReadDifficulty(character?.WeeniePropertyCollection, (uint)DdoProperty.Inventory_DungeonDifficulty));
        Note("weenie.cached", ReadDifficulty(character?.WeeniePropertyCollection, (uint)DdoProperty.Dungeon_CachedDifficulty));
        Note("area", ReadDifficultyFromArea(provider));
        var level = TryReadInstanceEffectiveLevel(provider);
        if (level is > 0)
            parts.Add($"effectiveLevel={level}");

        // Always show whether sticky Normal cached was ignored.
        var rawCached = ReadDifficulty(character?.InstanceProperties, (uint)DdoProperty.Dungeon_CachedDifficulty)
            ?? ReadFromVolume(provider, (uint)DdoProperty.Dungeon_CachedDifficulty);
        if (!string.IsNullOrWhiteSpace(rawCached)
            && !IsTrustedCachedDifficulty(rawCached))
            parts.Add($"ignoredStickyCached={rawCached}");

        return parts.Count <= 2
            ? string.Join(", ", parts) + ", (no trusted volume difficulty props yet)"
            : string.Join(", ", parts);
    }

    public static string DescribeResolution(IDdoGameDataProvider provider, int? baseQuestLevel = null)
    {
        var parts = new List<string>
        {
            $"resolved={ResolveForInstance(provider, baseQuestLevel)}"
        };

        parts.Add(DescribeLiveSources(provider));

        if (baseQuestLevel is > 0)
            parts.Add($"baseLevel={baseQuestLevel}");

        var fromLevel = TryInferFromLevelOffset(provider, baseQuestLevel);
        if (!string.IsNullOrWhiteSpace(fromLevel))
            parts.Add($"fromLevel={fromLevel}");

        return string.Join(", ", parts);
    }

    public static string? MapPropertyId(uint propertyId)
    {
        if (DifficultyPropertyMap.TryGetValue(propertyId, out var mapped))
            return mapped;

        return null;
    }

    public static string FormatPropertyIdLabel(uint propertyId)
    {
        if (DifficultyPropertyMap.TryGetValue(propertyId, out var mapped))
        {
            var keyName = Enum.IsDefined(typeof(DdoProperty), propertyId)
                ? Enum.GetName(typeof(DdoProperty), propertyId)
                : null;
            return keyName != null ? $"{mapped} ({keyName})" : mapped;
        }

        return $"0x{propertyId:X8}";
    }

    public static string? TryFormatRawDifficultyValue(uint value) => FormatDifficultyValue(value);

    public static string? TryReadDifficultyFromProperty(IProperty? property)
    {
        if (property == null)
            return null;

        try
        {
            if (DifficultyPropertyMap.ContainsKey(property.PropertyId))
                return MapPropertyId(property.PropertyId);

            return FormatPropertyValue(property);
        }
        catch
        {
            return null;
        }
    }

    public static int GetDifficultyRank(string? difficulty)
    {
        if (string.IsNullOrWhiteSpace(difficulty) || QuestLevelResolver.IsUnknownDifficulty(difficulty))
            return -1;

        var normalized = difficulty.Trim();
        var skull = TryParseSkull(normalized);

        return normalized switch
        {
            _ when normalized.Contains("Legendary Reaper", StringComparison.OrdinalIgnoreCase) => 100 + (skull ?? 1),
            _ when normalized.Contains("Epic Reaper", StringComparison.OrdinalIgnoreCase) => 90 + (skull ?? 1),
            _ when normalized.Contains("Reaper", StringComparison.OrdinalIgnoreCase) => 50 + (skull ?? 1),
            _ when normalized.Contains("Legendary Elite", StringComparison.OrdinalIgnoreCase) => 85,
            _ when normalized.Contains("Legendary Hard", StringComparison.OrdinalIgnoreCase) => 84,
            _ when normalized.Contains("Legendary", StringComparison.OrdinalIgnoreCase)
                && normalized.Contains("Normal", StringComparison.OrdinalIgnoreCase) => 83,
            _ when normalized.Contains("Legendary Casual", StringComparison.OrdinalIgnoreCase) => 82,
            _ when normalized.Contains("Epic Elite", StringComparison.OrdinalIgnoreCase) => 75,
            _ when normalized.Contains("Epic Hard", StringComparison.OrdinalIgnoreCase) => 74,
            _ when normalized.Contains("Epic", StringComparison.OrdinalIgnoreCase)
                && normalized.Contains("Normal", StringComparison.OrdinalIgnoreCase) => 73,
            _ when normalized.Contains("Epic Casual", StringComparison.OrdinalIgnoreCase) => 72,
            _ when normalized.Equals("Elite", StringComparison.OrdinalIgnoreCase) => 40,
            _ when normalized.Equals("Hard", StringComparison.OrdinalIgnoreCase) => 30,
            _ when normalized.Equals("Normal", StringComparison.OrdinalIgnoreCase) => 20,
            _ when normalized.Equals("Casual", StringComparison.OrdinalIgnoreCase) => 10,
            _ when normalized.Equals("Solo", StringComparison.OrdinalIgnoreCase) => 5,
            _ => 0
        };
    }

    public static bool ShouldPreferDifficulty(string? current, string? candidate)
    {
        if (string.IsNullOrWhiteSpace(candidate) || QuestLevelResolver.IsUnknownDifficulty(candidate))
            return false;

        if (string.IsNullOrWhiteSpace(current) || QuestLevelResolver.IsUnknownDifficulty(current))
            return true;

        return GetDifficultyRank(candidate) > GetDifficultyRank(current);
    }

    public static bool ShouldReplaceDifficulty(string? current, string? candidate)
    {
        if (string.IsNullOrWhiteSpace(candidate) || QuestLevelResolver.IsUnknownDifficulty(candidate))
            return false;

        if (string.IsNullOrWhiteSpace(current) || QuestLevelResolver.IsUnknownDifficulty(current))
            return true;

        if (string.Equals(current, candidate, StringComparison.OrdinalIgnoreCase))
            return false;

        // Always allow volume/XP updates to overwrite — including Normal→Hard and Hard→Elite.
        return true;
    }

    public static bool IsRemakeMismatch(string? difficulty, string? questTier)
    {
        if (string.IsNullOrWhiteSpace(difficulty) || string.IsNullOrWhiteSpace(questTier))
            return false;

        if (!questTier.Equals("Heroic", StringComparison.OrdinalIgnoreCase))
            return false;

        return difficulty.Contains("Epic", StringComparison.OrdinalIgnoreCase)
            || difficulty.Contains("Legendary", StringComparison.OrdinalIgnoreCase);
    }

    public static string ClampToQuestTier(string? difficulty, string? questTier)
    {
        if (string.IsNullOrWhiteSpace(difficulty) || QuestLevelResolver.IsUnknownDifficulty(difficulty))
            return difficulty ?? "Unknown";

        if (string.IsNullOrWhiteSpace(questTier))
            return difficulty;

        // Live remake prefixes from volume props are trusted over a Heroic-only catalog guess.
        if (questTier.Equals("Heroic", StringComparison.OrdinalIgnoreCase)
            && (difficulty.Contains("Epic", StringComparison.OrdinalIgnoreCase)
                || difficulty.Contains("Legendary", StringComparison.OrdinalIgnoreCase)))
            return difficulty;

        return difficulty;
    }

    private static string? ReadDifficultyFromArea(IDdoGameDataProvider provider)
    {
        try
        {
            var areaDid = provider.GetMapInfo()?.AreaDid ?? 0;
            if (areaDid == 0)
                return null;

            foreach (var bag in new[]
                     {
                         provider.PropertyMaster?.GetPropertyCollection(areaDid),
                         provider.GetWeenieProperties(areaDid)
                     })
            {
                if (bag == null)
                    continue;

                var inventory = ReadDifficulty(bag, (uint)DdoProperty.Inventory_DungeonDifficulty);
                if (IsUsableDifficulty(inventory, (uint)DdoProperty.Inventory_DungeonDifficulty))
                    return inventory;

                var ui = ReadUiDifficultyLabel(bag);
                if (IsUsableDifficulty(ui, (uint)DdoProperty.Dungeon_UI_DifficultyName))
                    return ui;

                var cached = ReadDifficulty(bag, (uint)DdoProperty.Dungeon_CachedDifficulty);
                if (IsUsableDifficulty(cached, (uint)DdoProperty.Dungeon_CachedDifficulty))
                    return cached;
            }
        }
        catch
        {
            // Ignore.
        }

        return null;
    }

    private static string? ReadTrustedDifficulty(
        IDdoGameDataProvider provider,
        IEntity? character,
        uint propertyId)
    {
        var fromVolume = ReadFromVolume(provider, propertyId);
        if (IsUsableDifficulty(fromVolume, propertyId))
            return fromVolume;

        foreach (var bag in EnumerateInZoneBags(character))
        {
            var value = propertyId == (uint)DdoProperty.Dungeon_UI_DifficultyName
                ? ReadUiDifficultyLabel(bag)
                : ReadDifficulty(bag, propertyId);

            if (IsUsableDifficulty(value, propertyId))
                return value;
        }

        return null;
    }

    private static IEnumerable<IPropertyCollection> EnumerateInZoneBags(IEntity? character)
    {
        // Match QuestTracker Helpers: avoid PropertyCollection (merged Instance+Weenie, slow/redundant).
        if (character?.InstanceProperties != null)
            yield return character.InstanceProperties;
        if (character?.DetailedProperties != null)
            yield return character.DetailedProperties;
        if (character?.WeeniePropertyCollection != null)
            yield return character.WeeniePropertyCollection;
    }

    private static bool IsUsableDifficulty(string? difficulty, uint propertyId)
    {
        if (string.IsNullOrWhiteSpace(difficulty) || QuestLevelResolver.IsUnknownDifficulty(difficulty))
            return false;

        if (propertyId == (uint)DdoProperty.Dungeon_CachedDifficulty)
            return IsTrustedCachedDifficulty(difficulty);

        return true;
    }

    private static bool IsTrustedCachedDifficulty(string? difficulty)
    {
        // Sticky CachedDifficulty=Normal is the common false reading for Hard/Elite runs.
        return GetDifficultyRank(difficulty) > GetDifficultyRank("Normal");
    }

    private static int? TryReadLevelValue(IProperty property)
    {
        try
        {
            if (property is IInt32Property intProperty && intProperty.Int32Value is int signed && signed is > 0 and < 200)
                return signed;

            if (property is IUInt32Property uintProperty && uintProperty.UInt32Value is uint unsigned && unsigned is > 0 and < 200)
                return (int)unsigned;

            if (property.RawUInt32 is uint raw && raw is > 0 and < 200)
                return (int)raw;
        }
        catch
        {
            // Ignore.
        }

        return null;
    }

    private static string? ReadFromVolume(IDdoGameDataProvider provider, uint propertyId)
    {
        try
        {
            var serverProps = provider.GetServerProperties();

            // Prefer keyed bag accessors — PropertyId on enumerated items can be unreliable.
            if (serverProps is IPropertyCollection bag)
            {
                var fromBag = propertyId == (uint)DdoProperty.Dungeon_UI_DifficultyName
                    ? ReadUiDifficultyLabel(bag)
                    : ReadDifficulty(bag, propertyId);
                if (!string.IsNullOrWhiteSpace(fromBag))
                    return fromBag;
            }

            foreach (var property in serverProps)
            {
                if (property.PropertyId != propertyId)
                    continue;

                var mapped = FormatPropertyValue(property)
                    ?? PortalDifficultyReader.TryReadDifficultyLabel(property);
                if (string.IsNullOrWhiteSpace(mapped) && property.RawUInt32 is uint raw and not 0)
                {
                    mapped = MapPropertyId(raw) ?? FormatDifficultyValue(raw);
                }

                if (!string.IsNullOrWhiteSpace(mapped) && !QuestLevelResolver.IsUnknownDifficulty(mapped))
                    return mapped;
            }
        }
        catch
        {
            // Ignore.
        }

        return null;
    }

    /// <summary>
    /// One-shot diagnostic: which difficulty-related props exist in the server/volume bag.
    /// </summary>
    public static string ScanServerDifficultyProps(IDdoGameDataProvider provider)
    {
        var hits = new List<string>();
        try
        {
            var serverProps = provider.GetServerProperties();
            if (serverProps is IPropertyCollection bag && bag.Properties != null)
            {
                foreach (var kvp in bag.Properties)
                {
                    var property = kvp.Value;
                    var key = kvp.Key;
                    var id = property?.PropertyId ?? 0;
                    if (!IsDifficultyRelatedPropertyId(key) && !IsDifficultyRelatedPropertyId(id)
                        && MapPropertyId(property?.RawUInt32 ?? 0) == null
                        && (property == null || FormatPropertyValue(property) == null))
                        continue;

                    var label = property == null
                        ? "?"
                        : FormatPropertyValue(property)
                            ?? PortalDifficultyReader.TryReadDifficultyLabel(property)
                            ?? MapPropertyId(property.RawUInt32)
                            ?? (property.RawUInt32 != 0 ? $"0x{property.RawUInt32:X8}" : property.Value?.ToString())
                            ?? "?";
                    hits.Add($"key=0x{key:X8}/id=0x{id:X8}:{label}");
                }
            }
            else
            {
                foreach (var property in serverProps)
                {
                    var valueMapped = MapPropertyId(property.RawUInt32) ?? FormatPropertyValue(property);
                    if (!IsDifficultyRelatedPropertyId(property.PropertyId) && valueMapped == null)
                        continue;

                    var label = valueMapped
                        ?? PortalDifficultyReader.TryReadDifficultyLabel(property)
                        ?? (property.RawUInt32 != 0 ? $"0x{property.RawUInt32:X8}" : property.Value?.ToString())
                        ?? "?";
                    hits.Add($"id=0x{property.PropertyId:X8}:{label}");
                }
            }
        }
        catch (Exception ex)
        {
            return $"scan-failed:{ex.GetType().Name}";
        }

        return hits.Count == 0 ? "(none)" : string.Join("; ", hits.Take(30));
    }

    private static bool IsDifficultyRelatedPropertyId(uint propertyId)
    {
        if (propertyId == 0)
            return false;

        if (DifficultyPropertyMap.ContainsKey(propertyId))
            return true;

        if (propertyId == (uint)DdoProperty.Inventory_DungeonDifficulty
            || propertyId == (uint)DdoProperty.Dungeon_UI_DifficultyName
            || propertyId == (uint)DdoProperty.Dungeon_CachedDifficulty
            || propertyId == (uint)DdoProperty.Dungeon_DifficultyLevel
            || propertyId == (uint)DdoProperty.Dungeon_CachedReaperLevel
            || propertyId == (uint)DdoProperty.QuestSelector_QuestDifficulty
            || propertyId == (uint)DdoProperty.Quest_DifficultyType
            || propertyId == (uint)DdoProperty.Quest_GRS_Difficulty)
            return true;

        if (!Enum.IsDefined(typeof(DdoProperty), propertyId))
            return false;

        var name = Enum.GetName(typeof(DdoProperty), propertyId);
        return name != null
            && (name.Contains("Dungeon", StringComparison.OrdinalIgnoreCase)
                && name.Contains("Difficulty", StringComparison.OrdinalIgnoreCase)
                || name.Contains("Quest_CR_", StringComparison.OrdinalIgnoreCase));
    }

    private static string AppendReaperLevel(IEntity? character, string difficulty)
    {
        if (string.IsNullOrWhiteSpace(difficulty) || QuestLevelResolver.IsUnknownDifficulty(difficulty))
            return difficulty;

        if (!difficulty.Contains("Reaper", StringComparison.OrdinalIgnoreCase))
            return difficulty;

        var reaperLevel = TryReadReaperLevel(character);
        if (reaperLevel is not (>= 1 and <= 10))
            return difficulty;

        if (difficulty.StartsWith("Epic", StringComparison.OrdinalIgnoreCase))
            return $"Epic Reaper {reaperLevel.Value}";

        if (difficulty.StartsWith("Legendary", StringComparison.OrdinalIgnoreCase))
            return $"Legendary Reaper {reaperLevel.Value}";

        return $"Reaper {reaperLevel.Value}";
    }

    private static int? TryReadReaperLevel(IEntity? character)
    {
        foreach (var bag in new[] { character?.InstanceProperties, character?.PropertyCollection })
        {
            var level = bag?.GetInt32PropertyValue((uint)DdoProperty.Dungeon_CachedReaperLevel);
            if (level is >= 1 and <= 10)
                return level.Value;
        }

        return null;
    }

    private static int? TryParseSkull(string normalized)
    {
        if (normalized.StartsWith("Epic Reaper ", StringComparison.OrdinalIgnoreCase)
            && int.TryParse(normalized["Epic Reaper ".Length..], out var epic)
            && epic is >= 1 and <= 10)
            return epic;

        if (normalized.StartsWith("Legendary Reaper ", StringComparison.OrdinalIgnoreCase)
            && int.TryParse(normalized["Legendary Reaper ".Length..], out var legendary)
            && legendary is >= 1 and <= 10)
            return legendary;

        if (normalized.StartsWith("Reaper ", StringComparison.OrdinalIgnoreCase)
            && int.TryParse(normalized["Reaper ".Length..], out var heroic)
            && heroic is >= 1 and <= 10)
            return heroic;

        return null;
    }

    private static string? ReadUiDifficultyLabel(IPropertyCollection? properties)
    {
        if (properties == null)
            return null;

        try
        {
            var asDifficulty = ReadDifficulty(properties, (uint)DdoProperty.Dungeon_UI_DifficultyName);
            if (!string.IsNullOrWhiteSpace(asDifficulty))
                return asDifficulty;

            var propertyId = properties.GetUInt32PropertyValue((uint)DdoProperty.Dungeon_UI_DifficultyName);
            if (propertyId is uint id && id != 0)
            {
                var mapped = MapPropertyId(id);
                if (!string.IsNullOrWhiteSpace(mapped))
                    return mapped;
            }
        }
        catch
        {
            // Ignore.
        }

        return null;
    }

    private static string? ReadDifficulty(IPropertyCollection? properties, uint propertyId)
    {
        if (properties == null)
            return null;

        try
        {
            var property = properties.GetProperty(propertyId);
            if (property != null)
            {
                var formatted = FormatPropertyValue(property);
                if (!string.IsNullOrWhiteSpace(formatted))
                    return formatted;
            }

            var enumProperty = properties.GetEnumProperty(propertyId);
            if (enumProperty?.Value is DifficultyType difficultyType)
                return FormatEnumName(difficultyType.ToString());

            if (enumProperty?.Value != null)
            {
                var text = enumProperty.Value.ToString();
                if (!string.IsNullOrWhiteSpace(text) && !text.All(char.IsDigit))
                    return FormatEnumName(text);

                return FormatDifficultyValue(Convert.ToUInt32(enumProperty.Value));
            }

            var rawValue = properties.GetUInt32PropertyValue(propertyId);
            if (rawValue.HasValue)
                return FormatDifficultyValue(rawValue.Value);
        }
        catch
        {
            // Ignore.
        }

        return null;
    }

    private static string? FormatPropertyValue(IProperty property)
    {
        try
        {
            if (property.Value is DifficultyType difficultyType)
                return FormatEnumName(difficultyType.ToString());

            if (property is IEnumProperty enumProperty && enumProperty.Value != null)
            {
                if (enumProperty.Value is DifficultyType dt)
                    return FormatEnumName(dt.ToString());

                var text = enumProperty.Value.ToString();
                if (!string.IsNullOrWhiteSpace(text) && !text.All(char.IsDigit))
                    return FormatEnumName(text);

                return FormatDifficultyValue(Convert.ToUInt32(enumProperty.Value));
            }

            if (property.RawUInt32 is uint raw and not 0)
            {
                var mapped = FormatDifficultyValue(raw);
                if (!string.IsNullOrWhiteSpace(mapped))
                    return mapped;
            }

            if (property is IUInt32Property uintProperty && uintProperty.UInt32Value is uint uintValue)
                return FormatDifficultyValue(uintValue);
        }
        catch
        {
            // Ignore.
        }

        return null;
    }

    private static string? FormatDifficultyValue(uint value)
    {
        if (value == 0 || value == (uint)DifficultyType.Invalid || value == 0xFFFFFFFF)
            return null;

        if (DifficultyPropertyMap.TryGetValue(value, out var mapped))
            return mapped;

        if (value is >= 1 and <= 12 && Enum.IsDefined(typeof(DifficultyType), value))
        {
            var name = Enum.GetName(typeof(DifficultyType), value);
            if (!string.IsNullOrWhiteSpace(name))
                return FormatEnumName(name);
        }

        return null;
    }

    private static string FormatEnumName(string enumName) => enumName switch
    {
        // Internal dungeon names sometimes appear as Easy/Medium; player-facing is Normal/Hard/Elite.
        "Easy" => "Normal",
        "Medium" => "Hard",
        "Normal" => "Normal",
        "Hard" => "Hard",
        "Elite" => "Elite",
        "EliteFinished" => "Elite",
        "Casual" => "Casual",
        "Solo" => "Solo",
        "Reaper" => "Reaper",
        "Epic" => "Epic Normal",
        "Epic_Casual" => "Epic Casual",
        "Epic_Hard" => "Epic Hard",
        "Epic_Elite" => "Epic Elite",
        "Epic_Reaper" => "Epic Reaper",
        "Legendary" => "Legendary Normal",
        "Legendary_Casual" => "Legendary Casual",
        "Legendary_Hard" => "Legendary Hard",
        "Legendary_Elite" => "Legendary Elite",
        "Legendary_Reaper" => "Legendary Reaper",
        _ => enumName.Replace('_', ' ')
    };
}
