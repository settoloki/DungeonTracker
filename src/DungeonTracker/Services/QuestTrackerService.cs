using DungeonTracker.Models;
using VoK.Sdk.Ddo;
using VoK.Sdk.Ddo.Enums;
using VoK.Sdk.Enums;
using VoK.Sdk.Plugins;
using VoK.Sdk.Properties;

namespace DungeonTracker.Services;

public sealed class QuestTrackerService : IDisposable
{
    private const double MinimumRunSeconds = 5;
    private const double MinimumDungeonRunSeconds = 12;
    private const int ObjectiveClearPollsRequired = 3;
    private const double InstanceEntryGraceSeconds = 90;
    private const double AutoStartCooldownSeconds = 120;

    private readonly IDdoGameDataProvider _provider;
    private readonly IPlugin _plugin;
    private readonly string _pluginFolder;
    private readonly CharacterDataPathResolver _dataPaths;
    private readonly QuestHistoryStore _history;
    private readonly DevelopmentLog _devLog;
    private readonly InstanceQuestResolver _instanceQuests = new();
    private readonly SdkDebugProbe _debugProbe = new();
    private readonly Dictionary<uint, string> _armedPortalDifficultyByQuestDid = new();
    private readonly System.Threading.Timer _pollTimer;
    private int _pollInProgress;
    private ActiveQuestRun? _activeRun;
    private XpSnapshot? _startXpSnapshot;
    private long _dungeonEventXpHint;
    private uint _blockedAutoStartQuestDid;
    private DateTime? _blockedAutoStartUntilUtc;
    private IPortalInfo? _lastPortalInfo;
    private DateTime? _instanceEntryWatchStartedUtc;
    private DateTime? _lastUnknownDifficultyLogUtc;
    private bool _objectiveSeenDuringRun;
    private uint? _lastObjectiveDidDuringRun;
    private int _objectiveClearPolls;
    private int _instanceAbsentPolls;
    private uint _lastAcceptedQuestDid;
    private string _lastAcceptedQuestName = string.Empty;
    private InstanceDiagnostics _diagnostics = new();
    private readonly ExperienceReportCache _experienceReportCache = new();
    private ExperienceReportSnapshot _experienceReport = new();
    private SdkDebugSnapshot _debugSnapshot = new();
    private TrackingStatus _status = new();
    private string? _loadedCharacterDataFolder;
    private string _loadedCharacterLabel = string.Empty;
    private bool _pendingCatalogLog;

    public event Action? ActiveRunChanged;
    public event Action<QuestRunRecord>? RunCompleted;
    public event Action? StatusChanged;
    public event Action? DebugSnapshotChanged;
    public event Action? HistoryChanged;
    public event Action? CharacterContextChanged;

    public QuestTrackerService(IDdoGameDataProvider provider, string pluginFolder, IPlugin plugin)
    {
        _provider = provider;
        _plugin = plugin;
        _pluginFolder = pluginFolder;
        _dataPaths = new CharacterDataPathResolver(provider, plugin, pluginFolder);
        _history = new QuestHistoryStore();
        _devLog = new DevelopmentLog();
        QuestCatalog = new QuestCatalog(pluginFolder);
        // Catalog diagnostics are re-logged after the character data folder is known —
        // DevelopmentLog silently drops lines until SwitchTo() runs.
        _pendingCatalogLog = true;

        var events = provider.EventProvider;
        events.OnPortalActivate.AddHandler(portalInfo => HandlePortalEvent("activate", portalInfo));
        events.OnPortalActivateReadOnly.AddHandler(portalInfo => HandlePortalEvent("activate-readonly", portalInfo));
        events.OnPortalClose.AddHandler(_ =>
        {
            _instanceQuests.NotePortalClosed(_provider, _lastPortalInfo);
            NoteArmedPortalDifficulty(_lastPortalInfo);
            _debugProbe.NotePortalEvent(FormatPortalEvent("close", _lastPortalInfo));
            NoteAcceptedQuestFromPortal(_lastPortalInfo);
            BeginInstanceEntryWatch();
            PollRunState(forceCompletionChecks: false);
            return Task.CompletedTask;
        });
        events.OnRunScript.AddHandler(OnRunScript);
        events.OnAddAlert.AddHandler(OnAddAlert);
        events.OnChestOpen.AddHandler(containerId =>
        {
            _debugProbe.NoteChestEvent(containerId);
            // Completion is OnAddAlert-only — chest is not a clear signal.
            if (_activeRun != null)
                _devLog.Log("Completion", $"Ignored EndChest (waiting for OnAddAlert completion)");

            return Task.CompletedTask;
        });
        events.OnRewardOpened.AddHandler(containerId =>
        {
            _debugProbe.NoteRewardEvent(containerId);
            if (_activeRun != null)
                _devLog.Log("Completion", "Ignored RewardOpened (completion is OnAddAlert-only)");

            return Task.CompletedTask;
        });
        events.OnRewardClosed.AddHandler(containerId =>
        {
            _debugProbe.NoteRewardClosed(containerId);
            if (_activeRun != null)
                _devLog.Log("Completion", "Ignored RewardClosed (completion is OnAddAlert-only)");

            return Task.CompletedTask;
        });
        events.OnWorldNameReceived.AddHandler(_ =>
        {
            BeginInstanceEntryWatch();
            PollRunState(forceCompletionChecks: true);
            return Task.CompletedTask;
        });
        events.OnWorldPropertyReceived.AddHandler(property =>
        {
            // Observe summary/UI props for debug only — never a completion signal.
            _experienceReportCache.NoteProperty(_provider, property, "world-event");
            if (_activeRun != null && IsDungeonDifficultyProperty(property))
                TryRefreshActiveDifficulty();
            return Task.CompletedTask;
        });
        events.OnWorldPropertiesReceived.AddHandler(props =>
        {
            // Full world/volume property snapshot — this is when in-zone difficulty often becomes readable.
            NoteWorldDifficultySnapshot(props);
            if (_activeRun != null)
                TryRefreshActiveDifficulty();
            return Task.CompletedTask;
        });
        events.OnLogin.AddHandler(_ =>
        {
            EnsureCharacterDataLoaded(forceNotify: true);
            ResetActiveRun();
            return Task.CompletedTask;
        });

        _pollTimer = new System.Threading.Timer(
            _ => PollRunState(forceCompletionChecks: false),
            null,
            TimeSpan.FromMilliseconds(500),
            TimeSpan.FromMilliseconds(500));

        SetStatus(TrackingPhase.Idle, "Waiting for instance entry");
        PollRunState(forceCompletionChecks: true);
    }

    public void RefreshNow() => PollRunState(forceCompletionChecks: false);

    public QuestCatalog QuestCatalog { get; }

    public ActiveQuestRun? ActiveRun => _activeRun;

    public bool IsTracking => _activeRun != null;

    public double ActiveRunElapsedSeconds => ElapsedRunSeconds();

    public bool IsRunTimerPaused => _activeRun?.IsTimerPaused == true;

    public TrackingStatus Status => _status;

    public InstanceDiagnostics Diagnostics => _diagnostics;

    public SdkDebugSnapshot DebugSnapshot => _debugSnapshot;

    public string LastAcceptedQuestName => _lastAcceptedQuestName;

    public uint LastAcceptedQuestDid => _lastAcceptedQuestDid;

    public IReadOnlyList<QuestRunRecord> History => _history.Runs;

    public string CharacterDataFolder => _history.DataFolder;

    public string CharacterDataLabel => _loadedCharacterLabel;

    public XpBreakdown CurrentXpBreakdown
    {
        get
        {
            var startSnapshot = GetRunStartXpSnapshot();
            if (_activeRun == null || startSnapshot == null)
                return new XpBreakdown();

            return XpBreakdown.FromDelta(startSnapshot, XpTracker.Capture(_provider));
        }
    }

    public void ClearHistory() => _history.Clear();

    /// <summary>
    /// Cancels the active run without saving a completion (leave / wipe / abandoned mid-run).
    /// </summary>
    public bool StopTracking()
    {
        if (_activeRun == null)
            return false;

        var questDid = _activeRun.QuestDid;
        var questName = _activeRun.QuestName;
        _devLog.Log("Run", $"Stop tracking requested for {questName} (0x{questDid:X8})");
        ClearArmedPortalDifficulty(questDid);
        _lastPortalInfo = null;
        BlockAutoStart(questDid);
        ResetActiveRun();
        SetStatus(TrackingPhase.Idle, $"Stopped tracking — {questName}");
        return true;
    }

    public string ResolveQuestName(uint questDid)
    {
        try
        {
            var propertyMaster = _provider.PropertyMaster;
            var properties = propertyMaster.GetPropertyCollection(questDid);
            if (properties == null)
                return FormatQuestDid(questDid);

            var nameProperty = properties.GetStringInfoProperty((uint)DdoProperty.Name)
                ?? properties.GetStringInfoProperty((uint)DdoProperty.Quest_Name);

            var name = nameProperty?.GetText(propertyMaster, null, properties);
            return string.IsNullOrWhiteSpace(name) ? FormatQuestDid(questDid) : name;
        }
        catch
        {
            return FormatQuestDid(questDid);
        }
    }

    private Task OnAddAlert(IAddAlert alert)
    {
        var title = alert.Title?.Trim() ?? string.Empty;
        var description = alert.Description?.Trim() ?? string.Empty;
        var did = alert.Did ?? 0;

        // Log clear alerts always; log other alerts only while tracking so we can learn the payload.
        if (IsCompletionAlert(title, description))
        {
            _devLog.Log(
                "Alert",
                $"OnAddAlert completion did=0x{did:X8} title=\"{title}\" description=\"{description}\"");
            _debugProbe.NoteScriptEvent($"alert completion 0x{did:X8}");
            HandleCompletionAlert(did, title, description);
        }
        else if (_activeRun != null)
        {
            _devLog.Log(
                "Alert",
                $"OnAddAlert did=0x{did:X8} title=\"{title}\" description=\"{description}\"");
        }

        return Task.CompletedTask;
    }

    private void HandleCompletionAlert(uint did, string title, string description)
    {
        if (_activeRun == null)
            return;

        if (_activeRun.RunKind == RunKind.Dungeon
            && ElapsedRunSeconds() < MinimumDungeonRunSeconds)
        {
            _devLog.Log("Completion", "Ignored OnAddAlert completion (run too short)");
            return;
        }

        if (_activeRun.RunKind == RunKind.AdventureArea
            && ElapsedRunSeconds() < MinimumRunSeconds)
            return;

        // Prefer matching the alert's quest Did to the run we are tracking.
        if (did != 0 && _activeRun.QuestDid != 0 && did != _activeRun.QuestDid)
        {
            var nameMatch = !string.IsNullOrWhiteSpace(_activeRun.QuestName)
                && description.Contains(_activeRun.QuestName, StringComparison.OrdinalIgnoreCase);
            if (!nameMatch)
            {
                _devLog.Log(
                    "Completion",
                    $"Ignored OnAddAlert completion for other quest 0x{did:X8} (tracking 0x{_activeRun.QuestDid:X8})");
                return;
            }
        }

        // "Adventure Completed\nYou receive 696 XP." is the sole clear signal.
        NoteAlertAwardXp(description);

        var label = DescribeCompletionAlert(title, description);
        _devLog.Log(
            "Completion",
            $"OnAddAlert {label} for {_activeRun.QuestName} — completing");
        CompleteActiveRun(CompletionReason.AlertCompleted);
    }

    private void NoteAlertAwardXp(string description)
    {
        if (string.IsNullOrWhiteSpace(description))
            return;

        var match = System.Text.RegularExpressions.Regex.Match(
            description,
            @"You receive\s+(\d[\d,]*)\s+XP",
            System.Text.RegularExpressions.RegexOptions.IgnoreCase);
        if (!match.Success)
            return;

        if (!long.TryParse(match.Groups[1].Value.Replace(",", ""), out var xp) || xp <= 0)
            return;

        _dungeonEventXpHint = Math.Max(_dungeonEventXpHint, xp);
        _devLog.Log("Completion", $"OnAddAlert XP hint={xp}");
    }

    private static string DescribeCompletionAlert(string title, string description)
    {
        var text = $"{title}\n{description}";
        if (text.Contains("adventure complete", StringComparison.OrdinalIgnoreCase))
            return "adventure completed";
        if (text.Contains("quest complete", StringComparison.OrdinalIgnoreCase))
            return "quest completed";
        return "completion";
    }

    private static bool IsCompletionAlert(string title, string description)
    {
        static bool LooksLikeCompletion(string text)
        {
            if (string.IsNullOrWhiteSpace(text))
                return false;

            return text.Contains("quest completed", StringComparison.OrdinalIgnoreCase)
                || text.Contains("quest complete", StringComparison.OrdinalIgnoreCase)
                || text.Contains("adventure completed", StringComparison.OrdinalIgnoreCase)
                || text.Contains("adventure complete", StringComparison.OrdinalIgnoreCase);
        }

        return LooksLikeCompletion(title) || LooksLikeCompletion(description);
    }

    private Task OnRunScript(ScriptEventArgs args)
    {
        var dungeonEvent = ReadDungeonEvent(args.Properties);
        var questStatus = ReadDungeonQuestStatus(args.Properties);
        var eventXp = ReadDungeonEventXp(args.Properties);

        if (dungeonEvent != null)
        {
            _debugProbe.NoteDungeonEvent(dungeonEvent.Value);
            if (dungeonEvent == DungeonEvent.PlayerEntered)
                BeginInstanceEntryWatch();
        }
        else if (questStatus == null)
        {
            _debugProbe.NoteScriptEvent($"fx=0x{args.FxId:X8} target=0x{args.ScriptTargetIid:X16}");
        }

        if (_activeRun == null || _activeRun.RunKind != RunKind.Dungeon)
            return Task.CompletedTask;

        if (eventXp is > 0)
        {
            _dungeonEventXpHint = Math.Max(_dungeonEventXpHint, eventXp.Value);
            _devLog.Log("Completion", $"DungeonEvent_XP hint={eventXp.Value}");
        }

        // Logging only — completion is OnAddAlert-only.
        if (questStatus == QuestStatus.Completed)
            _devLog.Log("Completion", $"Ignored DungeonEvent_NewQuestStatus=Completed (waiting for OnAddAlert)");
        else if (questStatus == QuestStatus.Failed)
            _devLog.Log("Completion", $"Ignored DungeonEvent_NewQuestStatus=Failed (waiting for OnAddAlert)");
        else if (dungeonEvent is DungeonEvent.EndModule or DungeonEvent.LockModule
            or DungeonEvent.AllMonstersDied or DungeonEvent.TimeExpired)
            _devLog.Log("Completion", $"Ignored DungeonEvent.{dungeonEvent} (waiting for OnAddAlert)");

        return Task.CompletedTask;
    }

    private static DungeonEvent? ReadDungeonEvent(IPropertyCollection? properties)
    {
        if (properties == null)
            return null;

        try
        {
            var enumProperty = properties.GetEnumProperty((uint)DdoProperty.DungeonEvent_ID);
            if (enumProperty?.Value == null)
                return null;

            var value = Convert.ToUInt32(enumProperty.Value);
            return Enum.IsDefined(typeof(DungeonEvent), value) ? (DungeonEvent)value : null;
        }
        catch
        {
            return null;
        }
    }

    private static QuestStatus? ReadDungeonQuestStatus(IPropertyCollection? properties)
    {
        if (properties == null)
            return null;

        try
        {
            var enumProperty = properties.GetEnumProperty((uint)DdoProperty.DungeonEvent_NewQuestStatus);
            if (enumProperty?.Value != null)
            {
                var value = Convert.ToUInt32(enumProperty.Value);
                if (Enum.IsDefined(typeof(QuestStatus), value))
                    return (QuestStatus)value;
            }

            var intValue = properties.GetInt32PropertyValue((uint)DdoProperty.DungeonEvent_NewQuestStatus);
            if (intValue is int statusInt && Enum.IsDefined(typeof(QuestStatus), (uint)statusInt))
                return (QuestStatus)(uint)statusInt;

            var longValue = properties.GetInt64PropertyValue((uint)DdoProperty.DungeonEvent_NewQuestStatus);
            if (longValue is long statusLong
                && statusLong is >= 0 and <= uint.MaxValue
                && Enum.IsDefined(typeof(QuestStatus), (uint)statusLong))
                return (QuestStatus)(uint)statusLong;
        }
        catch
        {
            // Ignore read failures.
        }

        return null;
    }

    private static long? ReadDungeonEventXp(IPropertyCollection? properties)
    {
        if (properties == null)
            return null;

        try
        {
            var intValue = properties.GetInt32PropertyValue((uint)DdoProperty.DungeonEvent_XP);
            if (intValue is > 0)
                return intValue.Value;

            var longValue = properties.GetInt64PropertyValue((uint)DdoProperty.DungeonEvent_XP);
            if (longValue is > 0)
                return longValue.Value;
        }
        catch
        {
            // Ignore read failures.
        }

        return null;
    }

    private void BeginInstanceEntryWatch()
    {
        // Portal UI is used while still InTown — start the watch there so we keep
        // the selected Hard/Elite until StartRun consumes it.
        _instanceEntryWatchStartedUtc = DateTime.UtcNow;
    }

    private void EnsureCharacterDataLoaded(bool forceNotify = false)
    {
        var context = _dataPaths.TryResolve();
        if (context is not { IsReady: true })
            return;

        var historySwitched = _history.SwitchTo(context.DataFolder);
        var logSwitched = _devLog.SwitchTo(context.DataFolder);

        if (_pendingCatalogLog)
        {
            _pendingCatalogLog = false;
            _devLog.Log("Catalog", $"Loaded {QuestCatalog.Count} quest names from {QuestCatalog.LoadedFrom}");
            foreach (var diagnostic in QuestCatalog.LoadDiagnostics)
                _devLog.Log("Catalog", diagnostic);
            _devLog.Log("Catalog", $"Plugin folder: {_pluginFolder}");
        }

        if (!historySwitched && !logSwitched && !forceNotify)
            return;

        _loadedCharacterDataFolder = context.DataFolder;
        _loadedCharacterLabel = $"{context.ServerName} / {context.AccountId} / {context.CharacterName}";

        if (historySwitched || logSwitched)
            _devLog.Log("Data", $"Loaded character data from {context.DataFolder}");

        HistoryChanged?.Invoke();
        CharacterContextChanged?.Invoke();
    }

    private void PollRunState(bool forceCompletionChecks)
    {
        if (Interlocked.CompareExchange(ref _pollInProgress, 1, 0) != 0)
            return;

        try
        {
            PollRunStateCore(forceCompletionChecks);
        }
        finally
        {
            Interlocked.Exchange(ref _pollInProgress, 0);
        }
    }

    private void PollRunStateCore(bool forceCompletionChecks)
    {
        EnsureCharacterDataLoaded();

        _diagnostics = _instanceQuests.Probe(_provider);
        _experienceReport = ExperienceReportReader.Read(_provider, _experienceReportCache);
        UpdateLastAcceptedQuest();

        var inTown = _diagnostics.InTown;
        var instanceQuestDid = _diagnostics.ResolvedQuestDid;
        var objectiveDid = _provider.GetCurrentQuestObjectiveDid();

        if (inTown == true)
        {
            if (IsWatchingForInstanceEntry())
            {
                // Re-read live IPortalInfo.QuestGenericDifficulty only (not town Cached —
                // that sticks at Normal for Casual/Hard/Elite alike).
                NoteArmedPortalDifficulty(_lastPortalInfo);
            }
            else
            {
                _instanceQuests.ClearPortalEntryQuest();
                _lastPortalInfo = null;
            }
        }

        if (_activeRun == null)
        {
            if (instanceQuestDid != 0 && ShouldAutoStart(inTown, instanceQuestDid, IsAutoStartBlocked(instanceQuestDid)))
                StartRun(instanceQuestDid);

            if (_activeRun == null)
                UpdateIdleStatus(inTown, instanceQuestDid);
        }
        else
        {
            if (instanceQuestDid != 0
                && instanceQuestDid != _activeRun.QuestDid
                && ElapsedRunSeconds() >= MinimumRunSeconds)
            {
                // Never auto-complete on zone change — only OnAddAlert completes.
                _devLog.Log(
                    "Run",
                    $"Instance quest changed 0x{_activeRun.QuestDid:X8} -> 0x{instanceQuestDid:X8}; stopping unfinished prior run (no OnAddAlert)");
                BlockAutoStart(_activeRun.QuestDid);
                ResetActiveRun();
            }

            if (_activeRun == null)
            {
                RefreshDebugSnapshot();
                return;
            }

            // Use raw instance quest DID only — ResolvedQuestDid can stick via ActiveQuest after you leave.
            var rawInstanceQuestDid = ReadUInt(_provider.GetInstanceQuestDid());
            if (rawInstanceQuestDid == _activeRun.QuestDid)
                _instanceAbsentPolls = 0;

            if (_activeRun.RunKind == RunKind.Dungeon && objectiveDid is uint objective && objective != 0)
            {
                if (_objectiveSeenDuringRun
                    && _lastObjectiveDidDuringRun is uint lastObjective
                    && lastObjective != objective
                    && ElapsedRunSeconds() >= MinimumDungeonRunSeconds)
                {
                    _devLog.Log(
                        "Completion",
                        $"Ignored objective DID change 0x{lastObjective:X8} -> 0x{objective:X8} (waiting for OnAddAlert)");
                }

                _objectiveSeenDuringRun = true;
                _lastObjectiveDidDuringRun = objective;
                _objectiveClearPolls = 0;
            }

            TryRefreshActiveDifficulty();

            var xp = CurrentXpBreakdown;
            var xpLabel = xp.Total > 0
                ? $"{xp.Total:N0} XP ({xp.PerMinute(ElapsedRunSeconds()):N0}/min)"
                : "0 XP";

            SetStatus(
                TrackingPhase.Tracking,
                $"Tracking — {FormatRunLabel(_activeRun)} · {xpLabel}");

            if (ElapsedRunSeconds() >= MinimumRunSeconds)
            {
                if (_activeRun.RunKind == RunKind.AdventureArea)
                    PollAdventureAreaCompletion(inTown, instanceQuestDid, forceCompletionChecks);
                else
                    PollDungeonCompletion(inTown, instanceQuestDid, objectiveDid, forceCompletionChecks);
            }
        }

        RefreshDebugSnapshot();
    }

    private void RefreshDebugSnapshot()
    {
        _debugSnapshot = _debugProbe.Capture(_provider, _diagnostics, BuildRuntimeState());
        DebugSnapshotChanged?.Invoke();
    }

    private QuestTrackerRuntimeState BuildRuntimeState()
    {
        return new QuestTrackerRuntimeState
        {
            TrackingActive = _activeRun != null,
            ActiveRunLabel = _activeRun == null ? "—" : FormatRunLabel(_activeRun),
            ObjectiveSeenDuringRun = _objectiveSeenDuringRun,
            ObjectiveClearPolls = _objectiveClearPolls,
            InstanceAbsentPolls = _instanceAbsentPolls,
            EntryWatchActive = IsWatchingForInstanceEntry(),
            LastAcceptedQuestName = string.IsNullOrWhiteSpace(_lastAcceptedQuestName) ? "—" : _lastAcceptedQuestName,
            LastAcceptedQuestDid = _lastAcceptedQuestDid,
            StatusMessage = _status.Message,
            QuestCatalogCount = QuestCatalog.Count,
            QuestCatalogSource = QuestCatalog.LoadedFrom,
            CatalogLookup = BuildCatalogLookupLine(),
            CharacterDataFolder = string.IsNullOrWhiteSpace(_loadedCharacterLabel) ? "—" : _loadedCharacterLabel,
            PluginFolder = string.IsNullOrWhiteSpace(_pluginFolder) ? "—" : _pluginFolder,
            LivePortalDifficulty = PortalDifficultyReader.Describe(_lastPortalInfo?.QuestGenericDifficulty),
            ArmedPortalDifficulty = DescribeArmedPortalDifficulty(),
            SummaryTitle = string.IsNullOrWhiteSpace(_experienceReport.Title) ? "—" : _experienceReport.Title,
            SummaryText = string.IsNullOrWhiteSpace(_experienceReport.Text) ? "—" : _experienceReport.Text,
            SummaryLooksComplete = _experienceReport.LooksComplete,
            ExperienceReportDifficulty = _experienceReport.Parsed.Difficulty ?? _experienceReport.UiDifficultyLabel ?? "—",
            ExperienceReportXp = _experienceReport.Parsed.TotalXp,
            ExperienceReportSource = string.IsNullOrWhiteSpace(_experienceReport.Source)
                ? _experienceReportCache.DescribeState()
                : _experienceReport.Source,
            ChallengeScore = _experienceReport.ChallengeScore
        };
    }

    private string BuildCatalogLookupLine()
    {
        var questName = _activeRun?.QuestName;
        if (string.IsNullOrWhiteSpace(questName))
            questName = _lastAcceptedQuestName;

        if (string.IsNullOrWhiteSpace(questName))
            return "—";

        var entry = QuestCatalog.FindEntry(questName);
        if (entry == null)
            return $"{questName} -> not found";

        var difficulty = _activeRun?.Difficulty ?? "Unknown";
        var effective = QuestLevelResolver.ComputeEffectiveLevel(entry.Level, difficulty);
        var effectiveLabel = effective?.ToString() ?? "?";
        return $"{questName} -> base L{entry.Level}, effective L{effectiveLabel} ({entry.Difficulty})";
    }


    private Task HandlePortalEvent(string phase, IPortalInfo? portalInfo)
    {
        _lastPortalInfo = portalInfo;
        ClearAutoStartBlock();
        _instanceQuests.NotePortalActivated(portalInfo);
        BeginInstanceEntryWatch();
        NoteArmedPortalDifficulty(portalInfo);
        var portalEvent = FormatPortalEvent(phase, portalInfo);
        _debugProbe.NotePortalEvent(portalEvent);
        _devLog.Log("Portal", portalEvent);
        NoteAcceptedQuestFromPortal(portalInfo);
        return Task.CompletedTask;
    }

    /// <summary>
    /// Remember portal difficulty per quest DID from QuestGenericDifficulty only.
    /// Town Cached/Inventory are not used: with the portal open they stay sticky Normal
    /// for Casual/Hard/Elite alike (confirmed by probe dumps).
    /// Empty activate flashes are ignored so Hard→—→zone still works when a real flash occurred.
    /// Cleared on Stop / when applied.
    /// </summary>
    private void NoteArmedPortalDifficulty(IPortalInfo? portalInfo)
    {
        var questDid = ResolvePortalQuestDid(portalInfo);
        if (questDid == 0)
            return;

        var fromPortal = PortalDifficultyReader.TryReadDifficultyLabel(portalInfo);
        if (string.IsNullOrWhiteSpace(fromPortal) || QuestLevelResolver.IsUnknownDifficulty(fromPortal))
            return;

        ArmPortalDifficulty(questDid, fromPortal.Trim());
    }

    private void ArmPortalDifficulty(uint questDid, string difficulty)
    {
        if (_armedPortalDifficultyByQuestDid.TryGetValue(questDid, out var existing)
            && existing.Equals(difficulty, StringComparison.OrdinalIgnoreCase))
            return;

        _armedPortalDifficultyByQuestDid[questDid] = difficulty;
        _devLog.Log("Portal", $"Armed difficulty {difficulty} for quest 0x{questDid:X8}");
    }

    private void ClearArmedPortalDifficulty(uint questDid)
    {
        if (questDid == 0)
            return;

        if (!_armedPortalDifficultyByQuestDid.Remove(questDid))
            return;

        _devLog.Log("Portal", $"Cleared armed difficulty for quest 0x{questDid:X8}");
    }

    private static uint ResolvePortalQuestDid(IPortalInfo? portalInfo)
    {
        var quests = portalInfo?.AvailableQuests;
        if (quests == null || quests.Count == 0)
            return 0;

        if (quests.Count == 1)
            return quests[0];

        return 0;
    }

    private string? TryGetArmedPortalDifficulty(uint questDid)
    {
        if (questDid == 0)
            return null;

        return _armedPortalDifficultyByQuestDid.TryGetValue(questDid, out var difficulty)
            ? difficulty
            : null;
    }

    private string DescribeArmedPortalDifficulty()
    {
        var portalDid = ResolvePortalQuestDid(_lastPortalInfo);
        if (portalDid == 0)
            portalDid = _diagnostics.PortalEntryQuestDid;

        if (portalDid == 0)
            return _armedPortalDifficultyByQuestDid.Count == 0
                ? "—"
                : string.Join(", ", _armedPortalDifficultyByQuestDid.Select(kv =>
                    $"0x{kv.Key:X8}={kv.Value}"));

        return TryGetArmedPortalDifficulty(portalDid) is string armed
            ? $"0x{portalDid:X8}={armed}"
            : $"0x{portalDid:X8}=(none)";
    }

    private void UpdateLastAcceptedQuest()
    {
        var journalDid = ReadUInt(_provider.GetCurrentQuestDid());
        if (journalDid == 0 || journalDid == _lastAcceptedQuestDid)
            return;

        _lastAcceptedQuestDid = journalDid;
        _lastAcceptedQuestName = ResolveQuestName(journalDid);
    }

    private void NoteAcceptedQuestFromPortal(IPortalInfo? portalInfo)
    {
        var selected = ReadUInt(_provider.GetCurrentQuestDid());
        if (selected != 0 && portalInfo?.AvailableQuests?.Contains(selected) == true)
        {
            SetLastAcceptedQuest(selected);
            return;
        }

        var onlyQuest = portalInfo?.AvailableQuests?.Count == 1 ? portalInfo.AvailableQuests[0] : 0;
        if (onlyQuest != 0)
            SetLastAcceptedQuest(onlyQuest);
    }

    private void SetLastAcceptedQuest(uint questDid)
    {
        if (questDid == 0 || questDid == _lastAcceptedQuestDid)
            return;

        _lastAcceptedQuestDid = questDid;
        _lastAcceptedQuestName = ResolveQuestName(questDid);
    }

    private void PollAdventureAreaCompletion(bool? inTown, uint instanceQuestDid, bool forceCompletionChecks)
    {
        // Completion is OnAddAlert-only — zoning out only pauses.
        if (inTown == true || instanceQuestDid != _activeRun?.QuestDid)
        {
            _instanceAbsentPolls++;
            if (_activeRun != null
                && (_instanceAbsentPolls >= ObjectiveClearPollsRequired || forceCompletionChecks || inTown == true))
            {
                PauseRunTimer();
                SetStatus(
                    TrackingPhase.Tracking,
                    $"Paused outside area — {FormatRunLabel(_activeRun)} (waiting for OnAddAlert · Stop Tracking to cancel)");
            }
        }
        else
        {
            _instanceAbsentPolls = 0;
            ResumeRunTimer();
        }
    }

    private void PollDungeonCompletion(bool? inTown, uint instanceQuestDid, uint? objectiveDid, bool forceCompletionChecks)
    {
        var rawInstanceQuestDid = ReadUInt(_provider.GetInstanceQuestDid());
        var leftTrackedInstance = _activeRun != null
            && rawInstanceQuestDid != _activeRun.QuestDid;

        if (objectiveDid is uint objective && objective != 0)
        {
            _objectiveSeenDuringRun = true;
            _lastObjectiveDidDuringRun ??= objective;
            _objectiveClearPolls = 0;
        }

        if (inTown == true || leftTrackedInstance)
        {
            _instanceAbsentPolls++;
            if (_activeRun != null
                && (_instanceAbsentPolls >= ObjectiveClearPollsRequired || forceCompletionChecks || inTown == true))
            {
                PauseRunTimer();
                SetStatus(
                    TrackingPhase.Tracking,
                    $"Paused outside instance — {FormatRunLabel(_activeRun)} (waiting for OnAddAlert · Stop Tracking to cancel)");
            }

            return;
        }

        _instanceAbsentPolls = 0;
        ResumeRunTimer();
    }

    private void PauseRunTimer()
    {
        if (_activeRun == null || _activeRun.IsTimerPaused)
            return;

        var now = DateTime.UtcNow;
        if (_activeRun.ActiveSegmentStartedAtUtc is DateTime segmentStart)
        {
            var segment = (now - segmentStart).TotalSeconds;
            if (segment > 0)
                _activeRun.AccumulatedActiveSeconds += segment;
        }

        _activeRun.ActiveSegmentStartedAtUtc = null;
        _devLog.Log("Timer", $"Paused at {FormatDuration(TimeSpan.FromSeconds(_activeRun.AccumulatedActiveSeconds))}");
        ActiveRunChanged?.Invoke();
    }

    private void ResumeRunTimer()
    {
        if (_activeRun == null || !_activeRun.IsTimerPaused)
            return;

        _activeRun.ActiveSegmentStartedAtUtc = DateTime.UtcNow;
        _devLog.Log("Timer", $"Resumed after {_activeRun.AccumulatedActiveSeconds:0}s banked");
        ActiveRunChanged?.Invoke();
    }


    private void ClearAutoStartBlock()
    {
        if (_blockedAutoStartQuestDid == 0 && _blockedAutoStartUntilUtc == null)
            return;

        _devLog.LogChange("AutoStart", "BlockedQuestDid", _blockedAutoStartQuestDid, 0);
        _blockedAutoStartQuestDid = 0;
        _blockedAutoStartUntilUtc = null;
    }

    private void BlockAutoStart(uint questDid)
    {
        _blockedAutoStartQuestDid = questDid;
        _blockedAutoStartUntilUtc = DateTime.UtcNow.AddSeconds(AutoStartCooldownSeconds);
        _devLog.Log("AutoStart", $"Blocked auto-start for quest 0x{questDid:X8} until {_blockedAutoStartUntilUtc:O}");
    }

    private bool IsAutoStartBlocked(uint questDid)
    {
        return _blockedAutoStartQuestDid == questDid
            && _blockedAutoStartUntilUtc != null
            && DateTime.UtcNow < _blockedAutoStartUntilUtc.Value;
    }


    private XpSnapshot? GetRunStartXpSnapshot()
    {
        if (_startXpSnapshot != null)
            return _startXpSnapshot;

        if (_activeRun == null)
            return null;

        return new XpSnapshot
        {
            Heroic = _activeRun.StartXpHeroic,
            Epic = _activeRun.StartXpEpic,
            Legendary = _activeRun.StartXpLegendary,
            Reaper = _activeRun.StartXpReaper
        };
    }

    private XpBreakdown ResolveCompletionXp()
    {
        var startSnapshot = GetRunStartXpSnapshot();
        var xp = startSnapshot == null
            ? new XpBreakdown()
            : XpBreakdown.FromDelta(startSnapshot, XpTracker.Capture(_provider));

        // OnAddAlert "You receive N XP" is the game's declared adventure award.
        // Prefer it when larger than the live delta — Character_TotalXP often lags the
        // alert (and mid-run objective XP / level-ups can make the delta look small).
        if (_dungeonEventXpHint > 0 && _dungeonEventXpHint > xp.Total)
        {
            _devLog.Log(
                "Xp",
                $"Using OnAddAlert XP hint={_dungeonEventXpHint} (delta adventure={xp.Total}, reaper={xp.Reaper})");
            return AssignAdventureXp(xp, _dungeonEventXpHint);
        }

        if (xp.AwardTotal > 0)
        {
            _devLog.Log(
                "Xp",
                $"Using run delta: adventure={xp.Total}, reaper={xp.Reaper} (heroic={xp.Heroic}, epic={xp.Epic}, legendary={xp.Legendary})");
            return xp;
        }

        if (_dungeonEventXpHint > 0)
        {
            _devLog.Log("Xp", $"Using OnAddAlert XP hint: {_dungeonEventXpHint}");
            return AssignAdventureXp(xp, _dungeonEventXpHint);
        }

        _devLog.Log("Xp", "No XP captured — recording 0");
        return xp;
    }

    private static XpBreakdown AssignAdventureXp(XpBreakdown existing, long adventureXp)
    {
        // Keep any reaper delta; place adventure XP into the remake bucket that already moved.
        if (existing.Legendary > 0)
            return new XpBreakdown { Legendary = adventureXp, Reaper = existing.Reaper };

        if (existing.Epic > 0)
            return new XpBreakdown { Epic = adventureXp, Reaper = existing.Reaper };

        return new XpBreakdown { Heroic = adventureXp, Reaper = existing.Reaper };
    }

    private static bool ShouldAutoStart(bool? inTown, uint instanceQuestDid, bool isBlocked)
    {
        if (instanceQuestDid == 0 || inTown == true || isBlocked)
            return false;

        return true;
    }

    private void UpdateIdleStatus(bool? inTown, uint instanceQuestDid)
    {
        if (inTown == true)
        {
            SetStatus(TrackingPhase.Idle, "Waiting for instance entry");
            return;
        }

        if (instanceQuestDid == 0 && IsWatchingForInstanceEntry())
        {
            SetStatus(TrackingPhase.Idle, "Zoning in — waiting for instance quest signal");
            return;
        }

        if (instanceQuestDid == 0 && _diagnostics.LooksLikeInstanceArea)
        {
            SetStatus(TrackingPhase.Idle, "Inside instance — waiting for quest signal");
            return;
        }

        if (instanceQuestDid == 0)
        {
            SetStatus(TrackingPhase.Idle, "Outside instance — enter a dungeon or adventure area");
            return;
        }

        SetStatus(TrackingPhase.Idle, "Inside instance — starting tracker");
    }

    private bool IsWatchingForInstanceEntry()
    {
        return _instanceEntryWatchStartedUtc != null
            && (DateTime.UtcNow - _instanceEntryWatchStartedUtc.Value).TotalSeconds <= InstanceEntryGraceSeconds;
    }

    private void StartRun(uint questDid)
    {
        var xpSnapshot = XpTracker.Capture(_provider);
        var questName = ResolveQuestName(questDid);
        var runKind = ClassifyQuest(questDid);
        var baseLevel = QuestCatalog.FindEntry(questName)?.Level;
        // Volume/Server difficulty is only available in-zone — start Unknown until props appear.
        var difficulty = QuestDifficultyResolver.ResolveForInstance(_provider, baseLevel);

        var startedAt = DateTime.UtcNow;
        _activeRun = new ActiveQuestRun
        {
            QuestDid = questDid,
            QuestName = questName,
            RunKind = runKind,
            Difficulty = difficulty,
            CharacterName = _provider.GetCurrentCharacterName() ?? "Unknown",
            ServerName = _provider.GetServerName() ?? "Unknown",
            StartedAtUtc = startedAt,
            AccumulatedActiveSeconds = 0,
            ActiveSegmentStartedAtUtc = startedAt,
            StartXpHeroic = xpSnapshot.Heroic,
            StartXpEpic = xpSnapshot.Epic,
            StartXpLegendary = xpSnapshot.Legendary,
            StartXpReaper = xpSnapshot.Reaper
        };

        RefineRunDifficulty(_activeRun);

        _startXpSnapshot = xpSnapshot;
        _objectiveSeenDuringRun = runKind == RunKind.Dungeon
            && _provider.GetCurrentQuestObjectiveDid() is uint objective && objective != 0;
        _lastObjectiveDidDuringRun = _objectiveSeenDuringRun
            ? _provider.GetCurrentQuestObjectiveDid()
            : null;
        _objectiveClearPolls = 0;
        _dungeonEventXpHint = 0;
        _instanceAbsentPolls = 0;
        _instanceEntryWatchStartedUtc = null;

        _devLog.Log(
            "Run",
            $"Started {questName} (0x{questDid:X8}) kind={runKind} difficulty={_activeRun.Difficulty}");
        _devLog.Log(
            "Difficulty",
            QuestDifficultyResolver.DescribeResolution(_provider, baseLevel));
        _devLog.Log(
            "Difficulty",
            $"Server difficulty scan: {QuestDifficultyResolver.ScanServerDifficultyProps(_provider)}");
        _lastUnknownDifficultyLogUtc = DateTime.UtcNow;

        var referenceXp = QuestCatalog.TryGetReferenceXp(questName, _activeRun.Difficulty);
        if (referenceXp is > 0)
            _devLog.Log("Catalog", $"Reference XP for {_activeRun.Difficulty}: {referenceXp.Value}");

        SetStatus(TrackingPhase.Tracking, $"Tracking started — {FormatRunLabel(_activeRun)}");
        ActiveRunChanged?.Invoke();
    }

    private RunKind ClassifyQuest(uint questDid)
    {
        if (questDid == 0)
            return RunKind.Dungeon;

        try
        {
            var properties = _provider.PropertyMaster.GetPropertyCollection(questDid);
            if (properties == null)
                return RunKind.Dungeon;

            return ReadBoolProperty(properties, DdoProperty.Quest_IsAdventureArea) == true
                ? RunKind.AdventureArea
                : RunKind.Dungeon;
        }
        catch
        {
            return RunKind.Dungeon;
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

    private double ElapsedRunSeconds()
    {
        return _activeRun?.GetElapsedSeconds(DateTime.UtcNow) ?? 0;
    }


    private void CompleteActiveRun(CompletionReason reason)
    {
        if (_activeRun == null)
            return;

        // Bank any open in-instance segment before computing duration.
        if (!_activeRun.IsTimerPaused)
            PauseRunTimer();

        var completedAt = DateTime.UtcNow;
        var duration = TimeSpan.FromSeconds(_activeRun.AccumulatedActiveSeconds);
        var xp = ResolveCompletionXp();

        var difficulty = ResolveCompletionDifficulty();
        if (QuestLevelResolver.IsUnknownDifficulty(difficulty) && _activeRun != null)
            difficulty = _activeRun.Difficulty;

        var outcome = ResolveRunOutcome(reason);
        var record = new QuestRunRecord
        {
            QuestDid = _activeRun.QuestDid,
            QuestName = _activeRun.QuestName,
            RunKind = _activeRun.RunKind,
            Difficulty = difficulty,
            CharacterName = _activeRun.CharacterName,
            ServerName = _activeRun.ServerName,
            StartedAtUtc = _activeRun.StartedAtUtc,
            CompletedAtUtc = completedAt,
            DurationSeconds = duration.TotalSeconds,
            XpHeroic = xp.Heroic,
            XpEpic = xp.Epic,
            XpLegendary = xp.Legendary,
            XpReaper = xp.Reaper,
            XpTotal = xp.Total,
            XpPerMinute = xp.PerMinute(duration.TotalSeconds),
            Outcome = outcome
        };

        var inferredDifficulty = QuestCatalog.InferRunDifficultyFromXp(
            record.QuestName,
            xp.Heroic,
            xp.Epic,
            xp.Legendary);

        if (!string.IsNullOrWhiteSpace(inferredDifficulty)
            && (QuestLevelResolver.IsUnknownDifficulty(record.Difficulty)
                || string.Equals(record.Difficulty, "Normal", StringComparison.OrdinalIgnoreCase)
                || QuestDifficultyResolver.ShouldReplaceDifficulty(record.Difficulty, inferredDifficulty)))
        {
            _devLog.LogChange("Difficulty", "XpInfer", record.Difficulty, inferredDifficulty);
            record.Difficulty = inferredDifficulty!;
        }

        RefineRunDifficulty(record, xp.Heroic, xp.Epic, xp.Legendary);

        // XP is authoritative when live props never arrived (Unknown) or only sticky Normal.
        if ((QuestLevelResolver.IsUnknownDifficulty(record.Difficulty)
                || string.Equals(record.Difficulty, "Normal", StringComparison.OrdinalIgnoreCase))
            && xp.Total > 0)
        {
            var inferredFromXp = QuestCatalog.InferRunDifficultyFromXp(
                record.QuestName,
                xp.Heroic,
                xp.Epic,
                xp.Legendary);
            if (!string.IsNullOrWhiteSpace(inferredFromXp)
                && !string.Equals(record.Difficulty, inferredFromXp, StringComparison.OrdinalIgnoreCase))
            {
                record.Difficulty = inferredFromXp!;
                _devLog.Log("Difficulty", $"Inferred from XP amount: {record.Difficulty}");
            }
        }

        _devLog.Log(
            "Run",
            $"{outcome} {record.QuestName} via {reason}: {FormatDuration(duration)}, adventure={xp.Total} XP, reaper={xp.Reaper}, difficulty={record.Difficulty}");

        var catalogReferenceXp = QuestCatalog.TryGetReferenceXp(record.QuestName, record.Difficulty);
        if (catalogReferenceXp is > 0)
            _devLog.Log("Catalog", $"Reference XP for {record.Difficulty}: {catalogReferenceXp.Value} (measured {xp.Total})");

        var completedQuestDid = record.QuestDid;
        var shouldBlockAutoStart = record.XpTotal > 0 || record.DurationSeconds >= MinimumDungeonRunSeconds;
        _history.AddRun(record);
        ResetRunState();
        if (shouldBlockAutoStart)
            BlockAutoStart(completedQuestDid);
        else
            _devLog.Log("AutoStart", $"Skipped auto-start block after empty/short run of 0x{completedQuestDid:X8}");

        var awardSummary = xp.Reaper > 0
            ? $"{xp.Total:N0} XP + {xp.Reaper:N0} reaper"
            : $"{xp.Total:N0} XP";
        SetStatus(
            TrackingPhase.Completed,
            $"Run saved — {FormatRunLabel(record)} ({FormatDuration(duration)}) · {outcome} · {awardSummary} ({record.XpPerMinute:N0}/min)");

        ActiveRunChanged?.Invoke();
        RunCompleted?.Invoke(record);
    }

    private string ResolveCompletionDifficulty()
    {
        var baseLevel = _activeRun?.BaseQuestLevel
            ?? QuestCatalog.FindEntry(_activeRun?.QuestName ?? string.Empty)?.Level;
        var resolved = QuestDifficultyResolver.ResolveForInstance(_provider, baseLevel);

        if (!QuestLevelResolver.IsUnknownDifficulty(resolved))
            return resolved;

        if (!QuestLevelResolver.IsUnknownDifficulty(_activeRun?.Difficulty))
            return _activeRun!.Difficulty;

        return "Unknown";
    }

    private void TryRefreshActiveDifficulty()
    {
        if (_activeRun == null)
            return;

        var previousDifficulty = _activeRun.Difficulty;
        var previousBase = _activeRun.BaseQuestLevel;

        var liveXp = CurrentXpBreakdown;
        if (liveXp.Total > 0)
            RefineRunDifficulty(_activeRun, liveXp.Heroic, liveXp.Epic, liveXp.Legendary);
        else
            RefineRunDifficulty(_activeRun);

        if (_activeRun.Difficulty != previousDifficulty || _activeRun.BaseQuestLevel != previousBase)
        {
            _devLog.LogChange("Difficulty", _activeRun.QuestName, previousDifficulty, _activeRun.Difficulty);
            _devLog.Log(
                "Difficulty",
                QuestDifficultyResolver.DescribeResolution(_provider, _activeRun.BaseQuestLevel));
            ActiveRunChanged?.Invoke();
        }
        else if (QuestLevelResolver.IsUnknownDifficulty(_activeRun.Difficulty)
            && (_lastUnknownDifficultyLogUtc == null
                || (DateTime.UtcNow - _lastUnknownDifficultyLogUtc.Value).TotalSeconds >= 5))
        {
            _lastUnknownDifficultyLogUtc = DateTime.UtcNow;
            _devLog.Log(
                "Difficulty",
                $"Still Unknown — {QuestDifficultyResolver.DescribeResolution(_provider, _activeRun.BaseQuestLevel)}");
            _devLog.Log(
                "Difficulty",
                $"Server difficulty scan: {QuestDifficultyResolver.ScanServerDifficultyProps(_provider)}");
        }
    }

    private void NoteWorldDifficultySnapshot(IPropertyCollection? props)
    {
        if (props?.Properties == null || props.Properties.Count == 0)
        {
            _devLog.Log("Difficulty", "WorldProperties snapshot empty");
            return;
        }

        var hits = new List<string>();
        foreach (var property in props.Properties.Values)
        {
            if (!IsDungeonDifficultyProperty(property))
                continue;

            var label = QuestDifficultyResolver.TryReadDifficultyFromProperty(property)
                ?? (property.RawUInt32 != 0 ? $"0x{property.RawUInt32:X8}" : null)
                ?? property.Value?.ToString()
                ?? "?";
            hits.Add($"0x{property.PropertyId:X8}={label}");
        }

        _devLog.Log(
            "Difficulty",
            hits.Count == 0
                ? $"WorldProperties snapshot count={props.Properties.Count} (no dungeon difficulty props)"
                : $"WorldProperties snapshot: {string.Join(", ", hits)}");
    }

    private static bool IsDungeonDifficultyProperty(IProperty? property)
    {
        if (property == null)
            return false;

        var id = property.PropertyId;
        return id == (uint)DdoProperty.Inventory_DungeonDifficulty
            || id == (uint)DdoProperty.Dungeon_UI_DifficultyName
            || id == (uint)DdoProperty.Dungeon_CachedDifficulty
            || id == (uint)DdoProperty.Dungeon_DifficultyLevel
            || id == (uint)DdoProperty.Dungeon_EasyDifficulty
            || id == (uint)DdoProperty.Dungeon_MediumDifficulty
            || id == (uint)DdoProperty.Dungeon_HardDifficulty
            || id == (uint)DdoProperty.Dungeon_ReaperDifficulty
            || QuestDifficultyResolver.MapPropertyId(id) != null;
    }

    private void RefineRunDifficulty(ActiveQuestRun run)
    {
        RefineRunDifficulty(run, 0, 0, 0);
    }

    private void RefineRunDifficulty(QuestRunRecord record, long xpHeroic, long xpEpic, long xpLegendary)
    {
        var run = new ActiveQuestRun
        {
            QuestDid = record.QuestDid,
            QuestName = record.QuestName,
            Difficulty = record.Difficulty,
            RunKind = record.RunKind,
            CharacterName = record.CharacterName,
            ServerName = record.ServerName,
            StartedAtUtc = record.StartedAtUtc
        };

        RefineRunDifficulty(run, xpHeroic, xpEpic, xpLegendary);

        record.Difficulty = run.Difficulty;
        record.QuestTier = run.QuestTier;
        record.BaseQuestLevel = run.BaseQuestLevel;
        record.EffectiveQuestLevel = run.EffectiveQuestLevel;
    }

    private void RefineRunDifficulty(ActiveQuestRun run, long xpHeroic, long xpEpic, long xpLegendary)
    {
        var difficulty = run.Difficulty;
        var baseLevel = run.BaseQuestLevel
            ?? QuestCatalog.FindEntry(run.QuestName)?.Level;

        var resolved = QuestDifficultyResolver.ResolveForInstance(_provider, baseLevel);
        if (!QuestLevelResolver.IsUnknownDifficulty(resolved)
            && (QuestDifficultyResolver.ShouldReplaceDifficulty(difficulty, resolved)
                || QuestDifficultyResolver.IsRemakeMismatch(difficulty, run.QuestTier)
                || QuestLevelResolver.IsUnknownDifficulty(difficulty)))
            difficulty = resolved;

        // Per-quest portal selection (Hard→—→zone). Consumed so the next entry must re-arm.
        if (QuestLevelResolver.IsUnknownDifficulty(difficulty))
        {
            var armed = TryGetArmedPortalDifficulty(run.QuestDid);
            if (!string.IsNullOrWhiteSpace(armed))
            {
                difficulty = armed!;
                _devLog.Log(
                    "Difficulty",
                    $"Applied armed portal difficulty {armed} for 0x{run.QuestDid:X8}");
                ClearArmedPortalDifficulty(run.QuestDid);
            }
        }

        run.Difficulty = difficulty;
        ApplyQuestLevels(run);

        difficulty = QuestDifficultyResolver.ClampToQuestTier(difficulty, run.QuestTier);
        run.Difficulty = difficulty;

        // XP inference is completion-time fallback only when volume never arrived.
        if ((xpHeroic > 0 || xpEpic > 0 || xpLegendary > 0)
            && QuestLevelResolver.IsUnknownDifficulty(difficulty))
        {
            var fromXp = QuestCatalog.InferRunDifficultyFromXp(run.QuestName, xpHeroic, xpEpic, xpLegendary);
            if (!string.IsNullOrWhiteSpace(fromXp))
                difficulty = fromXp!;

            run.Difficulty = difficulty;
        }

        ApplyQuestLevels(run);
        run.Difficulty = QuestDifficultyResolver.ClampToQuestTier(run.Difficulty, run.QuestTier);
    }


    private void ApplyQuestLevels(ActiveQuestRun run)
    {
        var characterLevel = _provider.GetCurrentCharacter()?.PropertyCollection
            ?.GetInt32PropertyValue((uint)DdoProperty.Character_TotalLevel) ?? 0;

        var start = GetRunStartXpSnapshot();
        var end = XpTracker.Capture(_provider);
        long xpHeroic = 0, xpEpic = 0, xpLegendary = 0;
        if (start != null)
        {
            xpHeroic = Math.Max(0, end.Heroic - start.Heroic);
            xpEpic = Math.Max(0, end.Epic - start.Epic);
            xpLegendary = Math.Max(0, end.Legendary - start.Legendary);
        }

        var (baseLevel, effectiveLevel, questTier) = QuestCatalog.ResolveLevels(
            run.QuestName,
            run.Difficulty,
            xpHeroic,
            xpEpic,
            xpLegendary,
            characterLevel);

        var instanceLevel = QuestDifficultyResolver.TryReadInstanceEffectiveLevel(_provider);
        run.QuestTier = questTier;
        run.BaseQuestLevel = baseLevel;
        run.EffectiveQuestLevel = effectiveLevel ?? instanceLevel;
    }


    private void ResetActiveRun()
    {
        ResetRunState();
        _instanceEntryWatchStartedUtc = null;
        SetStatus(TrackingPhase.Idle, "Waiting for instance entry");
        ActiveRunChanged?.Invoke();
    }

    private void ResetRunState()
    {
        _activeRun = null;
        _startXpSnapshot = null;
        _dungeonEventXpHint = 0;
        _objectiveSeenDuringRun = false;
        _lastObjectiveDidDuringRun = null;
        _objectiveClearPolls = 0;
        _instanceAbsentPolls = 0;
        _experienceReportCache.Clear();
    }

    public void AcknowledgeCompletedStatus()
    {
        if (_status.Phase != TrackingPhase.Completed)
            return;

        SetStatus(TrackingPhase.Idle, "Waiting for instance entry");
    }

    private void SetStatus(TrackingPhase phase, string message)
    {
        if (_status.Phase == phase && _status.Message == message)
            return;

        if (_status.Phase != phase)
            _devLog.LogChange("Status", "Phase", _status.Phase, phase);

        _status = new TrackingStatus { Phase = phase, Message = message };
        StatusChanged?.Invoke();
    }

    private static string FormatPortalEvent(string phase, IPortalInfo? portalInfo)
    {
        var diff = PortalDifficultyReader.Describe(portalInfo?.QuestGenericDifficulty);
        if (portalInfo?.AvailableQuests == null || portalInfo.AvailableQuests.Count == 0)
            return $"{phase}: no quests, diff={diff}";

        var quests = string.Join(", ", portalInfo.AvailableQuests.Select(q => $"0x{q:X8}"));
        return $"{phase}: [{quests}] diff={diff}";
    }

    private static string FormatRunLabel(ActiveQuestRun? run)
    {
        if (run == null)
            return "—";

        return FormatRunLabel(run.QuestName, run.RunKind, run.Difficulty, run.BaseQuestLevel, run.EffectiveQuestLevel);
    }

    private static string FormatRunLabel(QuestRunRecord run)
    {
        return FormatRunLabel(run.QuestName, run.RunKind, run.Difficulty, run.BaseQuestLevel, run.EffectiveQuestLevel);
    }

    private static string FormatRunLabel(
        string questName,
        RunKind runKind,
        string difficulty,
        int? baseLevel = null,
        int? effectiveLevel = null)
    {
        var levelLabel = QuestLevelResolver.FormatLevelLabel(baseLevel, effectiveLevel);
        var levelSuffix = levelLabel == "—" ? string.Empty : $" · {levelLabel}";

        if (runKind == RunKind.AdventureArea)
            return $"{questName} (Adventure · {difficulty}{levelSuffix})";

        return $"{questName} ({difficulty}{levelSuffix})";
    }

    private static string FormatQuestDid(uint questDid) => questDid == 0 ? "Unknown Quest" : $"Quest 0x{questDid:X8}";

    private static string FormatDuration(TimeSpan duration)
    {
        return duration.TotalHours >= 1
            ? duration.ToString(@"h\:mm\:ss")
            : duration.ToString(@"mm\:ss");
    }

    private static uint ReadUInt(uint? value) => value is > 0 ? value.Value : 0;

    private static string ResolveRunOutcome(CompletionReason reason)
    {
        // Completions only arrive via OnAddAlert.
        return reason == CompletionReason.AlertCompleted ? "Completed" : "Abandoned";
    }

    private enum CompletionReason
    {
        AlertCompleted
    }

    public void Dispose()
    {
        _pollTimer.Dispose();
    }
}

