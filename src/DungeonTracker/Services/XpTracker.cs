using VoK.Sdk.Ddo;
using VoK.Sdk.Enums;

namespace DungeonTracker.Services;

public sealed class XpSnapshot
{
    public long Heroic { get; init; }
    public long Epic { get; init; }
    public long Legendary { get; init; }
    public long Reaper { get; init; }

    public long AdventureTotal => Heroic + Epic + Legendary;

    public long AwardTotal => AdventureTotal + Reaper;
}

public sealed class XpBreakdown
{
    public long Heroic { get; init; }
    public long Epic { get; init; }
    public long Legendary { get; init; }
    public long Reaper { get; init; }

    public long Total => Heroic + Epic + Legendary;

    public long AwardTotal => Total + Reaper;

    public double PerMinute(double durationSeconds)
    {
        if (durationSeconds <= 0 || Total <= 0)
            return 0;

        return Total / (durationSeconds / 60.0);
    }

    public static XpBreakdown FromDelta(XpSnapshot start, XpSnapshot end)
    {
        return new XpBreakdown
        {
            Heroic = Math.Max(0, end.Heroic - start.Heroic),
            Epic = Math.Max(0, end.Epic - start.Epic),
            Legendary = Math.Max(0, end.Legendary - start.Legendary),
            Reaper = Math.Max(0, end.Reaper - start.Reaper)
        };
    }
}

public static class XpTracker
{
    public static XpSnapshot Capture(IDdoGameDataProvider provider)
    {
        var properties = provider.GetCurrentCharacter()?.PropertyCollection;
        if (properties == null)
            return new XpSnapshot();

        return new XpSnapshot
        {
            Heroic = ReadInt64(properties, DdoProperty.Character_TotalXP),
            Epic = ReadInt64(properties, DdoProperty.Character_TotalEpicXP),
            Legendary = ReadInt64(properties, DdoProperty.Character_TotalLegendaryXP),
            Reaper = ReadInt64(properties, DdoProperty.Character_ReaperExperience)
        };
    }

    private static long ReadInt64(VoK.Sdk.Properties.IPropertyCollection properties, DdoProperty propertyId)
    {
        var value = properties.GetInt64PropertyValue((uint)propertyId);
        if (value.HasValue)
            return value.Value;

        var intValue = properties.GetInt32PropertyValue((uint)propertyId);
        return intValue ?? 0;
    }
}
