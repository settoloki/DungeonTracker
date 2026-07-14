using System.Diagnostics;
using DungeonTracker.Models;
using DungeonTracker.Services;

namespace DungeonTracker;

public sealed class DdoTrackerConfigForm : Form
{
    private readonly DdoTrackerSyncService _cloudSync;
    private readonly Label _lblAccount;
    private readonly Label _lblStatus;
    private readonly Label _lblGameChars;
    private readonly Label _lblWebsiteChars;
    private readonly Label _lblPending;
    private readonly ComboBox _cmbCharacter;
    private readonly CheckBox _chkAutoSync;
    private readonly Button _btnAccount;
    private readonly Button _btnSyncCharacters;
    private readonly Button _btnFlushPending;
    private readonly Button _btnRefresh;
    private readonly Button _btnOpenSite;
    private readonly Button _btnClose;
    private bool _suppressCharacterEvents;
    private bool _suppressAutoSyncEvents;
    private bool _busy;

    public DdoTrackerConfigForm(DdoTrackerSyncService cloudSync)
    {
        _cloudSync = cloudSync;

        Text = "DDO Tracker settings";
        FormBorderStyle = FormBorderStyle.FixedDialog;
        StartPosition = FormStartPosition.CenterParent;
        MinimizeBox = false;
        MaximizeBox = false;
        ShowInTaskbar = false;
        ClientSize = new Size(440, 360);
        BackColor = Color.FromArgb(28, 32, 40);
        ForeColor = Color.Gainsboro;
        Font = new Font("Segoe UI", 9F, FontStyle.Regular, GraphicsUnit.Point);

        var lblTitle = new Label
        {
            Text = "Cloud sync & characters",
            Location = new Point(16, 14),
            AutoSize = true,
            Font = new Font("Segoe UI", 11F, FontStyle.Bold, GraphicsUnit.Point),
            ForeColor = Color.White
        };

        _lblAccount = new Label
        {
            Location = new Point(16, 42),
            Size = new Size(300, 18),
            ForeColor = Color.FromArgb(180, 190, 210)
        };

        _btnAccount = new Button
        {
            Location = new Point(330, 38),
            Size = new Size(94, 26),
            UseVisualStyleBackColor = true
        };
        _btnAccount.Click += async (_, _) => await OnAccountClickAsync().ConfigureAwait(true);

        _lblStatus = new Label
        {
            Location = new Point(16, 68),
            Size = new Size(408, 32),
            ForeColor = Color.FromArgb(150, 200, 160)
        };

        var lblCharacter = new Label
        {
            Text = "Website character for completions",
            Location = new Point(16, 108),
            AutoSize = true
        };

        _cmbCharacter = new ComboBox
        {
            DropDownStyle = ComboBoxStyle.DropDownList,
            Location = new Point(16, 128),
            Size = new Size(408, 23)
        };
        _cmbCharacter.SelectedIndexChanged += (_, _) =>
        {
            if (_suppressCharacterEvents)
                return;
            if (_cmbCharacter.SelectedItem is DdoTrackerCharacter character)
                _cloudSync.SelectCharacter(character.Id);
            RefreshDetails();
        };

        _chkAutoSync = new CheckBox
        {
            Text = "Auto-sync completions when a dungeon finishes",
            Location = new Point(16, 162),
            AutoSize = true,
            ForeColor = Color.Silver
        };
        _chkAutoSync.CheckedChanged += (_, _) =>
        {
            if (_suppressAutoSyncEvents)
                return;
            _cloudSync.AutoSync = _chkAutoSync.Checked;
        };

        _lblGameChars = new Label
        {
            Location = new Point(16, 192),
            Size = new Size(408, 18),
            ForeColor = Color.FromArgb(170, 180, 195)
        };

        _lblWebsiteChars = new Label
        {
            Location = new Point(16, 212),
            Size = new Size(408, 18),
            ForeColor = Color.FromArgb(170, 180, 195)
        };

        _lblPending = new Label
        {
            Location = new Point(16, 232),
            Size = new Size(408, 18),
            ForeColor = Color.FromArgb(170, 180, 195)
        };

        _btnSyncCharacters = new Button
        {
            Text = "Sync characters to website",
            Location = new Point(16, 262),
            Size = new Size(200, 28),
            UseVisualStyleBackColor = true
        };
        _btnSyncCharacters.Click += async (_, _) => await OnSyncCharactersClickAsync().ConfigureAwait(true);

        _btnFlushPending = new Button
        {
            Text = "Push pending completions",
            Location = new Point(224, 262),
            Size = new Size(200, 28),
            UseVisualStyleBackColor = true
        };
        _btnFlushPending.Click += async (_, _) => await OnFlushPendingClickAsync().ConfigureAwait(true);

        _btnRefresh = new Button
        {
            Text = "Refresh session",
            Location = new Point(16, 298),
            Size = new Size(130, 28),
            UseVisualStyleBackColor = true
        };
        _btnRefresh.Click += async (_, _) => await OnRefreshClickAsync().ConfigureAwait(true);

        _btnOpenSite = new Button
        {
            Text = "Open website",
            Location = new Point(154, 298),
            Size = new Size(110, 28),
            UseVisualStyleBackColor = true
        };
        _btnOpenSite.Click += (_, _) =>
        {
            try
            {
                Process.Start(new ProcessStartInfo
                {
                    FileName = "https://ddotracker.zepsu.com/",
                    UseShellExecute = true
                });
            }
            catch (Exception ex)
            {
                MessageBox.Show(this, ex.Message, "DDO Tracker", MessageBoxButtons.OK, MessageBoxIcon.Warning);
            }
        };

        _btnClose = new Button
        {
            Text = "Close",
            DialogResult = DialogResult.OK,
            Location = new Point(314, 298),
            Size = new Size(110, 28),
            UseVisualStyleBackColor = true
        };

        CancelButton = _btnClose;

        Controls.Add(lblTitle);
        Controls.Add(_lblAccount);
        Controls.Add(_btnAccount);
        Controls.Add(_lblStatus);
        Controls.Add(lblCharacter);
        Controls.Add(_cmbCharacter);
        Controls.Add(_chkAutoSync);
        Controls.Add(_lblGameChars);
        Controls.Add(_lblWebsiteChars);
        Controls.Add(_lblPending);
        Controls.Add(_btnSyncCharacters);
        Controls.Add(_btnFlushPending);
        Controls.Add(_btnRefresh);
        Controls.Add(_btnOpenSite);
        Controls.Add(_btnClose);

        _cloudSync.StateChanged += OnCloudStateChanged;
        FormClosed += (_, _) => _cloudSync.StateChanged -= OnCloudStateChanged;

        RefreshAll();
    }

    private void OnCloudStateChanged()
    {
        if (IsDisposed)
            return;

        if (InvokeRequired)
        {
            BeginInvoke(RefreshAll);
            return;
        }

        RefreshAll();
    }

    private void RefreshAll()
    {
        RefreshDetails();
        RefreshCharacterList();
        UpdateEnabledState();
    }

    private void RefreshDetails()
    {
        _lblAccount.Text = _cloudSync.IsSignedIn
            ? $"Signed in as {_cloudSync.Email ?? "account"}"
            : "Not signed in";

        _lblStatus.Text = _cloudSync.StatusMessage;
        _btnAccount.Text = _cloudSync.IsSignedIn ? "Sign out" : "Sign in";

        _suppressAutoSyncEvents = true;
        _chkAutoSync.Checked = _cloudSync.AutoSync;
        _suppressAutoSyncEvents = false;

        var gameChars = _cloudSync.DiscoverGameCharacters();
        _lblGameChars.Text = gameChars.Count == 0
            ? "Game characters found: none (visit character select once for full roster)"
            : $"Game characters found: {gameChars.Count} — {string.Join(", ", gameChars.Select(c => c.Name))}";

        var website = _cloudSync.Characters;
        _lblWebsiteChars.Text = website.Count == 0
            ? "Website characters: none"
            : $"Website characters: {website.Count} — {string.Join(", ", website.Select(c => c.Name))}";

        var pending = _cloudSync.PendingCount;
        _lblPending.Text = pending == 0
            ? "Pending completions: none"
            : $"Pending completions: {pending}";
    }

    private void RefreshCharacterList()
    {
        _suppressCharacterEvents = true;
        _cmbCharacter.Items.Clear();

        if (_cloudSync.IsSignedIn)
        {
            foreach (var character in _cloudSync.Characters)
                _cmbCharacter.Items.Add(character);

            var selectedId = _cloudSync.SelectedCharacterId;
            if (!string.IsNullOrWhiteSpace(selectedId))
            {
                for (var i = 0; i < _cmbCharacter.Items.Count; i++)
                {
                    if (_cmbCharacter.Items[i] is DdoTrackerCharacter item
                        && string.Equals(item.Id, selectedId, StringComparison.OrdinalIgnoreCase))
                    {
                        _cmbCharacter.SelectedIndex = i;
                        break;
                    }
                }
            }
        }

        _suppressCharacterEvents = false;
    }

    private void UpdateEnabledState()
    {
        var signedIn = _cloudSync.IsSignedIn;
        _cmbCharacter.Enabled = signedIn && !_busy;
        _chkAutoSync.Enabled = signedIn && !_busy;
        _btnSyncCharacters.Enabled = signedIn && !_busy;
        _btnFlushPending.Enabled = signedIn && !_busy && _cloudSync.PendingCount > 0;
        _btnRefresh.Enabled = signedIn && !_busy;
        _btnAccount.Enabled = !_busy;
        _btnOpenSite.Enabled = !_busy;
        _btnClose.Enabled = !_busy;
    }

    private async Task OnAccountClickAsync()
    {
        if (_cloudSync.IsSignedIn)
        {
            var result = MessageBox.Show(
                this,
                "Sign out of DDO Tracker? Completions will stay local until you sign in again.",
                "DDO Tracker",
                MessageBoxButtons.YesNo,
                MessageBoxIcon.Question);
            if (result != DialogResult.Yes)
                return;

            await RunBusyAsync(async () => await _cloudSync.LogoutAsync().ConfigureAwait(true)).ConfigureAwait(true);
            return;
        }

        using var dialog = new DdoTrackerLoginForm(_cloudSync.Email);
        if (dialog.ShowDialog(this) != DialogResult.OK)
            return;

        await RunBusyAsync(async () =>
        {
            await _cloudSync.LoginAsync(dialog.Email, dialog.Password).ConfigureAwait(true);
            _cloudSync.BindToCurrentGameCharacter();
        }).ConfigureAwait(true);
    }

    private async Task OnSyncCharactersClickAsync()
    {
        await RunBusyAsync(async () =>
        {
            await _cloudSync.SyncAccountCharactersAsync().ConfigureAwait(true);
            MessageBox.Show(
                this,
                "Character sync finished. Existing website characters were linked and updated; missing ones were created.",
                "DDO Tracker",
                MessageBoxButtons.OK,
                MessageBoxIcon.Information);
        }).ConfigureAwait(true);
    }

    private async Task OnFlushPendingClickAsync()
    {
        await RunBusyAsync(async () => await _cloudSync.FlushPendingAsync().ConfigureAwait(true)).ConfigureAwait(true);
    }

    private async Task OnRefreshClickAsync()
    {
        await RunBusyAsync(async () =>
        {
            await _cloudSync.RefreshSessionAsync(syncCharacters: false).ConfigureAwait(true);
            _cloudSync.BindToCurrentGameCharacter();
        }).ConfigureAwait(true);
    }

    private async Task RunBusyAsync(Func<Task> action)
    {
        _busy = true;
        UpdateEnabledState();
        try
        {
            await action().ConfigureAwait(true);
        }
        catch (Exception ex)
        {
            MessageBox.Show(this, ex.Message, "DDO Tracker", MessageBoxButtons.OK, MessageBoxIcon.Error);
        }
        finally
        {
            _busy = false;
            RefreshAll();
        }
    }
}
