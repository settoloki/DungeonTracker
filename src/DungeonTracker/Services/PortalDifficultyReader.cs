using VoK.Sdk.Ddo;
using VoK.Sdk.Ddo.Enums;
using VoK.Sdk.Enums;
using VoK.Sdk.Properties;

namespace DungeonTracker.Services;

public static class PortalDifficultyReader
{
    public static uint? TryReadPropertyId(IPortalInfo? portalInfo) =>
        TryReadPropertyId(portalInfo?.QuestGenericDifficulty);

    public static string? TryReadDifficultyLabel(IPortalInfo? portalInfo) =>
        TryReadDifficultyLabel(portalInfo?.QuestGenericDifficulty);

    public static string? TryReadDifficultyLabel(object? rawValue)
    {
        if (rawValue == null)
            return null;

        try
        {
            if (rawValue is IEnumProperty enumProperty && enumProperty.Value != null)
            {
                var enumLabel = QuestDifficultyResolver.TryFormatRawDifficultyValue(
                    Convert.ToUInt32(enumProperty.Value));
                if (!string.IsNullOrWhiteSpace(enumLabel))
                    return enumLabel;
            }

            if (rawValue is IUInt32Property uintProperty && uintProperty.UInt32Value is uint rawUInt)
            {
                var mapped = QuestDifficultyResolver.MapPropertyId(rawUInt)
                    ?? QuestDifficultyResolver.TryFormatRawDifficultyValue(rawUInt);
                if (!string.IsNullOrWhiteSpace(mapped))
                    return mapped;
            }

            if (rawValue is Enum enumValue)
            {
                var enumLabel = QuestDifficultyResolver.TryFormatRawDifficultyValue(Convert.ToUInt32(enumValue));
                if (!string.IsNullOrWhiteSpace(enumLabel))
                    return enumLabel;
            }

            var propertyId = TryReadPropertyId(rawValue);
            if (propertyId is uint id)
            {
                return QuestDifficultyResolver.MapPropertyId(id)
                    ?? QuestDifficultyResolver.TryFormatRawDifficultyValue(id);
            }
        }
        catch
        {
            // Fall through to null.
        }

        return null;
    }

    public static uint? TryReadPropertyId(IProperty? property) =>
        TryReadPropertyId((object?)property);

    public static uint? TryReadPropertyId(object? rawValue)
    {
        if (rawValue == null)
            return null;

        try
        {
            switch (rawValue)
            {
                case uint uintValue when uintValue != 0:
                    return uintValue;
                case int intValue when intValue > 0:
                    return (uint)intValue;
                case long longValue when longValue > 0 && longValue <= uint.MaxValue:
                    return (uint)longValue;
                case DdoProperty property when (uint)property != 0:
                    return (uint)property;
                case IEnumProperty enumProperty when enumProperty.Value != null:
                {
                    var enumValue = Convert.ToUInt32(enumProperty.Value);
                    if (enumValue == 0)
                        return null;

                    var mapped = QuestDifficultyResolver.MapPropertyId(enumValue);
                    if (mapped != null)
                        return enumValue;

                    if (Enum.IsDefined(typeof(DdoProperty), enumValue))
                        return enumValue;

                    return MapDifficultyTypeToPropertyId(enumProperty.Value);
                }
                case IUInt32Property uintProperty when uintProperty.UInt32Value is uint rawUInt && rawUInt != 0:
                    return rawUInt;
                case IProperty property when property.PropertyId != 0:
                    return property.PropertyId;
                case Enum enumValue when Convert.ToUInt32(enumValue) != 0:
                {
                    var numeric = Convert.ToUInt32(enumValue);
                    return QuestDifficultyResolver.MapPropertyId(numeric) != null
                        ? numeric
                        : MapDifficultyTypeToPropertyId(enumValue);
                }
            }

            if (rawValue is string text)
                return TryParsePropertyId(text);

            return TryParsePropertyId(rawValue.ToString());
        }
        catch
        {
            return null;
        }
    }

    private static uint? MapDifficultyTypeToPropertyId(object enumValue)
    {
        if (enumValue is not DifficultyType difficultyType || difficultyType == DifficultyType.Invalid)
            return null;

        return difficultyType switch
        {
            DifficultyType.Casual => (uint)DdoProperty.Dungeon_CasualDifficulty,
            DifficultyType.Solo => (uint)DdoProperty.Dungeon_SoloDifficulty,
            DifficultyType.Normal => (uint)DdoProperty.Dungeon_EasyDifficulty,
            DifficultyType.Hard => (uint)DdoProperty.Dungeon_MediumDifficulty,
            DifficultyType.Elite or DifficultyType.EliteFinished => (uint)DdoProperty.Dungeon_HardDifficulty,
            DifficultyType.Reaper => (uint)DdoProperty.Dungeon_ReaperDifficulty,
            DifficultyType.Epic => (uint)DdoProperty.Dungeon_EpicDifficulty,
            DifficultyType.Epic_Casual => (uint)DdoProperty.Dungeon_EpicCasualDifficulty,
            DifficultyType.Epic_Hard => (uint)DdoProperty.Dungeon_EpicHardDifficulty,
            DifficultyType.Epic_Elite => (uint)DdoProperty.Dungeon_EpicEliteDifficulty,
            DifficultyType.Epic_Reaper => (uint)DdoProperty.Dungeon_EpicReaperDifficulty,
            _ => null
        };
    }

    public static string Describe(object? rawValue)
    {
        if (rawValue == null)
            return "—";

        var propertyId = TryReadPropertyId(rawValue);
        if (propertyId is uint id)
        {
            var label = QuestDifficultyResolver.MapPropertyId(id);
            return string.IsNullOrWhiteSpace(label)
                ? $"0x{id:X8} ({rawValue.GetType().Name})"
                : $"{label} (0x{id:X8})";
        }

        return $"{rawValue} ({rawValue.GetType().Name})";
    }

    private static uint? TryParsePropertyId(string? text)
    {
        if (string.IsNullOrWhiteSpace(text))
            return null;

        var trimmed = text.Trim();
        if (trimmed.StartsWith("0x", StringComparison.OrdinalIgnoreCase)
            && uint.TryParse(trimmed[2..], System.Globalization.NumberStyles.HexNumber, null, out var hexValue)
            && hexValue != 0)
            return hexValue;

        if (uint.TryParse(trimmed, out var decimalValue) && decimalValue != 0)
            return decimalValue;

        if (Enum.TryParse(typeof(DdoProperty), trimmed, ignoreCase: true, out var property)
            && property != null
            && (uint)(DdoProperty)property != 0)
            return (uint)(DdoProperty)property;

        return null;
    }
}
