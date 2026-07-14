using VoK.Sdk.Ddo;
using VoK.Sdk.Enums;
using VoK.Sdk.Properties;

namespace DungeonTracker.Services;

public static class InstanceSummaryReader
{
    public static InstanceSummarySnapshot Read(IDdoGameDataProvider provider)
    {
        var character = provider.GetCurrentCharacter()?.PropertyCollection;
        var title = ReadString(provider, character, DdoProperty.Dungeon_Summary_Title);
        var text = ReadString(provider, character, DdoProperty.Dungeon_Summary_Text);

        if (string.IsNullOrWhiteSpace(title) && string.IsNullOrWhiteSpace(text))
        {
            foreach (var property in provider.GetServerProperties())
            {
                if (property.PropertyId == (uint)DdoProperty.Dungeon_Summary_Title && string.IsNullOrWhiteSpace(title))
                    title = ReadStringFromProperty(provider, property);
                if (property.PropertyId == (uint)DdoProperty.Dungeon_Summary_Text && string.IsNullOrWhiteSpace(text))
                    text = ReadStringFromProperty(provider, property);
            }
        }

        return new InstanceSummarySnapshot
        {
            Title = title,
            Text = text,
            LooksComplete = LooksLikeCompletionSummary(title, text)
        };
    }

    public static bool LooksLikeCompletionSummary(string? title, string? text)
    {
        var combined = $"{title}\n{text}";
        if (string.IsNullOrWhiteSpace(combined))
            return false;

        return combined.Contains("XP awarded", StringComparison.OrdinalIgnoreCase)
            || combined.Contains("Bonuses locked in", StringComparison.OrdinalIgnoreCase)
            || combined.Contains("Completed.", StringComparison.OrdinalIgnoreCase)
            || combined.Contains("adventure is complete", StringComparison.OrdinalIgnoreCase);
    }

    private static string ReadString(
        IDdoGameDataProvider provider,
        IPropertyCollection? properties,
        DdoProperty propertyId)
    {
        if (properties == null)
            return string.Empty;

        try
        {
            var stringProperty = properties.GetStringInfoProperty((uint)propertyId);
            var value = stringProperty?.GetText(provider.PropertyMaster, null, properties);
            return string.IsNullOrWhiteSpace(value) ? string.Empty : value.Trim();
        }
        catch
        {
            return string.Empty;
        }
    }

    private static string ReadStringFromProperty(IDdoGameDataProvider provider, IProperty property)
    {
        try
        {
            if (property is IStringInfoProperty stringInfo)
            {
                var value = stringInfo.GetText(provider.PropertyMaster, null, null);
                return string.IsNullOrWhiteSpace(value) ? string.Empty : value.Trim();
            }

            return property.Value?.ToString()?.Trim() ?? string.Empty;
        }
        catch
        {
            return string.Empty;
        }
    }
}

public sealed class InstanceSummarySnapshot
{
    public string Title { get; init; } = string.Empty;
    public string Text { get; init; } = string.Empty;
    public bool LooksComplete { get; init; }
}
