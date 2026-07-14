using VoK.Sdk.Ddo;
using VoK.Sdk.Plugins;

namespace DungeonTracker.Services;

public sealed record CharacterDataContext(
    string DataFolder,
    string ServerName,
    string AccountId,
    string CharacterName)
{
    public bool IsReady =>
        !string.IsNullOrWhiteSpace(DataFolder)
        && !string.IsNullOrWhiteSpace(ServerName)
        && !string.IsNullOrWhiteSpace(AccountId)
        && !string.IsNullOrWhiteSpace(CharacterName);
}

public sealed class CharacterDataPathResolver
{
    private readonly IDdoGameDataProvider _provider;
    private readonly IPlugin _plugin;
    private readonly string _pluginFolder;

    public CharacterDataPathResolver(IDdoGameDataProvider provider, IPlugin plugin, string pluginFolder)
    {
        _provider = provider;
        _plugin = plugin;
        _pluginFolder = pluginFolder;
    }

    public CharacterDataContext? TryResolve()
    {
        var characterId = _provider.GetCurrentCharacterId();
        if (characterId is null or 0)
            return null;

        var characterName = _provider.GetCurrentCharacterName();
        if (string.IsNullOrWhiteSpace(characterName))
            return null;

        var accountId = _provider.GetSubscriptionKeyHash(_plugin);
        if (string.IsNullOrWhiteSpace(accountId))
            accountId = "unknown-account";

        var serverName = _provider.GetServerName();
        if (string.IsNullOrWhiteSpace(serverName))
            serverName = "unknown-server";

        var dataFolder = Path.Combine(
            _pluginFolder,
            SanitizePathSegment(serverName),
            SanitizePathSegment(accountId),
            SanitizePathSegment(characterName));

        return new CharacterDataContext(
            dataFolder,
            serverName.Trim(),
            accountId,
            characterName.Trim());
    }

    internal static string SanitizePathSegment(string value)
    {
        if (string.IsNullOrWhiteSpace(value))
            return "unknown";

        var trimmed = value.Trim();
        var invalid = Path.GetInvalidFileNameChars();
        var sanitized = new string(trimmed.Select(ch => invalid.Contains(ch) ? '_' : ch).ToArray());
        return string.IsNullOrWhiteSpace(sanitized) ? "unknown" : sanitized;
    }
}
