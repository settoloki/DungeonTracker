using DungeonTracker.Services;
using VoK.Sdk;
using VoK.Sdk.Ddo;
using VoK.Sdk.Plugins;

namespace DungeonTracker;

public sealed class DungeonTrackerPlugin : IDdoPlugin
{
    // TODO: Request unique PluginId and PluginKey from Morrikan (Dungeon Helper team).
    private static readonly Guid PluginGuid = Guid.Parse("8f4e2c19-6b7a-4d3e-9c21-a5d8f0e3b712");
    private const string PluginKeyValue = "PLACEHOLDER-REQUEST-FROM-MORRIKAN";

    private DungeonTrackerUI? _ui;
    private QuestTrackerService? _tracker;
    private DdoTrackerSyncService? _cloudSync;

    public Guid PluginId => PluginGuid;

    public GameId Game => GameId.DDO;

    public string PluginKey => PluginKeyValue;

    public string Name => "Dungeon Tracker";

    public string Description => "Tracks your active quest and how long each run took to complete.";

    public string Author => "tom";

    public Version Version => GetType().Assembly.GetName().Version ?? new Version(1, 0, 0);

    public IPluginUI GetPluginUI() => _ui!;

    public void Initialize(IDdoGameDataProvider gameDataProvider, string folder)
    {
        _tracker = new QuestTrackerService(gameDataProvider, folder, this);
        _cloudSync = new DdoTrackerSyncService(folder, gameDataProvider, plugin: this);
        _cloudSync.AttachQuestCatalog(_tracker.QuestCatalog);
        _tracker.CharacterContextChanged += OnCharacterContextChanged;
        _ui = new DungeonTrackerUI(gameDataProvider, _tracker, _cloudSync);
        _ = _cloudSync.InitializeAsync();
    }

    private void OnCharacterContextChanged()
    {
        _cloudSync?.BindToCurrentGameCharacter();
    }

    public void Terminate()
    {
        if (_tracker != null)
            _tracker.CharacterContextChanged -= OnCharacterContextChanged;

        _ui?.Terminate();
        _tracker?.Dispose();
        _cloudSync = null;
    }
}
