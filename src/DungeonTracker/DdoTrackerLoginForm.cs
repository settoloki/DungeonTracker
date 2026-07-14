namespace DungeonTracker;

public sealed class DdoTrackerLoginForm : Form
{
    private readonly TextBox _txtEmail;
    private readonly TextBox _txtPassword;
    private readonly Button _btnLogin;
    private readonly Button _btnCancel;
    private readonly Label _lblHint;

    public string Email => _txtEmail.Text.Trim();
    public string Password => _txtPassword.Text;

    public DdoTrackerLoginForm(string? existingEmail = null)
    {
        Text = "Sign in to DDO Tracker";
        FormBorderStyle = FormBorderStyle.FixedDialog;
        StartPosition = FormStartPosition.CenterParent;
        MinimizeBox = false;
        MaximizeBox = false;
        ShowInTaskbar = false;
        ClientSize = new Size(360, 180);
        BackColor = Color.FromArgb(28, 32, 40);
        ForeColor = Color.Gainsboro;
        Font = new Font("Segoe UI", 9F, FontStyle.Regular, GraphicsUnit.Point);

        var lblEmail = new Label
        {
            Text = "Email",
            Location = new Point(16, 16),
            AutoSize = true
        };

        _txtEmail = new TextBox
        {
            Location = new Point(16, 34),
            Width = 328,
            Text = existingEmail ?? string.Empty
        };

        var lblPassword = new Label
        {
            Text = "Password",
            Location = new Point(16, 66),
            AutoSize = true
        };

        _txtPassword = new TextBox
        {
            Location = new Point(16, 84),
            Width = 328,
            UseSystemPasswordChar = true
        };

        _lblHint = new Label
        {
            Text = "Uses https://ddotracker.zepsu.com/api/plugin — bearer token stored locally.",
            Location = new Point(16, 114),
            Width = 328,
            Height = 28,
            ForeColor = Color.FromArgb(150, 160, 175)
        };

        _btnLogin = new Button
        {
            Text = "Sign in",
            DialogResult = DialogResult.None,
            Location = new Point(188, 146),
            Width = 75
        };
        _btnLogin.Click += (_, _) =>
        {
            if (string.IsNullOrWhiteSpace(Email) || string.IsNullOrWhiteSpace(Password))
            {
                MessageBox.Show(this, "Enter email and password.", "DDO Tracker", MessageBoxButtons.OK, MessageBoxIcon.Information);
                return;
            }

            DialogResult = DialogResult.OK;
            Close();
        };

        _btnCancel = new Button
        {
            Text = "Cancel",
            DialogResult = DialogResult.Cancel,
            Location = new Point(269, 146),
            Width = 75
        };

        AcceptButton = _btnLogin;
        CancelButton = _btnCancel;

        Controls.Add(lblEmail);
        Controls.Add(_txtEmail);
        Controls.Add(lblPassword);
        Controls.Add(_txtPassword);
        Controls.Add(_lblHint);
        Controls.Add(_btnLogin);
        Controls.Add(_btnCancel);
    }
}
