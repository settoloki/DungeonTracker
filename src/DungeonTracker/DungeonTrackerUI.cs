using System.Drawing;
using System.Drawing.Drawing2D;
using DungeonTracker.Services;
using VoK.Sdk.Ddo;
using VoK.Sdk.Plugins;

namespace DungeonTracker;

public sealed class DungeonTrackerUI : IPluginUI
{
    private readonly IngameUI _ingameUi;
    private readonly DdoTrackerSyncService _cloudSync;

    public DungeonTrackerUI(
        IDdoGameDataProvider provider,
        QuestTrackerService tracker,
        DdoTrackerSyncService cloudSync)
    {
        _cloudSync = cloudSync;
        _ingameUi = new IngameUI(tracker, cloudSync);
    }

    public float? FocusedOpacity => 1.0f;

    public bool EnabledInCharacterSelection => false;

    public Image ToolbarImage
    {
        get
        {
            var image = new Bitmap(36, 36);
            using var graphics = Graphics.FromImage(image);
            graphics.SmoothingMode = SmoothingMode.AntiAlias;
            graphics.Clear(Color.FromArgb(24, 36, 58));

            using var outerPen = new Pen(Color.FromArgb(230, 210, 120), 2f);
            using var innerBrush = new SolidBrush(Color.FromArgb(72, 108, 160));
            graphics.DrawEllipse(outerPen, 4, 4, 28, 28);
            graphics.FillEllipse(innerBrush, 10, 10, 16, 16);

            using var handPen = new Pen(Color.FromArgb(230, 210, 120), 2f);
            graphics.DrawLine(handPen, 18, 12, 18, 22);
            graphics.DrawLine(handPen, 18, 22, 14, 18);
            graphics.DrawLine(handPen, 18, 22, 22, 18);

            return image;
        }
    }

    public object UserInterfaceForm => _ingameUi;

    public Tuple<int, int> MinSize => new(520, 462);

    public void Terminate()
    {
        _ingameUi.Dispose();
        _cloudSync.Dispose();
    }
}
