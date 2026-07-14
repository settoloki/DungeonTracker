#nullable enable
namespace DungeonTracker;

partial class IngameUI
{
    private System.ComponentModel.IContainer? components = null;

    protected override void Dispose(bool disposing)
    {
        if (disposing)
        {
            _elapsedTimer.Stop();
            _elapsedTimer.Dispose();
            _completedFlashTimer.Stop();
            _completedFlashTimer.Dispose();
            _tracker.ActiveRunChanged -= OnTrackerChanged;
            _tracker.RunCompleted -= OnRunCompleted;
            _tracker.StatusChanged -= OnStatusChanged;
            _tracker.DebugSnapshotChanged -= OnDebugSnapshotChanged;
            _tracker.HistoryChanged -= OnHistoryChanged;
            if (_cloudSync != null)
                _cloudSync.StateChanged -= OnCloudStateChanged;
            components?.Dispose();
        }

        base.Dispose(disposing);
    }

    private void InitializeComponent()
    {
        lblHeader = new Label();
        pnlStatus = new Panel();
        pnlStatusDot = new Panel();
        lblStatusMessage = new Label();
        pnlActiveRun = new Panel();
        lblActiveRunCaption = new Label();
        lblActiveRunValue = new Label();
        lblXpValue = new Label();
        lblElapsedValue = new Label();
        pnlContent = new Panel();
        pnlDebug = new Panel();
        txtDebug = new TextBox();
        lblDebugCaption = new Label();
        pnlHistory = new Panel();
        lblHistory = new Label();
        lvHistory = new ListView();
        colQuest = new ColumnHeader();
        colLevel = new ColumnHeader();
        colDifficulty = new ColumnHeader();
        colDuration = new ColumnHeader();
        colXp = new ColumnHeader();
        colXpPerMin = new ColumnHeader();
        pnlCloud = new Panel();
        lblCloudStatus = new Label();
        cmbCloudCharacter = new ComboBox();
        btnCloudConfigure = new Button();
        pnlFooter = new Panel();
        btnToggleDebug = new Button();
        btnStopTracking = new Button();
        btnClearHistory = new Button();
        pnlStatus.SuspendLayout();
        pnlActiveRun.SuspendLayout();
        pnlCloud.SuspendLayout();
        pnlContent.SuspendLayout();
        pnlDebug.SuspendLayout();
        pnlHistory.SuspendLayout();
        pnlFooter.SuspendLayout();
        SuspendLayout();

        lblHeader.Dock = DockStyle.Top;
        lblHeader.Font = new Font("Segoe UI", 15F, FontStyle.Bold, GraphicsUnit.Point);
        lblHeader.ForeColor = Color.Gainsboro;
        lblHeader.Name = "lblHeader";
        lblHeader.Padding = new Padding(12, 10, 12, 4);
        lblHeader.Size = new Size(520, 42);
        lblHeader.TabIndex = 0;
        lblHeader.Text = "Quest Tracker";

        pnlStatus.Controls.Add(lblStatusMessage);
        pnlStatus.Controls.Add(pnlStatusDot);
        pnlStatus.Dock = DockStyle.Top;
        pnlStatus.Name = "pnlStatus";
        pnlStatus.Padding = new Padding(12, 6, 12, 6);
        pnlStatus.Size = new Size(520, 34);
        pnlStatus.TabIndex = 1;

        pnlStatusDot.Location = new Point(12, 9);
        pnlStatusDot.Name = "pnlStatusDot";
        pnlStatusDot.Size = new Size(12, 12);
        pnlStatusDot.TabIndex = 0;

        lblStatusMessage.AutoSize = true;
        lblStatusMessage.ForeColor = Color.FromArgb(170, 170, 170);
        lblStatusMessage.Location = new Point(30, 7);
        lblStatusMessage.Name = "lblStatusMessage";
        lblStatusMessage.Size = new Size(160, 15);
        lblStatusMessage.TabIndex = 1;
        lblStatusMessage.Text = "Waiting for instance entry";

        pnlActiveRun.Controls.Add(lblXpValue);
        pnlActiveRun.Controls.Add(lblElapsedValue);
        pnlActiveRun.Controls.Add(lblActiveRunValue);
        pnlActiveRun.Controls.Add(lblActiveRunCaption);
        pnlActiveRun.Dock = DockStyle.Top;
        pnlActiveRun.Name = "pnlActiveRun";
        pnlActiveRun.Padding = new Padding(12, 0, 12, 8);
        pnlActiveRun.Size = new Size(520, 76);
        pnlActiveRun.TabIndex = 2;

        lblActiveRunCaption.AutoSize = true;
        lblActiveRunCaption.ForeColor = Color.Silver;
        lblActiveRunCaption.Location = new Point(12, 4);
        lblActiveRunCaption.Name = "lblActiveRunCaption";
        lblActiveRunCaption.Size = new Size(68, 15);
        lblActiveRunCaption.TabIndex = 0;
        lblActiveRunCaption.Text = "Current Run";

        lblActiveRunValue.AutoSize = true;
        lblActiveRunValue.Font = new Font("Segoe UI", 10F, FontStyle.Bold, GraphicsUnit.Point);
        lblActiveRunValue.ForeColor = Color.White;
        lblActiveRunValue.Location = new Point(12, 22);
        lblActiveRunValue.MaximumSize = new Size(360, 0);
        lblActiveRunValue.Name = "lblActiveRunValue";
        lblActiveRunValue.Size = new Size(145, 19);
        lblActiveRunValue.TabIndex = 1;
        lblActiveRunValue.Text = "No active run";

        lblXpValue.AutoSize = true;
        lblXpValue.ForeColor = Color.FromArgb(180, 200, 230);
        lblXpValue.Location = new Point(12, 44);
        lblXpValue.MaximumSize = new Size(470, 0);
        lblXpValue.Name = "lblXpValue";
        lblXpValue.Size = new Size(45, 15);
        lblXpValue.TabIndex = 3;
        lblXpValue.Text = "XP: —";

        lblElapsedValue.Anchor = AnchorStyles.Top | AnchorStyles.Right;
        lblElapsedValue.Font = new Font("Consolas", 12F, FontStyle.Bold, GraphicsUnit.Point);
        lblElapsedValue.ForeColor = Color.FromArgb(230, 210, 120);
        lblElapsedValue.Location = new Point(400, 22);
        lblElapsedValue.Name = "lblElapsedValue";
        lblElapsedValue.Size = new Size(100, 22);
        lblElapsedValue.TabIndex = 2;
        lblElapsedValue.Text = "--:--";
        lblElapsedValue.TextAlign = ContentAlignment.MiddleRight;

        pnlCloud.Controls.Add(btnCloudConfigure);
        pnlCloud.Controls.Add(cmbCloudCharacter);
        pnlCloud.Controls.Add(lblCloudStatus);
        pnlCloud.Dock = DockStyle.Top;
        pnlCloud.Name = "pnlCloud";
        pnlCloud.Padding = new Padding(12, 0, 12, 6);
        pnlCloud.Size = new Size(520, 52);
        pnlCloud.TabIndex = 3;

        lblCloudStatus.AutoSize = true;
        lblCloudStatus.ForeColor = Color.FromArgb(170, 185, 210);
        lblCloudStatus.Location = new Point(12, 4);
        lblCloudStatus.Name = "lblCloudStatus";
        lblCloudStatus.Size = new Size(180, 15);
        lblCloudStatus.TabIndex = 0;
        lblCloudStatus.Text = "DDO Tracker: signed out";

        cmbCloudCharacter.DropDownStyle = ComboBoxStyle.DropDownList;
        cmbCloudCharacter.FormattingEnabled = true;
        cmbCloudCharacter.Location = new Point(12, 24);
        cmbCloudCharacter.Name = "cmbCloudCharacter";
        cmbCloudCharacter.Size = new Size(300, 23);
        cmbCloudCharacter.TabIndex = 1;
        cmbCloudCharacter.Visible = false;
        cmbCloudCharacter.SelectedIndexChanged += cmbCloudCharacter_SelectedIndexChanged;

        btnCloudConfigure.Anchor = AnchorStyles.Top | AnchorStyles.Right;
        btnCloudConfigure.Location = new Point(400, 22);
        btnCloudConfigure.Name = "btnCloudConfigure";
        btnCloudConfigure.Size = new Size(108, 23);
        btnCloudConfigure.TabIndex = 2;
        btnCloudConfigure.Text = "Configure…";
        btnCloudConfigure.UseVisualStyleBackColor = true;
        btnCloudConfigure.Click += btnCloudConfigure_Click;

        pnlContent.Controls.Add(pnlDebug);
        pnlContent.Controls.Add(pnlHistory);
        pnlContent.Dock = DockStyle.Fill;
        pnlContent.Name = "pnlContent";
        pnlContent.TabIndex = 4;

        pnlDebug.Controls.Add(txtDebug);
        pnlDebug.Controls.Add(lblDebugCaption);
        pnlDebug.Dock = DockStyle.Fill;
        pnlDebug.Name = "pnlDebug";
        pnlDebug.Padding = new Padding(12, 0, 12, 0);
        pnlDebug.TabIndex = 6;
        pnlDebug.Visible = false;

        lblDebugCaption.Dock = DockStyle.Top;
        lblDebugCaption.ForeColor = Color.FromArgb(180, 190, 210);
        lblDebugCaption.Name = "lblDebugCaption";
        lblDebugCaption.Padding = new Padding(0, 0, 0, 4);
        lblDebugCaption.Size = new Size(496, 19);
        lblDebugCaption.TabIndex = 0;
        lblDebugCaption.Text = "SDK Debug (temporary)";

        txtDebug.BackColor = Color.FromArgb(18, 20, 26);
        txtDebug.BorderStyle = BorderStyle.FixedSingle;
        txtDebug.Dock = DockStyle.Fill;
        txtDebug.Font = new Font("Consolas", 8F, FontStyle.Regular, GraphicsUnit.Point);
        txtDebug.ForeColor = Color.FromArgb(190, 200, 215);
        txtDebug.Multiline = true;
        txtDebug.Name = "txtDebug";
        txtDebug.HideSelection = true;
        txtDebug.ReadOnly = true;
        txtDebug.ScrollBars = ScrollBars.Vertical;
        txtDebug.TabIndex = 1;
        txtDebug.WordWrap = true;

        pnlHistory.Controls.Add(lvHistory);
        pnlHistory.Controls.Add(lblHistory);
        pnlHistory.Dock = DockStyle.Fill;
        pnlHistory.Name = "pnlHistory";
        pnlHistory.Padding = new Padding(12, 0, 12, 0);
        pnlHistory.TabIndex = 0;

        lblHistory.Dock = DockStyle.Top;
        lblHistory.ForeColor = Color.Silver;
        lblHistory.Name = "lblHistory";
        lblHistory.Padding = new Padding(0, 0, 0, 4);
        lblHistory.Size = new Size(416, 19);
        lblHistory.TabIndex = 0;
        lblHistory.Text = "Completed Runs";

        lvHistory.BorderStyle = BorderStyle.FixedSingle;
        lvHistory.Columns.AddRange(new[] { colQuest, colLevel, colDifficulty, colDuration, colXp, colXpPerMin });
        lvHistory.Dock = DockStyle.Fill;
        lvHistory.FullRowSelect = true;
        lvHistory.GridLines = true;
        lvHistory.HeaderStyle = ColumnHeaderStyle.Nonclickable;
        lvHistory.MultiSelect = false;
        lvHistory.Name = "lvHistory";
        lvHistory.UseCompatibleStateImageBehavior = false;
        lvHistory.View = View.Details;

        colQuest.Text = "Area";
        colQuest.Width = 145;
        colLevel.Text = "Lvl";
        colLevel.Width = 52;
        colDifficulty.Text = "Diff";
        colDifficulty.Width = 55;
        colDuration.Text = "Time";
        colDuration.Width = 55;
        colXp.Text = "XP";
        colXp.Width = 70;
        colXpPerMin.Text = "XP/min";
        colXpPerMin.Width = 60;

        pnlFooter.Controls.Add(btnClearHistory);
        pnlFooter.Controls.Add(btnStopTracking);
        pnlFooter.Controls.Add(btnToggleDebug);
        pnlFooter.Dock = DockStyle.Bottom;
        pnlFooter.Name = "pnlFooter";
        pnlFooter.Padding = new Padding(12, 6, 12, 6);
        pnlFooter.Size = new Size(520, 36);
        pnlFooter.TabIndex = 4;

        btnToggleDebug.Location = new Point(12, 6);
        btnToggleDebug.Name = "btnToggleDebug";
        btnToggleDebug.Size = new Size(95, 23);
        btnToggleDebug.TabIndex = 0;
        btnToggleDebug.Text = "Show Debug";
        btnToggleDebug.UseVisualStyleBackColor = true;
        btnToggleDebug.Click += btnToggleDebug_Click;

        btnStopTracking.Location = new Point(113, 6);
        btnStopTracking.Name = "btnStopTracking";
        btnStopTracking.Size = new Size(105, 23);
        btnStopTracking.TabIndex = 1;
        btnStopTracking.Text = "Stop Tracking";
        btnStopTracking.UseVisualStyleBackColor = true;
        btnStopTracking.Enabled = false;
        btnStopTracking.Click += btnStopTracking_Click;

        btnClearHistory.Anchor = AnchorStyles.Top | AnchorStyles.Right;
        btnClearHistory.Location = new Point(433, 6);
        btnClearHistory.Name = "btnClearHistory";
        btnClearHistory.Size = new Size(75, 23);
        btnClearHistory.TabIndex = 2;
        btnClearHistory.Text = "Clear";
        btnClearHistory.UseVisualStyleBackColor = true;
        btnClearHistory.Click += btnClearHistory_Click;

        AutoScaleDimensions = new SizeF(7F, 15F);
        AutoScaleMode = AutoScaleMode.Font;
        BackColor = Color.FromArgb(28, 32, 40);
        ClientSize = new Size(520, FormHeight);
        Controls.Add(pnlContent);
        Controls.Add(pnlFooter);
        Controls.Add(pnlCloud);
        Controls.Add(pnlActiveRun);
        Controls.Add(pnlStatus);
        Controls.Add(lblHeader);
        DoubleBuffered = true;
        FormBorderStyle = FormBorderStyle.None;
        MinimumSize = new Size(520, FormHeight);
        Name = "IngameUI";
        ShowIcon = false;
        ShowInTaskbar = false;
        Text = "Quest Tracker";
        pnlStatus.ResumeLayout(false);
        pnlStatus.PerformLayout();
        pnlActiveRun.ResumeLayout(false);
        pnlActiveRun.PerformLayout();
        pnlCloud.ResumeLayout(false);
        pnlCloud.PerformLayout();
        pnlContent.ResumeLayout(false);
        pnlDebug.ResumeLayout(false);
        pnlDebug.PerformLayout();
        pnlHistory.ResumeLayout(false);
        pnlFooter.ResumeLayout(false);
        ResumeLayout(false);
    }

    private Label lblHeader;
    private Panel pnlStatus;
    private Panel pnlStatusDot;
    private Label lblStatusMessage;
    private Panel pnlActiveRun;
    private Label lblActiveRunCaption;
    private Label lblActiveRunValue;
    private Label lblXpValue;
    private Label lblElapsedValue;
    private Panel pnlCloud;
    private Label lblCloudStatus;
    private ComboBox cmbCloudCharacter;
    private Button btnCloudConfigure;
    private Panel pnlContent;
    private Panel pnlDebug;
    private Label lblDebugCaption;
    private TextBox txtDebug;
    private Panel pnlHistory;
    private Label lblHistory;
    private ListView lvHistory;
    private ColumnHeader colQuest;
    private ColumnHeader colLevel;
    private ColumnHeader colDifficulty;
    private ColumnHeader colDuration;
    private ColumnHeader colXp;
    private ColumnHeader colXpPerMin;
    private Panel pnlFooter;
    private Button btnToggleDebug;
    private Button btnStopTracking;
    private Button btnClearHistory;
}
