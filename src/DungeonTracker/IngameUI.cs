using System.Runtime.InteropServices;
using DungeonTracker.Models;
using DungeonTracker.Services;

namespace DungeonTracker;

public sealed partial class IngameUI : Form
{
    private const int FormHeight = 462;
    private const int EmGetFirstVisibleLine = 0x00CE;
    private const int EmLineScroll = 0x00B6;

    [DllImport("user32.dll", CharSet = CharSet.Auto)]
    private static extern IntPtr SendMessage(IntPtr hWnd, int msg, IntPtr wParam, IntPtr lParam);

    private readonly QuestTrackerService _tracker;
    private readonly DdoTrackerSyncService _cloudSync;
    private readonly System.Windows.Forms.Timer _elapsedTimer;
    private readonly System.Windows.Forms.Timer _completedFlashTimer;
    private bool _debugVisible;
    private bool _uiInitialized;
    private bool _suppressCloudCharacterEvents;

    public IngameUI(QuestTrackerService tracker, DdoTrackerSyncService cloudSync)
    {
        _tracker = tracker;
        _cloudSync = cloudSync;
        InitializeComponent();

        _elapsedTimer = new System.Windows.Forms.Timer { Interval = 250 };
        _elapsedTimer.Tick += (_, _) => OnElapsedTick();

        _completedFlashTimer = new System.Windows.Forms.Timer { Interval = 8000 };
        _completedFlashTimer.Tick += (_, _) =>
        {
            _completedFlashTimer.Stop();
            _tracker.AcknowledgeCompletedStatus();
        };

        _tracker.ActiveRunChanged += OnTrackerChanged;
        _tracker.RunCompleted += OnRunCompleted;
        _tracker.StatusChanged += OnStatusChanged;
        _tracker.DebugSnapshotChanged += OnDebugSnapshotChanged;
        _tracker.HistoryChanged += OnHistoryChanged;
        _cloudSync.StateChanged += OnCloudStateChanged;

        pnlStatusDot.Paint += (_, e) =>
        {
            e.Graphics.SmoothingMode = System.Drawing.Drawing2D.SmoothingMode.AntiAlias;
            var color = GetStatusColor(_tracker.Status.Phase);
            using var brush = new SolidBrush(color);
            e.Graphics.FillEllipse(brush, 1, 1, pnlStatusDot.Width - 2, pnlStatusDot.Height - 2);
        };

        Load += (_, _) => RunOnUiThread(EnsureUiInitialized);
        Shown += (_, _) => RunOnUiThread(EnsureUiInitialized);
    }

    private void OnElapsedTick()
    {
        _tracker.RefreshNow();
        UpdateActiveRunDisplay();
    }

    private void EnsureUiInitialized()
    {
        if (_uiInitialized)
            return;

        _uiInitialized = true;
        InitializeUiState();
    }

    private void RunOnUiThread(Action action)
    {
        if (InvokeRequired)
        {
            BeginInvoke(action);
            return;
        }

        action();
    }

    private void InitializeUiState()
    {
        RefreshHistory();
        UpdateActiveRunDisplay();
        UpdateStatusDisplay();
        UpdateStopTrackingButton();
        UpdateCloudDisplay();
        UpdateDebugDisplay();
        _elapsedTimer.Start();
        _ = InitializeCloudAsync();
    }

    private async Task InitializeCloudAsync()
    {
        try
        {
            await _cloudSync.InitializeAsync().ConfigureAwait(true);
            _cloudSync.BindToCurrentGameCharacter();
        }
        catch (Exception ex)
        {
            MessageBox.Show(
                this,
                $"Could not restore DDO Tracker session:\n{ex.Message}",
                "DDO Tracker",
                MessageBoxButtons.OK,
                MessageBoxIcon.Warning);
        }
        finally
        {
            RunOnUiThread(UpdateCloudDisplay);
        }
    }

    private void OnCloudStateChanged()
    {
        RunOnUiThread(UpdateCloudDisplay);
    }

    private void UpdateCloudDisplay()
    {
        lblCloudStatus.Text = _cloudSync.StatusMessage;
        btnCloudConfigure.Text = "Configure…";

        _suppressCloudCharacterEvents = true;
        cmbCloudCharacter.Visible = _cloudSync.IsSignedIn;
        cmbCloudCharacter.Items.Clear();

        if (_cloudSync.IsSignedIn)
        {
            foreach (var character in _cloudSync.Characters)
                cmbCloudCharacter.Items.Add(character);

            var selectedId = _cloudSync.SelectedCharacterId;
            if (!string.IsNullOrWhiteSpace(selectedId))
            {
                for (var i = 0; i < cmbCloudCharacter.Items.Count; i++)
                {
                    if (cmbCloudCharacter.Items[i] is Models.DdoTrackerCharacter item
                        && string.Equals(item.Id, selectedId, StringComparison.OrdinalIgnoreCase))
                    {
                        cmbCloudCharacter.SelectedIndex = i;
                        break;
                    }
                }
            }
        }

        _suppressCloudCharacterEvents = false;
    }

    private void btnCloudConfigure_Click(object? sender, EventArgs e)
    {
        using var dialog = new DdoTrackerConfigForm(_cloudSync);
        dialog.ShowDialog(this);
        UpdateCloudDisplay();
    }

    private void cmbCloudCharacter_SelectedIndexChanged(object? sender, EventArgs e)
    {
        if (_suppressCloudCharacterEvents)
            return;

        if (cmbCloudCharacter.SelectedItem is Models.DdoTrackerCharacter character)
            _cloudSync.SelectCharacter(character.Id);
    }

    private void OnTrackerChanged()
    {
        if (InvokeRequired)
        {
            BeginInvoke(OnTrackerChanged);
            return;
        }

        UpdateActiveRunDisplay();
        UpdateStatusDisplay();
        UpdateStopTrackingButton();
    }

    private void OnStatusChanged()
    {
        if (InvokeRequired)
        {
            BeginInvoke(OnStatusChanged);
            return;
        }

        UpdateStatusDisplay();
        UpdateStopTrackingButton();
    }

    private void OnDebugSnapshotChanged()
    {
        if (InvokeRequired)
        {
            BeginInvoke(OnDebugSnapshotChanged);
            return;
        }

        if (_debugVisible)
            UpdateDebugDisplay();
    }

    private void OnHistoryChanged()
    {
        if (InvokeRequired)
        {
            BeginInvoke(OnHistoryChanged);
            return;
        }

        RefreshHistory();
    }

    private void OnRunCompleted(QuestRunRecord record)
    {
        if (InvokeRequired)
        {
            BeginInvoke(() => OnRunCompleted(record));
            return;
        }

        RefreshHistory();
        UpdateActiveRunDisplay();
        UpdateStatusDisplay();
        UpdateStopTrackingButton();
        _cloudSync.HandleLocalRunCompleted(record);
        _completedFlashTimer.Stop();
        _completedFlashTimer.Start();
    }

    private void UpdateStatusDisplay()
    {
        var status = _tracker.Status;
        lblStatusMessage.Text = status.Message;
        pnlStatusDot.Invalidate();

        pnlStatus.BackColor = status.Phase switch
        {
            TrackingPhase.Tracking => Color.FromArgb(36, 58, 42),
            TrackingPhase.Completed => Color.FromArgb(58, 52, 30),
            _ => Color.FromArgb(34, 38, 46)
        };

        lblStatusMessage.ForeColor = status.Phase switch
        {
            TrackingPhase.Tracking => Color.FromArgb(140, 220, 140),
            TrackingPhase.Completed => Color.FromArgb(230, 210, 120),
            _ => Color.FromArgb(170, 170, 170)
        };
    }

    private void UpdateActiveRunDisplay()
    {
        var active = _tracker.ActiveRun;
        if (active == null)
        {
            lblActiveRunValue.Text = "No active run";
            lblXpValue.Text = "XP: —";
            lblElapsedValue.Text = "--:--";
            lblElapsedValue.ForeColor = Color.FromArgb(120, 120, 120);
            return;
        }

        lblActiveRunValue.Text = FormatActiveRunLabel(active);

        var elapsed = TimeSpan.FromSeconds(_tracker.ActiveRunElapsedSeconds);
        lblElapsedValue.Text = FormatDuration(elapsed);
        lblElapsedValue.ForeColor = _tracker.IsRunTimerPaused
            ? Color.FromArgb(220, 180, 100)
            : Color.FromArgb(140, 220, 140);

        var xp = _tracker.CurrentXpBreakdown;
        var xpPerMinute = xp.PerMinute(elapsed.TotalSeconds);
        lblXpValue.Text = xp.Total > 0
            ? $"XP: {xp.Total:N0} · {xpPerMinute:N0}/min"
            : "XP: 0";
    }

    private void UpdateDebugDisplay()
    {
        if (!_debugVisible)
            return;

        var snapshot = _tracker.DebugSnapshot;
        lblDebugCaption.Text = $"SDK Debug · updated {snapshot.CapturedAtUtc.ToLocalTime():HH:mm:ss}";

        var report = snapshot.FormatReport();
        if (txtDebug.Text == report)
            return;

        var firstVisibleLine = GetFirstVisibleLine(txtDebug);
        txtDebug.Text = report;
        RestoreFirstVisibleLine(txtDebug, firstVisibleLine);
    }

    private static int GetFirstVisibleLine(TextBox textBox)
    {
        if (!textBox.IsHandleCreated)
            return 0;

        return SendMessage(textBox.Handle, EmGetFirstVisibleLine, IntPtr.Zero, IntPtr.Zero).ToInt32();
    }

    private static void RestoreFirstVisibleLine(TextBox textBox, int line)
    {
        if (line <= 0 || !textBox.IsHandleCreated)
            return;

        SendMessage(textBox.Handle, EmLineScroll, IntPtr.Zero, (IntPtr)line);
    }

    private static Color GetStatusColor(TrackingPhase phase)
    {
        return phase switch
        {
            TrackingPhase.Tracking => Color.FromArgb(80, 200, 90),
            TrackingPhase.Completed => Color.FromArgb(230, 210, 120),
            _ => Color.FromArgb(120, 120, 120)
        };
    }

    private void RefreshHistory()
    {
        lvHistory.BeginUpdate();
        lvHistory.Items.Clear();

        foreach (var run in _tracker.History)
        {
            var item = new ListViewItem(FormatHistoryQuestName(run));
            item.SubItems.Add(QuestLevelResolver.FormatLevelLabel(run.BaseQuestLevel, run.EffectiveQuestLevel));
            item.SubItems.Add(run.Difficulty);
            item.SubItems.Add(FormatDuration(TimeSpan.FromSeconds(run.DurationSeconds)));
            item.SubItems.Add(run.XpTotal > 0 ? run.XpTotal.ToString("N0") : "—");
            item.SubItems.Add(run.XpPerMinute > 0 ? run.XpPerMinute.ToString("N0") : "—");
            lvHistory.Items.Add(item);
        }

        lvHistory.EndUpdate();
    }

    private static string FormatActiveRunLabel(ActiveQuestRun run)
    {
        var levelLabel = QuestLevelResolver.FormatLevelLabel(run.BaseQuestLevel, run.EffectiveQuestLevel);
        var levelSuffix = levelLabel == "—" ? string.Empty : $" · {levelLabel}";

        return run.RunKind == RunKind.AdventureArea
            ? $"{run.QuestName} (Adventure · {run.Difficulty}{levelSuffix})"
            : $"{run.QuestName} ({run.Difficulty}{levelSuffix})";
    }

    private static string FormatHistoryQuestName(QuestRunRecord run)
    {
        return run.RunKind == RunKind.AdventureArea
            ? $"{run.QuestName} [Adventure]"
            : run.QuestName;
    }

    private static string FormatDuration(TimeSpan duration)
    {
        return duration.TotalHours >= 1
            ? duration.ToString(@"h\:mm\:ss")
            : duration.ToString(@"mm\:ss");
    }

    private void UpdateStopTrackingButton()
    {
        btnStopTracking.Enabled = _tracker.ActiveRun != null;
    }

    private void btnStopTracking_Click(object? sender, EventArgs e)
    {
        if (_tracker.ActiveRun == null)
            return;

        var questName = _tracker.ActiveRun.QuestName;
        var result = MessageBox.Show(
            this,
            $"Stop tracking \"{questName}\"?\n\nThis cancels the current run without saving a completion.",
            "Stop Tracking",
            MessageBoxButtons.YesNo,
            MessageBoxIcon.Question);

        if (result != DialogResult.Yes)
            return;

        _tracker.StopTracking();
        UpdateActiveRunDisplay();
        UpdateStatusDisplay();
        UpdateStopTrackingButton();
    }

    private void btnToggleDebug_Click(object sender, EventArgs e)
    {
        _debugVisible = !_debugVisible;
        pnlHistory.Visible = !_debugVisible;
        pnlDebug.Visible = _debugVisible;
        btnToggleDebug.Text = _debugVisible ? "Hide Debug" : "Show Debug";

        if (_debugVisible)
        {
            pnlDebug.BringToFront();
            _tracker.RefreshNow();
            UpdateDebugDisplay();
        }
        else
        {
            pnlHistory.BringToFront();
        }

        pnlContent.PerformLayout();
    }

    private void btnClearHistory_Click(object sender, EventArgs e)
    {
        var result = MessageBox.Show(
            this,
            "Clear all saved quest completion times?",
            "Clear History",
            MessageBoxButtons.YesNo,
            MessageBoxIcon.Question);

        if (result != DialogResult.Yes)
            return;

        _tracker.ClearHistory();
        RefreshHistory();
    }
}
