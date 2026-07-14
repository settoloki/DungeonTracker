using VoK.Sdk.Ddo;
using VoK.Sdk.Enums;
using VoK.Sdk.Plugins;
using VoK.Sdk.Properties;
using DungeonTracker.Models;

namespace DungeonTracker.Services;

/// <summary>
/// Discovers DDO characters on the logged-in account from the character select roster,
/// the currently played toon, and previously tracked local data folders.
/// </summary>
public static class GameCharacterDiscovery
{
    private static readonly (string ClassName, DdoProperty Property)[] ClassLevelProperties =
    [
        ("Alchemist", DdoProperty.Character_AlchemistLevel),
        ("Artificer", DdoProperty.Character_ArtificerLevel),
        ("Barbarian", DdoProperty.Character_BarbarianLevel),
        ("Bard", DdoProperty.Character_BardLevel),
        ("Cleric", DdoProperty.Character_ClericLevel),
        ("Druid", DdoProperty.Character_DruidLevel),
        ("Favored Soul", DdoProperty.Character_FavoredSoulLevel),
        ("Fighter", DdoProperty.Character_FighterLevel),
        ("Monk", DdoProperty.Character_MonkLevel),
        ("Paladin", DdoProperty.Character_PaladinLevel),
        ("Ranger", DdoProperty.Character_RangerLevel),
        ("Rogue", DdoProperty.Character_RogueLevel),
        ("Sorcerer", DdoProperty.Character_SorcererLevel),
        ("Warlock", DdoProperty.Character_WarlockLevel),
        ("Wizard", DdoProperty.Character_WizardLevel)
    ];

    public static IReadOnlyList<GameAccountCharacter> Discover(
        IDdoGameDataProvider provider,
        IPlugin? plugin = null,
        string? pluginFolder = null)
    {
        var byName = new Dictionary<string, GameAccountCharacter>(StringComparer.OrdinalIgnoreCase);

        TryAddFromSdkRoster(provider, byName);
        TryAddCurrentCharacter(provider, byName);
        TryAddFromLocalFolders(provider, plugin, pluginFolder, byName);

        return byName.Values
            .OrderBy(c => c.Name, StringComparer.OrdinalIgnoreCase)
            .ToList();
    }

    public static void SplitDisplayName(string fullName, out string givenName, out string? surname)
    {
        var trimmed = fullName.Trim();
        var space = trimmed.IndexOf(' ');
        if (space <= 0 || space >= trimmed.Length - 1)
        {
            givenName = trimmed;
            surname = null;
            return;
        }

        givenName = trimmed[..space].Trim();
        surname = trimmed[(space + 1)..].Trim();
        if (string.IsNullOrWhiteSpace(surname))
            surname = null;
    }

    private static void TryAddFromSdkRoster(
        IDdoGameDataProvider provider,
        IDictionary<string, GameAccountCharacter> byName)
    {
        try
        {
            var roster = provider.GetAllCharacters();
            if (roster == null)
                return;

            var server = provider.GetServerName()?.Trim();
            foreach (var character in roster)
            {
                if (character == null || string.IsNullOrWhiteSpace(character.Name))
                    continue;

                Upsert(byName, BuildFromProperties(
                    character.Name.Trim(),
                    character.Properties,
                    character.Id == 0 ? null : character.Id,
                    server));
            }
        }
        catch
        {
            // Roster is only available after visiting character select; ignore.
        }
    }

    private static void TryAddCurrentCharacter(
        IDdoGameDataProvider provider,
        IDictionary<string, GameAccountCharacter> byName)
    {
        var name = provider.GetCurrentCharacterName()?.Trim();
        if (string.IsNullOrWhiteSpace(name))
            return;

        IPropertyCollection? properties = null;
        try
        {
            properties = provider.GetCurrentCharacter()?.PropertyCollection;
        }
        catch
        {
            // Ignore.
        }

        Upsert(byName, BuildFromProperties(
            name,
            properties,
            provider.GetCurrentCharacterId() is ulong id and > 0 ? id : null,
            provider.GetServerName()?.Trim()));
    }

    private static void TryAddFromLocalFolders(
        IDdoGameDataProvider provider,
        IPlugin? plugin,
        string? pluginFolder,
        IDictionary<string, GameAccountCharacter> byName)
    {
        if (string.IsNullOrWhiteSpace(pluginFolder) || !Directory.Exists(pluginFolder))
            return;

        try
        {
            var server = provider.GetServerName()?.Trim();
            var accountHash = plugin != null ? provider.GetSubscriptionKeyHash(plugin)?.Trim() : null;
            if (string.IsNullOrWhiteSpace(server) || string.IsNullOrWhiteSpace(accountHash))
                return;

            var accountFolder = Path.Combine(
                pluginFolder,
                CharacterDataPathResolver.SanitizePathSegment(server),
                CharacterDataPathResolver.SanitizePathSegment(accountHash));

            if (!Directory.Exists(accountFolder))
                return;

            foreach (var dir in Directory.EnumerateDirectories(accountFolder))
            {
                var name = Path.GetFileName(dir)?.Trim();
                if (string.IsNullOrWhiteSpace(name))
                    continue;

                if (name.Length >= 40 && name.All(static c =>
                        c is (>= '0' and <= '9') or (>= 'a' and <= 'f') or (>= 'A' and <= 'F')))
                    continue;

                SplitDisplayName(name, out var given, out var surname);
                Upsert(byName, new GameAccountCharacter
                {
                    Name = name,
                    GivenName = given,
                    Surname = surname,
                    Server = server
                });
            }
        }
        catch
        {
            // Local scan is best-effort.
        }
    }

    private static GameAccountCharacter BuildFromProperties(
        string name,
        IPropertyCollection? properties,
        ulong? gameCharacterId,
        string? server)
    {
        SplitDisplayName(name, out var given, out var surname);

        var classes = ReadClasses(properties);
        var totalLevel = ReadInt(properties, DdoProperty.Character_TotalLevel);
        var epicLevels = ReadInt(properties, DdoProperty.Character_EpicLevel)
            ?? ReadInt(properties, DdoProperty.Character_EpicAndLegendaryLevel);
        var gender = FormatGender(ReadInt(properties, DdoProperty.Character_Gender));

        return new GameAccountCharacter
        {
            Name = name,
            GivenName = given,
            Surname = surname,
            Server = server,
            Gender = gender,
            Level = totalLevel,
            EpicLevels = epicLevels,
            GameCharacterId = gameCharacterId,
            Classes = classes
        };
    }

    private static IReadOnlyList<DdoTrackerClassLevel> ReadClasses(IPropertyCollection? properties)
    {
        if (properties == null)
            return Array.Empty<DdoTrackerClassLevel>();

        var classes = new List<DdoTrackerClassLevel>();
        foreach (var (className, property) in ClassLevelProperties)
        {
            var levels = ReadInt(properties, property);
            if (levels is >= 1 and <= 20)
                classes.Add(new DdoTrackerClassLevel { Name = className, Levels = levels.Value });
        }

        return classes
            .OrderByDescending(c => c.Levels)
            .ThenBy(c => c.Name, StringComparer.OrdinalIgnoreCase)
            .Take(3)
            .ToList();
    }

    private static void Upsert(
        IDictionary<string, GameAccountCharacter> byName,
        GameAccountCharacter candidate)
    {
        if (!byName.TryGetValue(candidate.Name, out var existing))
        {
            byName[candidate.Name] = candidate;
            return;
        }

        byName[candidate.Name] = new GameAccountCharacter
        {
            Name = existing.Name,
            GivenName = Prefer(candidate.GivenName, existing.GivenName) ?? existing.Name,
            Surname = Prefer(candidate.Surname, existing.Surname),
            Server = Prefer(candidate.Server, existing.Server),
            Race = Prefer(candidate.Race, existing.Race),
            Gender = Prefer(candidate.Gender, existing.Gender),
            Alignment = Prefer(candidate.Alignment, existing.Alignment),
            Guild = Prefer(candidate.Guild, existing.Guild),
            Level = candidate.Level ?? existing.Level,
            EpicLevels = candidate.EpicLevels ?? existing.EpicLevels,
            GameCharacterId = candidate.GameCharacterId ?? existing.GameCharacterId,
            Classes = candidate.Classes.Count > 0 ? candidate.Classes : existing.Classes
        };
    }

    private static string? Prefer(string? primary, string? fallback) =>
        !string.IsNullOrWhiteSpace(primary) ? primary : fallback;

    private static int? ReadInt(IPropertyCollection? properties, DdoProperty property)
    {
        if (properties == null)
            return null;

        try
        {
            return properties.GetInt32PropertyValue((uint)property);
        }
        catch
        {
            return null;
        }
    }

    private static string? FormatGender(int? value) =>
        value switch
        {
            1 => "Male",
            2 => "Female",
            _ => null
        };
}
