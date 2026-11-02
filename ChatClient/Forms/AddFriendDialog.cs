using ChatProtocol;
using System.Drawing.Drawing2D;

namespace ChatClient;

public sealed class AddFriendDialog : Form
{
    private readonly NetworkClient _network;
    private readonly string _currentUsername;
    private readonly TextBox _txtUsername = new() { PlaceholderText = "Nhập tên người dùng..." };
    private readonly Button _btnSend = new() { Text = "Gửi lời mời", DialogResult = DialogResult.OK };
    private readonly Button _btnCancel = new() { Text = "Hủy", DialogResult = DialogResult.Cancel };
    private readonly Label _lblStatus = new() { AutoSize = false, Height = 32, ForeColor = UiTheme.Mint, TextAlign = ContentAlignment.MiddleCenter };
    private readonly PictureBox _picAvatar = new() { Size = new Size(64, 64), SizeMode = PictureBoxSizeMode.Zoom, BorderStyle = BorderStyle.FixedSingle };
    private readonly Label _lblDisplayName = new() { AutoSize = true, Font = new Font("Segoe UI Semibold", 12F), ForeColor = UiTheme.Text };
    private readonly Panel _previewPanel = new() { Height = 100, Visible = false };
    private bool _userFound = false;
    private string _foundUsername = "";
    private string _foundDisplayName = "";
    private string _foundAvatar = "";

    public AddFriendDialog(NetworkClient network, string currentUsername)
    {
        _network = network;
        _currentUsername = currentUsername;
        InitializeComponent();
      
    }

    // Constructor overload (3 tham số) - dùng cho context menu "Kết bạn" trên user cụ thể
    public AddFriendDialog(NetworkClient network, string currentUsername, string prefillUsername)
        : this(network, currentUsername)
    {
        _txtUsername.Text = prefillUsername;
        // Dùng event Shown thay vì BeginInvoke trong constructor
        Shown += async (_, _) =>
        {
            await Task.Delay(100);
            BtnSearch_Click(null, EventArgs.Empty);
        };
    }

    private void InitializeComponent()
    {
        Text = "Thêm bạn bè";
        Size = new Size(420, 380);
        MinimumSize = new Size(420, 380);
        StartPosition = FormStartPosition.CenterParent;
        FormBorderStyle = FormBorderStyle.FixedDialog;
        MaximizeBox = false;
        MinimizeBox = false;
        BackColor = UiTheme.Canvas;
        Font = new Font("Segoe UI", 10F);
        ForeColor = UiTheme.Text;
        Padding = new Padding(24);

        // Icon
        try
        {
            string iconPath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "su.ico");
            if (File.Exists(iconPath)) Icon = new Icon(iconPath);
        }
        catch { }

        Paint += (_, e) =>
        {
            using var brush = new LinearGradientBrush(ClientRectangle,
                Color.FromArgb(20, 24, 38),
                Color.FromArgb(30, 35, 47),
                90F);
            e.Graphics.FillRectangle(brush, ClientRectangle);
        };

        var layout = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            ColumnCount = 1,
            RowCount = 6,
            BackColor = Color.Transparent
        };
        layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 50));  // Title
        layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 60));  // Input
        layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 50));  // Search btn
        layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 110)); // Preview
        layout.RowStyles.Add(new RowStyle(SizeType.Percent, 100));  // Spacer
        layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 50));  // Buttons

        // Title
        var lblTitle = new Label
        {
            Text = "Tìm kiếm người dùng",
            Dock = DockStyle.Fill,
            TextAlign = ContentAlignment.MiddleCenter,
            Font = new Font("Segoe UI Semibold", 16F),
            ForeColor = UiTheme.Text
        };
        layout.Controls.Add(lblTitle, 0, 0);

        // Input row
        var inputPanel = new Panel { Dock = DockStyle.Fill, Padding = new Padding(0, 8, 0, 8) };
        var lblInput = new Label { Text = "Tên người dùng", Dock = DockStyle.Top, Height = 24, ForeColor = UiTheme.Muted, Font = new Font("Segoe UI", 8.5F, FontStyle.Bold) };
        _txtUsername.Dock = DockStyle.Bottom;
        _txtUsername.Height = 36;
        UiTheme.StyleInput(_txtUsername);
        _txtUsername.TextChanged += TxtUsername_TextChanged;
        _txtUsername.KeyDown += (_, e) => { if (e.KeyCode == Keys.Enter && _userFound) _btnSend.PerformClick(); };
        inputPanel.Controls.Add(_txtUsername);
        inputPanel.Controls.Add(lblInput);
        layout.Controls.Add(inputPanel, 0, 1);

        // Search button
        var btnSearch = new Button { Text = "🔍  Tìm kiếm", Dock = DockStyle.Fill, Height = 42 };
        UiTheme.StyleButton(btnSearch, UiTheme.Primary, Color.White);
        btnSearch.Click += BtnSearch_Click;
        layout.Controls.Add(btnSearch, 0, 2);

        // Preview panel
        _previewPanel.BackColor = UiTheme.Surface;
        _previewPanel.Padding = new Padding(12);
        _previewPanel.Paint += (_, e) =>
        {
            using var pen = new Pen(Color.FromArgb(60, 70, 90));
            e.Graphics.DrawRectangle(pen, 0, 0, _previewPanel.Width - 1, _previewPanel.Height - 1);
        };

        var previewLayout = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 2, RowCount = 2, BackColor = Color.Transparent };
        previewLayout.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 74));
        previewLayout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        previewLayout.RowStyles.Add(new RowStyle(SizeType.Absolute, 30));
        previewLayout.RowStyles.Add(new RowStyle(SizeType.Percent, 100));

        _picAvatar.Location = new Point(0, 0);
        _picAvatar.Image = AvatarHelper.CreateRoundAvatar(null, "?", 64);
        previewLayout.Controls.Add(_picAvatar, 0, 0);
        previewLayout.SetRowSpan(_picAvatar, 2);

        _lblDisplayName.Dock = DockStyle.Bottom;
        previewLayout.Controls.Add(_lblDisplayName, 1, 0);

        var lblNote = new Label { Text = "Nhấn \"Gửi lời mời\" để kết bạn", Dock = DockStyle.Top, ForeColor = UiTheme.Muted, Font = new Font("Segoe UI", 9F) };
        previewLayout.Controls.Add(lblNote, 1, 1);

        _previewPanel.Controls.Add(previewLayout);
        layout.Controls.Add(_previewPanel, 0, 3);

        // Status label
        _lblStatus.Dock = DockStyle.Fill;
        layout.Controls.Add(_lblStatus, 0, 4);

        // Buttons
        var btnPanel = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 2, RowCount = 1, BackColor = Color.Transparent };
        btnPanel.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 50));
        btnPanel.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 50));

        UiTheme.StyleButton(_btnCancel, UiTheme.SurfaceRaised, UiTheme.Text);
        _btnCancel.Dock = DockStyle.Fill;
        _btnCancel.Margin = new Padding(0, 0, 6, 0);
        btnPanel.Controls.Add(_btnCancel, 0, 0);

        UiTheme.StyleButton(_btnSend, UiTheme.Primary, Color.White);
        _btnSend.Dock = DockStyle.Fill;
        _btnSend.Margin = new Padding(6, 0, 0, 0);
        _btnSend.Enabled = false;
        _btnSend.Click += BtnSend_Click;
        btnPanel.Controls.Add(_btnSend, 1, 0);

        layout.Controls.Add(btnPanel, 0, 5);

        Controls.Add(layout);
        AcceptButton = _btnSend;
        CancelButton = _btnCancel;
    }

    private void TxtUsername_TextChanged(object? sender, EventArgs e)
    {
        _userFound = false;
        _btnSend.Enabled = false;
        _previewPanel.Visible = false;
        _lblStatus.Text = "";
        _txtUsername.BackColor = UiTheme.SurfaceRaised;
    }

    private async void BtnSearch_Click(object? sender, EventArgs e)
    {
        var username = _txtUsername.Text.Trim();
        if (string.IsNullOrWhiteSpace(username) || username.Length < 3)
        {
            ShowError("Tên người dùng phải có ít nhất 3 ký tự.");
            return;
        }

        if (string.Equals(username, _currentUsername, StringComparison.OrdinalIgnoreCase))
        {
            ShowError("Không thể kết bạn với chính mình.");
            return;
        }

        _btnSend.Enabled = false;
        _previewPanel.Visible = false;
        _lblStatus.ForeColor = UiTheme.Mint;
        _lblStatus.Text = "Đang tìm kiếm...";

        var tcs = new TaskCompletionSource<Envelope>(TaskCreationOptions.RunContinuationsAsynchronously);
        void OnMessage(Envelope env)
        {
            if (env.Type is "profile" or "error") tcs.TrySetResult(env);
        }
        _network.MessageReceived += OnMessage;

        try
        {
            _network.Send("profile", new ProfileRequest(username));
            var completed = await Task.WhenAny(tcs.Task, Task.Delay(TimeSpan.FromSeconds(10)));
            if (completed != tcs.Task) throw new TimeoutException("Máy chủ không phản hồi.");

            var result = await tcs.Task;
            if (result.Type == "error")
            {
                var err = result.As<ErrorResponse>()?.Message ?? "Không tìm thấy người dùng.";
                ShowError(err);
                return;
            }

            var profile = result.As<ProfileResponse>()?.Profile;
            if (profile == null)
            {
                ShowError("Không tìm thấy người dùng.");
                return;
            }

            // Check if already friends or pending
            if (profile.Friends?.Contains(_currentUsername) == true)
            {
                ShowError("Đã là bạn bè.");
                return;
            }
            if (profile.ReceivedFriendRequests?.Contains(_currentUsername) == true)
            {
                ShowError("Đối phương đã gửi lời mời cho bạn. Hãy kiểm tra lời mời nhận được.");
                return;
            }
            if (profile.PendingFriendRequests?.Contains(_currentUsername) == true)
            {
                ShowError("Bạn đã gửi lời mời cho người này.");
                return;
            }

            _foundUsername = profile.Username;
            _foundDisplayName = profile.DisplayName ?? profile.Username;
            _foundAvatar = profile.AvatarBase64 ?? "";
            _lblDisplayName.Text = _foundDisplayName;
            _picAvatar.Image = AvatarHelper.CreateRoundAvatar(_foundAvatar, _foundDisplayName, 64);
            _previewPanel.Visible = true;
            _userFound = true;
            _btnSend.Enabled = true;
            _lblStatus.ForeColor = UiTheme.Mint;
            _lblStatus.Text = "Tìm thấy người dùng. Sẵn sàng gửi lời mời.";
        }
        catch (Exception ex)
        {
            ShowError($"Lỗi: {ex.Message}");
        }
        finally
        {
            _network.MessageReceived -= OnMessage;
        }
    }

    private void ShowError(string message)
    {
        _userFound = false;
        _btnSend.Enabled = false;
        _previewPanel.Visible = false;
        _lblStatus.ForeColor = Color.FromArgb(251, 146, 160);
        _lblStatus.Text = message;
        _txtUsername.BackColor = Color.FromArgb(80, 30, 30);
    }

    private async void BtnSend_Click(object? sender, EventArgs e)
    {
        if (!_userFound || string.IsNullOrEmpty(_foundUsername)) return;

        _btnSend.Enabled = false;
        _btnCancel.Enabled = false;
        _lblStatus.ForeColor = UiTheme.Mint;
        _lblStatus.Text = "Đang gửi lời mời...";

        var tcs = new TaskCompletionSource<Envelope>(TaskCreationOptions.RunContinuationsAsynchronously);
        void OnMessage(Envelope env)
        {
            if (env.Type is "friend-request-sent" or "friend-request-received" or "error") tcs.TrySetResult(env);
        }
        _network.MessageReceived += OnMessage;

        try
        {
            _network.Send("friend-request", new SendFriendRequest(_foundUsername));
            var completed = await Task.WhenAny(tcs.Task, Task.Delay(TimeSpan.FromSeconds(10)));
            if (completed != tcs.Task) throw new TimeoutException("Máy chủ không phản hồi.");

            var result = await tcs.Task;
            if (result.Type == "error")
            {
                var err = result.As<ErrorResponse>()?.Message ?? "Gửi lời mời thất bại.";
                ShowError(err);
                return;
            }

            _lblStatus.ForeColor = UiTheme.Mint;
            _lblStatus.Text = "✓ Đã gửi lời mời kết bạn!";
            _btnSend.Enabled = false;
            _btnCancel.Text = "Đóng";
            _btnCancel.Enabled = true;
            DialogResult = DialogResult.OK;

            // Auto close sau 1.5s
            await Task.Delay(1500);
            Close();
        }
        catch (Exception ex)
        {
            ShowError($"Lỗi: {ex.Message}");
            _btnSend.Enabled = _userFound;
            _btnCancel.Enabled = true;
        }
        finally
        {
            _network.MessageReceived -= OnMessage;
        }
    }
}