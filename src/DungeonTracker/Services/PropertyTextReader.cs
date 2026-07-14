using VoK.Sdk.Ddo;
using VoK.Sdk.Properties;

namespace DungeonTracker.Services;

public static class PropertyTextReader
{
    public static string Read(IDdoGameDataProvider provider, IPropertyCollection? properties, uint propertyId)
    {
        if (properties == null)
            return string.Empty;

        try
        {
            var stringProperty = properties.GetStringInfoProperty(propertyId);
            var value = stringProperty?.GetText(provider.PropertyMaster, null, properties);
            if (!string.IsNullOrWhiteSpace(value))
                return value.Trim();
        }
        catch
        {
            // Ignore string-info read failures.
        }

        return string.Empty;
    }

    public static string Read(
        IDdoGameDataProvider provider,
        IProperty? property,
        IPropertyCollection? context = null)
    {
        if (property == null)
            return string.Empty;

        try
        {
            if (property is IStringInfoProperty stringInfo)
            {
                var value = stringInfo.GetText(provider.PropertyMaster, null, context);
                if (!string.IsNullOrWhiteSpace(value))
                    return value.Trim();
            }

            var raw = property.Value?.ToString();
            if (!string.IsNullOrWhiteSpace(raw))
                return raw.Trim();
        }
        catch
        {
            // Ignore read failures.
        }

        return string.Empty;
    }

    public static string ReadFromCollection(
        IDdoGameDataProvider provider,
        IEnumerable<IProperty>? properties,
        uint propertyId)
    {
        if (properties == null)
            return string.Empty;

        foreach (var property in properties)
        {
            if (property.PropertyId != propertyId)
                continue;

            var value = Read(provider, property);
            if (!string.IsNullOrWhiteSpace(value))
                return value;
        }

        return string.Empty;
    }
}
