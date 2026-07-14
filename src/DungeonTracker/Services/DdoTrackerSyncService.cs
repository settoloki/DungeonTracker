using DungeonTracker.Models;
using VoK.Sdk.Ddo;
using VoK.Sdk.Plugins;

namespace DungeonTracker.Services;

public sealed class DdoTrackerSyncService : IDisposable
{
    private readonly DdoTrackerApiClient _api;
    private readonly DdoTrackerSettingsStore _settings;
    private readonly IDdoGameDataProvider _provider;
    private readonly IPlugin? _plugin;
    private readonly string _pluginFolder;
    private readonly DevelopmentLog _devLog;
    private readonly object _lock = new();
    private readonly SemaphoreSlim _flushGate = new(1, 1);
    private readonly List<DdoTrackerCharacter> _characters = new();
    private QuestCatalog? _questCatalog;
    private bool _busy;
    private string _statusMessage = "DDO Tracker: signed out";

    public event Action? StateChanged;

    public DdoTrackerSyncService(
        string pluginFolder,
        IDdoGameDataProvider provider,
        DevelopmentLog? devLog = null,
        IPlugin? plugin = null)
    {
        _provider = provider;
        _plugin = plugin;
        _pluginFolder = pluginFolder;
        _devLog = devLog ?? new DevelopmentLog();
        _devLog.SwitchTo(pluginFolder);
        _settings = new DdoTrackerSettingsStore(pluginFolder);
        _api = new DdoTrackerApiClient();

        var snapshot = _settings.Snapshot;
        _api.SetBearerToken(snapshot.Token);
        _statusMessage = string.IsNullOrWhiteSpace(snapshot.Token)
            ? "DDO Tracker: signed out"
            : $"DDO Tracker: signed in as {snapshot.Email ?? "account"}";
    }

    public void AttachQuestCatalog(QuestCatalog catalog) => _questCatalog = catalog;

    public bool IsSignedIn => !string.IsNullOrWhiteSpace(_settings.Snapshot.Token);

    public bool AutoSync
    {
        get => _settings.Snapshot.AutoSync;
        set
        {
            _settings.Update(settings => settings.AutoSync = value);
            RaiseStateChanged();
        }
    }

    public string StatusMessage
    {
        get
        {
            lock (_lock)
            {
                return _statusMessage;
            }
        }
    }

    public string? SelectedCharacterId => _settings.Snapshot.SelectedCharacterId;

    public string? SelectedCharacterName => _settings.Snapshot.SelectedCharacterName;

    public string? Email => _settings.Snapshot.Email;

    public int PendingCount => _settings.Snapshot.Pending.Count;

    public IReadOnlyList<DdoTrackerCharacter> Characters
    {
        get
        {
            lock (_lock)
            {
                return _characters.ToList();
            }
        }
    }

    public IReadOnlyList<GameAccountCharacter> DiscoverGameCharacters() =>
        GameCharacterDiscovery.Discover(_provider, _plugin, _pluginFolder);

    public async Task InitializeAsync()
    {
        if (!IsSignedIn)
            return;

        try
        {
            await RefreshSessionAsync().ConfigureAwait(false);
            await RefreshQuestCatalogAsync().ConfigureAwait(false);
            await FlushPendingAsync().ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            SetStatus($"DDO Tracker: session error — {ex.Message}");
            _devLog.Log("DdoTracker", $"Initialize failed: {ex.Message}");
        }
    }

    public async Task LoginAsync(string email, string password)
    {
        SetBusy(true);
        try
        {
            var login = await _api.LoginAsync(email, password).ConfigureAwait(false);
            _api.SetBearerToken(login.Token);

            var display = login.User?.DisplayName
                ?? login.User?.Name
                ?? login.User?.Email
                ?? email;

            _settings.Update(settings =>
            {
                settings.Token = login.Token;
                settings.Email = login.User?.Email ?? email.Trim();
                settings.UserName = display;
            });

            await RefreshSessionAsync(syncCharacters: true).ConfigureAwait(false);
            await RefreshQuestCatalogAsync().ConfigureAwait(false);
            await FlushPendingAsync().ConfigureAwait(false);
            SetStatus($"DDO Tracker: signed in as {_settings.Snapshot.Email}");
            _devLog.Log("DdoTracker", $"Logged in as {_settings.Snapshot.Email}");
        }
        finally
        {
            SetBusy(false);
        }
    }

    public async Task LogoutAsync()
    {
        SetBusy(true);
        try
        {
            try
            {
                await _api.LogoutAsync().ConfigureAwait(false);
            }
            catch (Exception ex)
            {
                _devLog.Log("DdoTracker", $"Logout API call failed (clearing local session): {ex.Message}");
            }

            _api.SetBearerToken(null);
            _settings.Update(settings =>
            {
                settings.Token = null;
                settings.Email = null;
                settings.UserName = null;
                settings.SelectedCharacterId = null;
                settings.SelectedCharacterName = null;
                settings.Pending.Clear();
            });

            lock (_lock)
            {
                _characters.Clear();
            }

            SetStatus("DDO Tracker: signed out");
        }
        finally
        {
            SetBusy(false);
        }
    }

    public async Task RefreshSessionAsync(bool syncCharacters = true)
    {
        if (!IsSignedIn)
            return;

        SetBusy(true);
        try
        {
            _api.SetBearerToken(_settings.Snapshot.Token);
            var me = await _api.GetMeAsync().ConfigureAwait(false);
            var characters = await _api.GetCharactersAsync().ConfigureAwait(false);

            lock (_lock)
            {
                _characters.Clear();
                _characters.AddRange(characters.Where(c => !string.IsNullOrWhiteSpace(c.Id) && !string.IsNullOrWhiteSpace(c.Name)));
            }

            _settings.Update(settings =>
            {
                settings.Email = me.Email ?? settings.Email;
                settings.UserName = me.DisplayName ?? me.Name ?? settings.UserName;
            });

            if (syncCharacters)
                await SyncAccountCharactersAsync().ConfigureAwait(false);

            EnsureCharacterSelection();
            SetStatus(BuildReadyStatus());
        }
        catch (DdoTrackerApiException ex) when (ex.StatusCode is 401 or 403)
        {
            _api.SetBearerToken(null);
            _settings.Update(settings =>
            {
                settings.Token = null;
                settings.SelectedCharacterId = null;
                settings.SelectedCharacterName = null;
            });
            SetStatus("DDO Tracker: session expired — sign in again");
            throw;
        }
        finally
        {
            SetBusy(false);
        }
    }

    /// <summary>
    /// Upserts each known game toon to the website: find by name, update if linked, create if missing.
    /// </summary>
    public async Task SyncAccountCharactersAsync()
    {
        if (!IsSignedIn)
            return;

        var gameCharacters = DiscoverGameCharacters();
        if (gameCharacters.Count == 0)
        {
            _devLog.Log("DdoTracker", "No game characters discovered to sync");
            return;
        }

        _devLog.Log(
            "DdoTracker",
            $"Syncing {gameCharacters.Count} game character(s): {string.Join(", ", gameCharacters.Select(c => c.Name))}");

        var website = Characters.ToList();
        var created = 0;
        var updated = 0;
        var linked = 0;

        foreach (var gameChar in gameCharacters)
        {
            try
            {
                var match = await FindWebsiteCharacterAsync(gameChar, website).ConfigureAwait(false);
                var payload = BuildUpsertRequest(gameChar, touchLogin: true);

                if (match != null)
                {
                    var updatedChar = await _api.UpdateCharacterAsync(match.Id, payload).ConfigureAwait(false);
                    ReplaceCharacter(website, updatedChar);
                    linked++;
                    updated++;
                    _devLog.Log("DdoTracker", $"Updated website character '{updatedChar.Name}' (id {updatedChar.Id})");
                    continue;
                }

                var createdChar = await _api.CreateCharacterAsync(payload).ConfigureAwait(false);
                website.Add(createdChar);
                created++;
                linked++;
                _devLog.Log("DdoTracker", $"Created website character '{createdChar.Name}' (id {createdChar.Id})");
            }
            catch (Exception ex)
            {
                _devLog.Log("DdoTracker", $"Could not sync website character '{gameChar.Name}': {ex.Message}");
            }
        }

        lock (_lock)
        {
            _characters.Clear();
            _characters.AddRange(website.Where(c => !string.IsNullOrWhiteSpace(c.Id) && !string.IsNullOrWhiteSpace(c.Name)));
        }

        if (created > 0 || updated > 0)
        {
            SetStatus($"DDO Tracker: synced characters (linked {linked}, created {created}, updated {updated})");
            _devLog.Log("DdoTracker", $"Character sync complete: linked={linked}, created={created}, updated={updated}");
        }

        EnsureCharacterSelection();

        var selectedId = _settings.Snapshot.SelectedCharacterId;
        if (!string.IsNullOrWhiteSpace(selectedId))
        {
            try
            {
                await _api.NoteCharacterLoginAsync(selectedId).ConfigureAwait(false);
            }
            catch (Exception ex)
            {
                _devLog.Log("DdoTracker", $"Character login heartbeat failed: {ex.Message}");
            }
        }
    }

    public void SelectCharacter(string? characterId)
    {
        var characters = Characters;
        var match = characters.FirstOrDefault(c =>
            string.Equals(c.Id, characterId, StringComparison.OrdinalIgnoreCase));

        _settings.Update(settings =>
        {
            settings.SelectedCharacterId = match?.Id;
            settings.SelectedCharacterName = match?.Name;
        });

        SetStatus(BuildReadyStatus());

        if (match != null && IsSignedIn)
            _ = NoteLoginSafeAsync(match.Id);
    }

    public void HandleLocalRunCompleted(QuestRunRecord record)
    {
        if (!_settings.Snapshot.AutoSync)
            return;

        if (!IsSignedIn)
            return;

        if (record.RunKind != RunKind.Dungeon)
            return;

        if (!string.Equals(record.Outcome, "Completed", StringComparison.OrdinalIgnoreCase))
            return;

        var difficulty = NormalizeApiDifficulty(record);
        if (difficulty == null)
        {
            _devLog.Log("DdoTracker", $"Skipped sync for {record.QuestName}: could not resolve Heroic/Epic/Legendary tier");
            return;
        }

        var setting = TryNormalizeApiSetting(record.Difficulty);
        if (setting == null)
        {
            _devLog.Log(
                "DdoTracker",
                $"Skipped sync for {record.QuestName}: difficulty \"{record.Difficulty}\" is unknown (will not guess elite)");
            SetStatus($"DDO Tracker: skipped {record.QuestName} — difficulty unknown");
            return;
        }

        var characterId = _settings.Snapshot.SelectedCharacterId;
        if (string.IsNullOrWhiteSpace(characterId))
        {
            SetStatus("DDO Tracker: choose a website character to sync");
            return;
        }

        var rawName = record.QuestName.Trim();
        var syncName = _questCatalog?.ResolveCanonicalName(rawName, difficulty) ?? rawName;
        if (!string.Equals(rawName, syncName, StringComparison.Ordinal))
            _devLog.Log("DdoTracker", $"Canonicalized quest name for API: \"{rawName}\" -> \"{syncName}\"");

        var pending = new DdoTrackerPendingCompletion
        {
            CharacterId = characterId,
            Name = syncName,
            Difficulty = difficulty,
            Setting = setting,
            DurationSeconds = record.DurationSeconds > 0
                ? (int)Math.Round(record.DurationSeconds)
                : null,
            CompletedAtUtc = record.CompletedAtUtc == default ? DateTime.UtcNow : record.CompletedAtUtc,
            QueuedAtUtc = DateTime.UtcNow
        };

        _settings.Update(settings => settings.Pending.Add(pending));
        SetStatus($"DDO Tracker: syncing {pending.Name} ({difficulty}/{pending.Setting})…");
        _ = FlushPendingAsync();
    }

    public async Task FlushPendingAsync()
    {
        if (!IsSignedIn)
            return;

        if (!await _flushGate.WaitAsync(0).ConfigureAwait(false))
            return;

        SetBusy(true);
        try
        {
            var queue = _settings.Snapshot.Pending.ToList();
            if (queue.Count == 0)
                return;

            _api.SetBearerToken(_settings.Snapshot.Token);
            var remaining = new List<DdoTrackerPendingCompletion>();

            foreach (var item in queue)
            {
                try
                {
                    await _api.PostCompletionAsync(
                        item.CharacterId,
                        new DdoTrackerCompletionRequest
                        {
                            Name = item.Name,
                            Difficulty = item.Difficulty,
                            Setting = string.IsNullOrWhiteSpace(item.Setting) ? "elite" : item.Setting,
                            DurationSeconds = item.DurationSeconds,
                            CompletedAt = item.CompletedAtUtc?.ToUniversalTime().ToString("o")
                        }).ConfigureAwait(false);

                    _devLog.Log(
                        "DdoTracker",
                        $"Synced completion: {item.Name} ({item.Difficulty}/{item.Setting}) → character {item.CharacterId}");
                    SetStatus($"DDO Tracker: synced {item.Name} ({item.Difficulty}/{item.Setting})");
                }
                catch (Exception ex)
                {
                    item.Attempts++;
                    remaining.Add(item);
                    _devLog.Log("DdoTracker", $"Sync failed for {item.Name}: {ex.Message}");
                    SetStatus($"DDO Tracker: sync failed — {ex.Message}");
                }
            }

            _settings.Update(settings => settings.Pending = remaining);

            if (remaining.Count > 0)
                SetStatus($"DDO Tracker: {remaining.Count} completion(s) waiting to sync");
            else
                SetStatus(BuildReadyStatus());
        }
        finally
        {
            SetBusy(false);
            _flushGate.Release();
        }
    }

    public static string? NormalizeApiDifficulty(QuestRunRecord record)
    {
        var runDiff = record.Difficulty ?? string.Empty;
        if (runDiff.Contains("Legendary", StringComparison.OrdinalIgnoreCase))
            return "Legendary";
        if (runDiff.StartsWith("Epic", StringComparison.OrdinalIgnoreCase))
            return "Epic";

        if (TryNormalizeTier(record.QuestTier, out var fromTier)
            && (fromTier is "Legendary" or "Epic"))
            return fromTier;

        // XP can land in Epic/Legendary counters on Heroic runs (potion / VIP side effects).
        // Prefer catalog QuestTier Heroic when present; only use XP when QuestTier is empty/unknown.
        if (!TryNormalizeTier(record.QuestTier, out fromTier) || fromTier == "Heroic")
        {
            if (fromTier == "Heroic")
                return "Heroic";

            if (record.XpLegendary > 0)
                return "Legendary";
            if (record.XpEpic > 0)
                return "Epic";
            if (record.XpHeroic > 0)
                return "Heroic";
        }

        if (TryNormalizeTier(record.QuestTier, out fromTier))
            return fromTier;

        return "Heroic";
    }

    /// <summary>
    /// Maps in-game challenge labels to API setting: casual|normal|hard|elite|reaper.
    /// Returns null when difficulty is Unknown so callers can skip sync instead of guessing elite.
    /// </summary>
    public static string? TryNormalizeApiSetting(string? runDifficulty)
    {
        if (string.IsNullOrWhiteSpace(runDifficulty) || QuestLevelResolver.IsUnknownDifficulty(runDifficulty))
            return null;

        var value = runDifficulty.Trim();

        if (value.Contains("Reaper", StringComparison.OrdinalIgnoreCase)
            || (value.StartsWith('R') && value.Length > 1 && char.IsDigit(value[1])))
            return "reaper";

        if (value.Contains("Casual", StringComparison.OrdinalIgnoreCase)
            || value.Equals("Solo", StringComparison.OrdinalIgnoreCase))
            return "casual";

        if (value.Contains("Hard", StringComparison.OrdinalIgnoreCase))
            return "hard";

        if (value.Contains("Elite", StringComparison.OrdinalIgnoreCase))
            return "elite";

        if (value.Contains("Normal", StringComparison.OrdinalIgnoreCase)
            || value.Equals("Epic", StringComparison.OrdinalIgnoreCase)
            || value.Equals("Legendary", StringComparison.OrdinalIgnoreCase)
            || value.Equals("Epic Normal", StringComparison.OrdinalIgnoreCase)
            || value.Equals("Legendary Normal", StringComparison.OrdinalIgnoreCase))
            return "normal";

        return null;
    }

    /// <summary>
    /// Maps in-game challenge labels to API setting: casual|normal|hard|elite|reaper.
    /// Unknown maps to elite for backwards-compatible callers; prefer <see cref="TryNormalizeApiSetting"/>.
    /// </summary>
    public static string NormalizeApiSetting(string? runDifficulty)
    {
        return TryNormalizeApiSetting(runDifficulty) ?? "elite";
    }

    public void BindToCurrentGameCharacter()
    {
        if (!IsSignedIn)
            return;

        _ = SyncAndBindAsync();
    }

    private async Task SyncAndBindAsync()
    {
        try
        {
            await SyncAccountCharactersAsync().ConfigureAwait(false);
            EnsureCharacterSelection();
            RaiseStateChanged();
        }
        catch (Exception ex)
        {
            _devLog.Log("DdoTracker", $"Character sync/bind failed: {ex.Message}");
            EnsureCharacterSelection();
        }
    }

    private async Task NoteLoginSafeAsync(string characterId)
    {
        try
        {
            _api.SetBearerToken(_settings.Snapshot.Token);
            await _api.NoteCharacterLoginAsync(characterId).ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            _devLog.Log("DdoTracker", $"Character login heartbeat failed: {ex.Message}");
        }
    }

    private async Task<DdoTrackerCharacter?> FindWebsiteCharacterAsync(
        GameAccountCharacter gameChar,
        IReadOnlyList<DdoTrackerCharacter> website)
    {
        // Prefer API find (without server if website characters often lack it).
        try
        {
            var found = await _api.FindCharactersAsync(
                givenName: gameChar.GivenName,
                surname: gameChar.Surname,
                name: gameChar.Name).ConfigureAwait(false);

            var match = found.FirstOrDefault()
                ?? FindBestCharacterMatch(found, gameChar.Name);
            if (match != null)
                return match;
        }
        catch (Exception ex)
        {
            _devLog.Log("DdoTracker", $"characters/find failed for {gameChar.Name}: {ex.Message}");
        }

        return FindBestCharacterMatch(website, gameChar.Name)
            ?? FindBestCharacterMatch(website, gameChar.GivenName);
    }

    private static DdoTrackerCharacterUpsertRequest BuildUpsertRequest(GameAccountCharacter gameChar, bool touchLogin)
    {
        return new DdoTrackerCharacterUpsertRequest
        {
            GivenName = gameChar.GivenName,
            Surname = gameChar.Surname,
            Name = string.IsNullOrWhiteSpace(gameChar.Surname)
                ? gameChar.GivenName
                : $"{gameChar.GivenName} {gameChar.Surname}",
            Race = gameChar.Race,
            Gender = gameChar.Gender,
            Alignment = gameChar.Alignment,
            Server = gameChar.Server,
            Guild = gameChar.Guild,
            Classes = gameChar.Classes.Count > 0 ? gameChar.Classes.ToList() : null,
            EpicLevels = gameChar.EpicLevels,
            LastLoginAt = touchLogin ? "now" : null,
            TouchLastLogin = touchLogin
        };
    }

    private static void ReplaceCharacter(List<DdoTrackerCharacter> list, DdoTrackerCharacter updated)
    {
        var index = list.FindIndex(c => string.Equals(c.Id, updated.Id, StringComparison.OrdinalIgnoreCase));
        if (index >= 0)
            list[index] = updated;
        else
            list.Add(updated);
    }

    private void EnsureCharacterSelection()
    {
        var characters = Characters;
        if (characters.Count == 0)
        {
            _settings.Update(settings =>
            {
                settings.SelectedCharacterId = null;
                settings.SelectedCharacterName = null;
            });
            SetStatus(BuildReadyStatus());
            return;
        }

        var gameName = _provider.GetCurrentCharacterName()?.Trim();
        var match = FindBestCharacterMatch(characters, gameName);

        match ??= characters.Count == 1 ? characters[0] : null;

        if (match != null)
        {
            if (!string.Equals(
                    _settings.Snapshot.SelectedCharacterId,
                    match.Id,
                    StringComparison.OrdinalIgnoreCase))
            {
                SelectCharacter(match.Id);
                _devLog.Log(
                    "DdoTracker",
                    $"Auto-selected website character '{match.Name}' for in-game '{gameName ?? "(unknown)"}'");
            }
            else
            {
                _settings.Update(settings => settings.SelectedCharacterName = match.Name);
                SetStatus(BuildReadyStatus());
            }

            return;
        }

        var settings = _settings.Snapshot;
        if (!string.IsNullOrWhiteSpace(settings.SelectedCharacterId)
            && characters.Any(c => string.Equals(c.Id, settings.SelectedCharacterId, StringComparison.OrdinalIgnoreCase)))
        {
            SetStatus(BuildReadyStatus());
            return;
        }

        SelectCharacter(characters[0].Id);
        _devLog.Log("DdoTracker", $"No in-game match for '{gameName}'; defaulted to {characters[0].Name}");
    }

    private static DdoTrackerCharacter? FindBestCharacterMatch(
        IReadOnlyList<DdoTrackerCharacter> characters,
        string? gameName)
    {
        if (string.IsNullOrWhiteSpace(gameName) || characters.Count == 0)
            return null;

        var exact = characters.FirstOrDefault(c =>
            string.Equals(c.Name, gameName, StringComparison.OrdinalIgnoreCase)
            || string.Equals(c.GivenName, gameName, StringComparison.OrdinalIgnoreCase)
            || string.Equals(c.FirstName, gameName, StringComparison.OrdinalIgnoreCase));
        if (exact != null)
            return exact;

        GameCharacterDiscovery.SplitDisplayName(gameName, out var given, out var surname);
        if (!string.IsNullOrWhiteSpace(surname))
        {
            var byParts = characters.FirstOrDefault(c =>
                string.Equals(c.GivenName ?? c.FirstName, given, StringComparison.OrdinalIgnoreCase)
                && string.Equals(c.Surname, surname, StringComparison.OrdinalIgnoreCase));
            if (byParts != null)
                return byParts;
        }

        var startsWith = characters
            .Where(c => gameName.StartsWith(c.Name, StringComparison.OrdinalIgnoreCase)
                || c.Name.StartsWith(gameName, StringComparison.OrdinalIgnoreCase)
                || (!string.IsNullOrWhiteSpace(c.GivenName)
                    && gameName.StartsWith(c.GivenName, StringComparison.OrdinalIgnoreCase)))
            .OrderByDescending(c => c.Name.Length)
            .FirstOrDefault();
        if (startsWith != null)
            return startsWith;

        return characters.FirstOrDefault(c =>
            gameName.Contains(c.Name, StringComparison.OrdinalIgnoreCase)
            || c.Name.Contains(gameName, StringComparison.OrdinalIgnoreCase));
    }

    private string BuildReadyStatus()
    {
        var settings = _settings.Snapshot;
        if (string.IsNullOrWhiteSpace(settings.Token))
            return "DDO Tracker: signed out";

        if (string.IsNullOrWhiteSpace(settings.SelectedCharacterId))
            return $"DDO Tracker: signed in as {settings.Email} — select a character";

        var sync = settings.AutoSync ? "auto-sync on" : "auto-sync off";
        return $"DDO Tracker: {settings.SelectedCharacterName} · {sync}";
    }

    private static bool TryNormalizeTier(string? value, out string tier)
    {
        tier = string.Empty;
        if (string.IsNullOrWhiteSpace(value))
            return false;

        if (value.Equals("Heroic", StringComparison.OrdinalIgnoreCase))
        {
            tier = "Heroic";
            return true;
        }

        if (value.Equals("Epic", StringComparison.OrdinalIgnoreCase))
        {
            tier = "Epic";
            return true;
        }

        if (value.Equals("Legendary", StringComparison.OrdinalIgnoreCase))
        {
            tier = "Legendary";
            return true;
        }

        return false;
    }

    private async Task RefreshQuestCatalogAsync()
    {
        if (!IsSignedIn || _questCatalog == null)
            return;

        try
        {
            var json = await _api.GetQuestsJsonAsync().ConfigureAwait(false);
            if (string.IsNullOrWhiteSpace(json))
            {
                _devLog.Log("Catalog", "DDO Tracker /quests returned empty body");
                return;
            }

            try
            {
                Directory.CreateDirectory(_pluginFolder);
                var cachePath = Path.Combine(_pluginFolder, "quests.json");
                await File.WriteAllTextAsync(cachePath, json).ConfigureAwait(false);
            }
            catch (Exception ex)
            {
                _devLog.Log("Catalog", $"Could not cache quests.json: {ex.Message}");
            }

            if (_questCatalog.TryReplaceFromJson(json, "DDO Tracker API /quests", out var count))
                _devLog.Log("Catalog", $"Loaded {count} quest names from DDO Tracker API /quests");
            else
                _devLog.Log("Catalog", "DDO Tracker /quests JSON parsed to 0 entries");
        }
        catch (Exception ex)
        {
            _devLog.Log("Catalog", $"DDO Tracker catalog refresh failed: {ex.Message}");
        }
    }

    private void SetBusy(bool busy)
    {
        lock (_lock)
        {
            _busy = busy;
        }
    }

    private void SetStatus(string message)
    {
        lock (_lock)
        {
            _statusMessage = message;
        }

        RaiseStateChanged();
    }

    private void RaiseStateChanged() => StateChanged?.Invoke();

    public void Dispose()
    {
        _api.Dispose();
        _flushGate.Dispose();
    }
}
