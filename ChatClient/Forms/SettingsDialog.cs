using ChatProtocol;

namespace ChatClient;

internal sealed class SettingsDialog : Form
{
    private readonly NetworkClient _network;
    private readonly string _username;
    private readonly Action _logout;
    private readonly PictureBox _avatar = new() { Size = new Size(184, 184), SizeMode = PictureBoxSizeMode.Zoom };
    private readonly Label _usernameLabel = new() { AutoSize = true, Font = new Font("Segoe UI Semibold", 16F), ForeColor = UiTheme.Text };
    private readonly Label _joinedLabel = new() { AutoSize = true, ForeColor = UiTheme.Muted };
    private readonly TextBox _displayName = new() { Width = 360 };
    private readonly TextBox _email = new() { Width = 360 };
    private readonly TextBox _bio = new() { Width = 360, Height = 54, Multiline = true, ScrollBars = ScrollBars.Vertical };
    private readonly ComboBox _status = new() { Width = 180, DropDownStyle = ComboBoxStyle.DropDownList };
    private readonly CheckBox _allowDirectMessages = new() { Text = "Cho phép người khác nhắn tin riêng", AutoSize = true };
    private readonly CheckBox _showAvatar = new() { Text = "Cho phép người khác xem avatar", AutoSize = true };
    private readonly CheckBox _showOnlineStatus = new() { Text = "Hiển thị trạng thái online", AutoSize = true };
    private readonly ListBox _blockedUsers = new() { Width = 220, Height = 82 };
    private readonly TextBox _blockUsername = new() { Width = 150 };
    private readonly Button _interfaceButton = new() { Text = "Giao diện", Dock = DockStyle.Top, Height = 46, TextAlign = ContentAlignment.MiddleLeft };
    private readonly Button _securityButton = new() { Text = "Bảo mật", Dock = DockStyle.Top, Height = 46, TextAlign = ContentAlignment.MiddleLeft };
    private readonly UserPreferences _preferences = UserPreferences.Load();
    private readonly Action<UserPreferences>? _appearanceChanged;
    private string? _pendingNewPassword;
    private readonly Button _accountButton = new() { Text = "Tài khoản", Dock = DockStyle.Top, Height = 46, TextAlign = ContentAlignment.MiddleLeft };
    private readonly Panel _content = new() { Dock = DockStyle.Fill, Padding = new Padding(34, 28, 34, 28), BackColor = UiTheme.Canvas };

    // Security page controls
    private ListView? _sessionsListView;
    private Button? _revokeSessionButton;
    private Button? _revokeAllSessionsButton;
    private Button? _refreshSessionsButton;
    private Button? _setup2faButton;
    private Button? _enable2faButton;
    private Button? _disable2faButton;
    private Label? _2faStatusLabel;
    private ListView? _loginHistoryListView;
    private Button? _refreshLoginHistoryButton;

    public SettingsDialog(NetworkClient network, string username, Action logout, Action<UserPreferences>? appearanceChanged = null)
    {
        _network = network;
        _username = username;
        _logout = logout;
        _appearanceChanged = appearanceChanged;
        ClientLog.Info($"SettingsDialog created user={username}");

        Text = "Cài đặt tài khoản";
        ClientSize = new Size(920, 720);
        MinimumSize = new Size(680, 650);
        StartPosition = FormStartPosition.CenterParent;
        FormBorderStyle = FormBorderStyle.FixedDialog;
        MaximizeBox = false;
        MinimizeBox = false;
        BackColor = UiTheme.Canvas;
        ForeColor = UiTheme.Text;
        Font = new Font("Segoe UI", 10F);

        BuildLayout();
        ShowAccountPage();
        ClientLog.Info("SettingsDialog requesting profile");
        _network.Send("profile", new ProfileRequest(_username));
    }

    private void BuildLayout()
    {
        var sidebar = new Panel { Dock = DockStyle.Left, Width = 210, Padding = new Padding(16), BackColor = UiTheme.Surface };
        var title = new Label { Text = "CÀI ĐẶT", Dock = DockStyle.Top, Height = 52, ForeColor = UiTheme.Muted, Font = new Font("Segoe UI Semibold", 9F), TextAlign = ContentAlignment.MiddleLeft };
        var logout = new Button { Text = "Đăng xuất", Dock = DockStyle.Bottom, Height = 42 };
        
        // Style buttons với visual feedback
        foreach (var btn in new[] { _accountButton, _interfaceButton, _securityButton, logout })
        {
            btn.FlatStyle = FlatStyle.Flat;
            btn.FlatAppearance.BorderSize = 0;
            btn.FlatAppearance.MouseOverBackColor = UiTheme.Primary;
            btn.FlatAppearance.MouseDownBackColor = Color.FromArgb(80, 90, 130);
            btn.Cursor = Cursors.Hand;
        }
            
        UiTheme.StyleButton(_accountButton, UiTheme.SurfaceRaised, UiTheme.Text);
        UiTheme.StyleButton(_interfaceButton, UiTheme.SurfaceRaised, UiTheme.Text);
        UiTheme.StyleButton(_securityButton, UiTheme.SurfaceRaised, UiTheme.Text);
        UiTheme.StyleButton(logout, Color.FromArgb(150, 45, 55), Color.White);
        _accountButton.Click += (_, _) => ShowAccountPage();
        _interfaceButton.Click += (_, _) => ShowInterfacePage();
        _securityButton.Click += (_, _) => ShowSecurityPage();
        logout.Click += (_, _) => ConfirmLogout();
        sidebar.Controls.Add(logout);
        sidebar.Controls.Add(_securityButton);
        sidebar.Controls.Add(_interfaceButton);
        sidebar.Controls.Add(_accountButton);
        sidebar.Controls.Add(title);

        Controls.Add(_content);
        Controls.Add(sidebar);
    }

    private void ShowAccountPage()
    {
        _content.Controls.Clear();
        var title = new Label { Text = "Tài khoản", Dock = DockStyle.Top, Height = 38, ForeColor = UiTheme.Text, Font = new Font("Segoe UI Semibold", 19F) };
        var subtitle = new Label { Text = "Quản lý thông tin và bảo mật tài khoản của bạn.", Dock = DockStyle.Top, Height = 28, ForeColor = UiTheme.Muted };
        var avatarPanel = new Panel { Dock = DockStyle.Left, Width = 230, Padding = new Padding(0, 26, 24, 0) };
        _avatar.Image ??= AvatarHelper.CreateRoundAvatar(null, _username, _avatar.Size.Width);
        avatarPanel.Controls.Add(_avatar);

        var details = new Panel { Dock = DockStyle.Fill, Padding = new Padding(14, 28, 0, 0) };
        _usernameLabel.Text = _username;
        _joinedLabel.Text = "Thành viên ChatNet";
        _displayName.Location = new Point(0, 78);
        _email.Location = new Point(0, 132);
        _bio.Location = new Point(0, 186);
        _status.Location = new Point(0, 258);
        _status.Items.Clear();
        _status.Items.AddRange(["Online", "Bận", "Ẩn"]);
        _status.SelectedIndex = 0;

        var displayNameLabel = CreateFieldLabel("Tên hiển thị", 0, 60);
        var emailLabel = CreateFieldLabel("Email", 0, 114);
        var bioLabel = CreateFieldLabel("Tiểu sử ngắn", 0, 168);
        var statusLabel = CreateFieldLabel("Trạng thái", 0, 240);
        var saveButton = new Button { Text = "Lưu thông tin", Width = 150, Height = 38, Location = new Point(195, 258) };
        var avatarButton = new Button { Text = "Đổi avatar", Width = 150, Height = 38, Location = new Point(0, 312) };
        var passwordButton = new Button { Text = "Đổi mật khẩu", Width = 150, Height = 38, Location = new Point(195, 312) };
        var privacyTitle = CreateFieldLabel("QUYỀN RIÊNG TƯ", 0, 366);
        _allowDirectMessages.Location = new Point(0, 390);
        _showAvatar.Location = new Point(0, 420);
        _showOnlineStatus.Location = new Point(0, 450);
        var blockedTitle = CreateFieldLabel("NGƯỜI DÙNG ĐÃ CHẶN", 0, 484);
        _blockedUsers.Location = new Point(0, 508);
        _blockUsername.Location = new Point(230, 508);
        var blockButton = new Button { Text = "Chặn", Width = 100, Height = 36, Location = new Point(230, 548) };
        var unblockButton = new Button { Text = "Bỏ chặn", Width = 100, Height = 36, Location = new Point(340, 548) };
        UiTheme.StyleInput(_displayName);
        UiTheme.StyleInput(_email);
        UiTheme.StyleInput(_bio);
        _status.BackColor = UiTheme.SurfaceRaised;
        _status.ForeColor = UiTheme.Text;
        _status.FlatStyle = FlatStyle.Flat;
        UiTheme.StyleButton(avatarButton, UiTheme.Primary, Color.White);
        UiTheme.StyleButton(passwordButton, UiTheme.SurfaceRaised, UiTheme.Text);
        UiTheme.StyleButton(saveButton, UiTheme.Mint, UiTheme.Canvas);
        UiTheme.StyleButton(blockButton, Color.FromArgb(150, 45, 55), Color.White);
        UiTheme.StyleButton(unblockButton, UiTheme.SurfaceRaised, UiTheme.Text);
        foreach (var checkBox in new[] { _allowDirectMessages, _showAvatar, _showOnlineStatus })
        {
            checkBox.ForeColor = UiTheme.Text;
            checkBox.BackColor = Color.Transparent;
        }
        avatarButton.Click += (_, _) => ChangeAvatar();
        passwordButton.Click += (_, _) => ChangePassword();
        saveButton.Click += (_, _) => SaveProfile();
        blockButton.Click += (_, _) => UpdateBlockedUser(true);
        unblockButton.Click += (_, _) => UpdateBlockedUser(false);
        details.Controls.Add(_usernameLabel);
        details.Controls.Add(_joinedLabel);
        details.Controls.AddRange([displayNameLabel, _displayName, emailLabel, _email, bioLabel, _bio, statusLabel, _status]);
        details.Controls.Add(saveButton);
        details.Controls.Add(avatarButton);
        details.Controls.Add(passwordButton);
        details.Controls.AddRange([privacyTitle, _allowDirectMessages, _showAvatar, _showOnlineStatus, blockedTitle, _blockedUsers, _blockUsername, blockButton, unblockButton]);

        _content.Controls.Add(details);
        _content.Controls.Add(avatarPanel);
        _content.Controls.Add(subtitle);
        _content.Controls.Add(title);
    }

    private void ShowInterfacePage()
    {
        _content.Controls.Clear();
        var title = new Label { Text = "Giao diện", Dock = DockStyle.Top, Height = 38, ForeColor = UiTheme.Text, Font = new Font("Segoe UI Semibold", 19F) };
        var subtitle = new Label { Text = "Tùy chỉnh cách ChatNet hiển thị và thông báo cho bạn.", Dock = DockStyle.Top, Height = 28, ForeColor = UiTheme.Muted };
        var page = new Panel { Dock = DockStyle.Fill, AutoScroll = true, Padding = new Padding(14, 28, 10, 12) };

        var themeLabel = CreateFieldLabel("CHỦ ĐỀ", 0, 0);
        var theme = new ComboBox { Location = new Point(0, 25), Width = 220, DropDownStyle = ComboBoxStyle.DropDownList };
        theme.Items.AddRange(["Dark", "Light", "System"]);
        theme.SelectedIndex = _preferences.Theme switch { "light" => 1, "system" => 2, _ => 0 };
        StyleCombo(theme);

        var accentLabel = CreateFieldLabel("MÀU CHỦ ĐẠO", 0, 75);
        var accentButton = new Button { Text = "Chọn màu", Location = new Point(0, 100), Width = 220, Height = 38 };
        UiTheme.StyleButton(accentButton, ParseColor(_preferences.AccentColor, UiTheme.Primary), Color.White);
        accentButton.Click += (_, _) =>
        {
            using var colorDialog = new ColorDialog { Color = ParseColor(_preferences.AccentColor, UiTheme.Primary), FullOpen = true };
            if (colorDialog.ShowDialog(this) == DialogResult.OK)
            {
                _preferences.AccentColor = ColorTranslator.ToHtml(colorDialog.Color);
                UiTheme.StyleButton(accentButton, colorDialog.Color, Color.White);
            }
        };

        var fontLabel = CreateFieldLabel("KÍCH THƯỚC CHỮ CHAT", 0, 150);
        var fontSize = new NumericUpDown { Location = new Point(0, 175), Width = 100, Minimum = 9, Maximum = 18, DecimalPlaces = 1, Increment = .5M, Value = (decimal)_preferences.ChatFontSize };
        StyleNumeric(fontSize);

        var displayLabel = CreateFieldLabel("HIỂN THỊ", 0, 225);
        var avatars = CreatePreferenceCheckBox("Hiển thị avatar trong tin nhắn", _preferences.ShowAvatarsInChat, 0, 250);
        var previews = CreatePreferenceCheckBox("Hiển thị preview ảnh", _preferences.ShowImagePreviews, 0, 280);

        var notificationLabel = CreateFieldLabel("THÔNG BÁO", 0, 330);
        var sound = CreatePreferenceCheckBox("Âm thanh tin nhắn mới", _preferences.MessageSound, 0, 355);
        var mentions = CreatePreferenceCheckBox("Thông báo khi được nhắc tên", _preferences.MentionNotifications, 0, 385);
        var directMessages = CreatePreferenceCheckBox("Thông báo tin nhắn riêng", _preferences.DirectMessageNotifications, 0, 415);
        var online = CreatePreferenceCheckBox("Thông báo khi có người online", _preferences.OnlineNotifications, 0, 445);

        var save = new Button { Text = "Lưu giao diện", Location = new Point(0, 500), Width = 160, Height = 40 };
        UiTheme.StyleButton(save, UiTheme.Mint, UiTheme.Canvas);
        save.Click += (_, _) =>
        {
            _preferences.Theme = theme.SelectedIndex switch { 1 => "light", 2 => "system", _ => "dark" };
            _preferences.ChatFontSize = (float)fontSize.Value;
            _preferences.ShowAvatarsInChat = avatars.Checked;
            _preferences.ShowImagePreviews = previews.Checked;
            _preferences.MessageSound = sound.Checked;
            _preferences.MentionNotifications = mentions.Checked;
            _preferences.DirectMessageNotifications = directMessages.Checked;
            _preferences.OnlineNotifications = online.Checked;
            _preferences.Save();
            _network.Send("update-appearance", new UpdateAppearanceRequest(
                _preferences.Theme,
                _preferences.AccentColor,
                _preferences.ChatFontSize,
                _preferences.ShowAvatarsInChat,
                _preferences.ShowImagePreviews,
                _preferences.MessageSound,
                _preferences.MentionNotifications,
                _preferences.DirectMessageNotifications,
                _preferences.OnlineNotifications));
            _appearanceChanged?.Invoke(_preferences);
            MessageBox.Show(this, "Đã lưu giao diện cá nhân.", "Giao diện", MessageBoxButtons.OK, MessageBoxIcon.Information);
        };

        page.Controls.AddRange([themeLabel, theme, accentLabel, accentButton, fontLabel, fontSize, displayLabel, avatars, previews, notificationLabel, sound, mentions, directMessages, online, save]);
        _content.Controls.Add(page);
        _content.Controls.Add(subtitle);
        _content.Controls.Add(title);
    }

    private void ShowSecurityPage()
    {
        _content.Controls.Clear();
        var title = new Label { Text = "Bảo mật", Dock = DockStyle.Top, Height = 38, ForeColor = UiTheme.Text, Font = new Font("Segoe UI Semibold", 19F) };
        var subtitle = new Label { Text = "Quản lý phiên đăng nhập, xác thực hai bước và lịch sử đăng nhập.", Dock = DockStyle.Top, Height = 28, ForeColor = UiTheme.Muted };
        var page = new Panel { Dock = DockStyle.Fill, AutoScroll = true, Padding = new Padding(14, 28, 10, 12) };

        // Sessions section
        var sessionsLabel = CreateFieldLabel("PHIÊN ĐĂNG NHẬP", 0, 0);
        _sessionsListView = new ListView
        {
            Location = new Point(0, 25),
            Size = new Size(600, 200),
            View = View.Details,
            FullRowSelect = true,
            GridLines = true,
            HideSelection = false,
            BackColor = UiTheme.SurfaceRaised,
            ForeColor = UiTheme.Text,
            BorderStyle = BorderStyle.FixedSingle
        };
        _sessionsListView.Columns.Add("Thiết bị", 200);
        _sessionsListView.Columns.Add("Địa chỉ IP", 150);
        _sessionsListView.Columns.Add("Đăng nhập lúc", 150);
        _sessionsListView.Columns.Add("Hoạt động gần nhất", 150);
        _sessionsListView.Columns.Add("Phiên hiện tại", 100);

        var sessionsButtonPanel = new Panel { Location = new Point(0, 235), Size = new Size(600, 40) };
        _refreshSessionsButton = new Button { Text = "Làm mới", Location = new Point(0, 0), Width = 100, Height = 36 };
        _revokeSessionButton = new Button { Text = "Thu hồi phiên đã chọn", Location = new Point(110, 0), Width = 150, Height = 36 };
        _revokeAllSessionsButton = new Button { Text = "Đăng xuất khỏi tất cả thiết bị", Location = new Point(270, 0), Width = 200, Height = 36 };
        UiTheme.StyleButton(_refreshSessionsButton, UiTheme.SurfaceRaised, UiTheme.Text);
        UiTheme.StyleButton(_revokeSessionButton, Color.FromArgb(150, 45, 55), Color.White);
        UiTheme.StyleButton(_revokeAllSessionsButton, Color.FromArgb(150, 45, 55), Color.White);
        _refreshSessionsButton.Click += (_, _) => LoadSessions();
        _revokeSessionButton.Click += (_, _) => RevokeSelectedSession();
        _revokeAllSessionsButton.Click += (_, _) => RevokeAllSessions();
        sessionsButtonPanel.Controls.AddRange([_refreshSessionsButton, _revokeSessionButton, _revokeAllSessionsButton]);

        // Two-factor authentication section
        var twoFactorLabel = CreateFieldLabel("XÁC THỰC HAI BƯỚC (2FA)", 0, 290);
        _2faStatusLabel = new Label { Location = new Point(0, 315), AutoSize = true, ForeColor = UiTheme.Text, Font = new Font("Segoe UI", 10F) };
        
        var twoFactorButtonPanel = new Panel { Location = new Point(0, 345), Size = new Size(600, 40) };
        _setup2faButton = new Button { Text = "Thiết lập 2FA", Location = new Point(0, 0), Width = 130, Height = 36 };
        _enable2faButton = new Button { Text = "Bật 2FA", Location = new Point(140, 0), Width = 100, Height = 36 };
        _disable2faButton = new Button { Text = "Tắt 2FA", Location = new Point(250, 0), Width = 100, Height = 36 };
        UiTheme.StyleButton(_setup2faButton, UiTheme.Primary, Color.White);
        UiTheme.StyleButton(_enable2faButton, UiTheme.Mint, UiTheme.Canvas);
        UiTheme.StyleButton(_disable2faButton, Color.FromArgb(150, 45, 55), Color.White);
        _setup2faButton.Click += (_, _) => SetupTwoFactor();
        _enable2faButton.Click += (_, _) => EnableTwoFactor();
        _disable2faButton.Click += (_, _) => DisableTwoFactor();
        twoFactorButtonPanel.Controls.AddRange([_setup2faButton, _enable2faButton, _disable2faButton]);

        // Login history section
        var loginHistoryLabel = CreateFieldLabel("LỊCH SỬ ĐĂNG NHẬP", 0, 400);
        _loginHistoryListView = new ListView
        {
            Location = new Point(0, 425),
            Size = new Size(600, 200),
            View = View.Details,
            FullRowSelect = true,
            GridLines = true,
            HideSelection = false,
            BackColor = UiTheme.SurfaceRaised,
            ForeColor = UiTheme.Text,
            BorderStyle = BorderStyle.FixedSingle
        };
        _loginHistoryListView.Columns.Add("Thời gian", 180);
        _loginHistoryListView.Columns.Add("Địa chỉ IP", 150);
        _loginHistoryListView.Columns.Add("Thiết bị", 150);
        _loginHistoryListView.Columns.Add("Trạng thái", 100);

        var loginHistoryButtonPanel = new Panel { Location = new Point(0, 635), Size = new Size(600, 40) };
        _refreshLoginHistoryButton = new Button { Text = "Làm mới", Location = new Point(0, 0), Width = 100, Height = 36 };
        UiTheme.StyleButton(_refreshLoginHistoryButton, UiTheme.SurfaceRaised, UiTheme.Text);
        _refreshLoginHistoryButton.Click += (_, _) => LoadLoginHistory();
        loginHistoryButtonPanel.Controls.Add(_refreshLoginHistoryButton);

        page.Controls.AddRange([
            sessionsLabel, _sessionsListView, sessionsButtonPanel,
            twoFactorLabel, _2faStatusLabel, twoFactorButtonPanel,
            loginHistoryLabel, _loginHistoryListView, loginHistoryButtonPanel
        ]);
        _content.Controls.Add(page);
        _content.Controls.Add(subtitle);
        _content.Controls.Add(title);

        // Load initial data
        LoadSessions();
        LoadTwoFactorStatus();
        LoadLoginHistory();
    }

    private async void LoadSessions()
    {
        if (_sessionsListView == null) return;
        _sessionsListView.Items.Clear();
        _network.Send("get-sessions", new GetSessionsRequest());
    }

    private async void LoadTwoFactorStatus()
    {
        _network.Send("2fa-status", new TwoFactorStatusResponse(false));
    }

    private async void LoadLoginHistory()
    {
        if (_loginHistoryListView == null) return;
        _loginHistoryListView.Items.Clear();
        _network.Send("get-login-history", new GetLoginHistoryRequest());
    }

    private void RevokeSelectedSession()
    {
        if (_sessionsListView?.SelectedItems.Count > 0)
        {
            var item = _sessionsListView.SelectedItems[0];
            var sessionId = item.Tag?.ToString();
            if (!string.IsNullOrEmpty(sessionId))
            {
                if (MessageBox.Show(this, "Bạn có chắc chắn muốn thu hồi phiên đăng nhập này?", "Xác nhận", MessageBoxButtons.YesNo, MessageBoxIcon.Question) == DialogResult.Yes)
                {
                    _network.Send("revoke-session", new RevokeSessionRequest(sessionId));
                }
            }
        }
    }

    private void RevokeAllSessions()
    {
        if (MessageBox.Show(this, "Bạn có chắc chắn muốn đăng xuất khỏi tất cả thiết bị khác? Phiên hiện tại sẽ được giữ lại.", "Xác nhận", MessageBoxButtons.YesNo, MessageBoxIcon.Question) == DialogResult.Yes)
        {
            _network.Send("revoke-all-sessions", new RevokeAllSessionsRequest());
        }
    }

    private void SetupTwoFactor()
    {
        _network.Send("2fa-setup", new TwoFactorSetupRequest());
    }

    private void EnableTwoFactor()
    {
        var code = PromptInputBox.Show("Bật xác thực hai bước", "Nhập mã 6 chữ số từ ứng dụng xác thực (Google Authenticator, Authy, v.v.):");
        if (!string.IsNullOrWhiteSpace(code))
        {
            _network.Send("2fa-enable", new TwoFactorEnableRequest(code.Trim()));
        }
    }

    private void DisableTwoFactor()
    {
        var code = PromptInputBox.Show("Tắt xác thực hai bước", "Nhập mã 6 chữ số từ ứng dụng xác thực hoặc mã dự phòng:");
        if (!string.IsNullOrWhiteSpace(code))
        {
            _network.Send("2fa-disable", new TwoFactorDisableRequest(code.Trim()));
        }
    }

    private static CheckBox CreatePreferenceCheckBox(string text, bool checkedValue, int x, int y) => new()
    {
        Text = text,
        Checked = checkedValue,
        Location = new Point(x, y),
        AutoSize = true,
        ForeColor = UiTheme.Text,
        BackColor = Color.Transparent
    };

    private static void StyleCombo(ComboBox combo)
    {
        combo.BackColor = UiTheme.SurfaceRaised;
        combo.ForeColor = UiTheme.Text;
        combo.FlatStyle = FlatStyle.Flat;
    }

    private static void StyleNumeric(NumericUpDown numeric)
    {
        numeric.BackColor = UiTheme.SurfaceRaised;
        numeric.ForeColor = UiTheme.Text;
        numeric.BorderStyle = BorderStyle.FixedSingle;
    }

    private static Color ParseColor(string value, Color fallback)
    {
        try { return ColorTranslator.FromHtml(value); }
        catch { return fallback; }
    }

    public void HandleProfile(UserProfile? profile)
    {
        if (profile == null) return;
        _joinedLabel.Text = $"Thành viên từ {profile.JoinedAt.LocalDateTime:dd/MM/yyyy}";
        _avatar.Image = AvatarHelper.CreateRoundAvatar(profile.AvatarBase64, profile.Username, _avatar.Size.Width);
        _displayName.Text = string.IsNullOrWhiteSpace(profile.DisplayName) ? profile.Username : profile.DisplayName;
        _email.Text = profile.Email ?? "";
        _bio.Text = profile.Bio ?? "";
        _status.SelectedIndex = profile.Status switch
        {
            "busy" => 1,
            "hidden" => 2,
            _ => 0
        };
        _allowDirectMessages.Checked = profile.AllowDirectMessages;
        _showAvatar.Checked = profile.ShowAvatar;
        _showOnlineStatus.Checked = profile.ShowOnlineStatus;
        _blockedUsers.Items.Clear();
        foreach (var username in profile.BlockedUsers ?? []) _blockedUsers.Items.Add(username);
        _preferences.Theme = profile.Theme;
        _preferences.AccentColor = profile.AccentColor;
        _preferences.ChatFontSize = profile.ChatFontSize;
        _preferences.ShowAvatarsInChat = profile.ShowAvatarsInChat;
        _preferences.ShowImagePreviews = profile.ShowImagePreviews;
        _preferences.MessageSound = profile.MessageSound;
        _preferences.MentionNotifications = profile.MentionNotifications;
        _preferences.DirectMessageNotifications = profile.DirectMessageNotifications;
        _preferences.OnlineNotifications = profile.OnlineNotifications;
    }

    public void HandleSettingsUpdated(string message)
    {
        MessageBox.Show(this, message, "Cài đặt", MessageBoxButtons.OK, MessageBoxIcon.Information);
        _network.Send("profile", new ProfileRequest(_username));
    }

    private void SaveProfile()
    {
        var status = _status.SelectedIndex switch
        {
            1 => "busy",
            2 => "hidden",
            _ => "online"
        };
        _network.Send("update-profile", new UpdateProfileRequest(
            _displayName.Text,
            _email.Text,
            _bio.Text,
            status));
            _network.Send("update-privacy", new UpdatePrivacyRequest(
                _allowDirectMessages.Checked,
                _showAvatar.Checked,
                _showOnlineStatus.Checked));
    }

    private void UpdateBlockedUser(bool blocked)
    {
        var username = blocked ? _blockUsername.Text.Trim() : _blockedUsers.SelectedItem?.ToString();
        if (string.IsNullOrWhiteSpace(username)) return;
        _network.Send("block-user", new BlockUserRequest(username, blocked));
        if (blocked) _blockUsername.Clear();
    }

    private static Label CreateFieldLabel(string text, int x, int y) => new()
    {
        Text = text,
        Location = new Point(x, y),
        AutoSize = true,
        ForeColor = UiTheme.Muted,
        Font = new Font("Segoe UI Semibold", 8.5F)
    };

    private void ChangeAvatar()
    {
        using var dialog = new OpenFileDialog
        {
            Title = "Chọn avatar",
            Filter = "Ảnh (*.png;*.jpg;*.jpeg)|*.png;*.jpg;*.jpeg",
            CheckFileExists = true
        };
        if (dialog.ShowDialog(this) != DialogResult.OK) return;

        try
        {
            using var source = Image.FromFile(dialog.FileName);
            using var thumbnail = new Bitmap(256, 256);
            using (var graphics = Graphics.FromImage(thumbnail))
            {
                graphics.Clear(Color.Transparent);
                graphics.InterpolationMode = System.Drawing.Drawing2D.InterpolationMode.HighQualityBicubic;
                var scale = Math.Min(256d / source.Width, 256d / source.Height);
                var width = (int)(source.Width * scale);
                var height = (int)(source.Height * scale);
                graphics.DrawImage(source, (256 - width) / 2, (256 - height) / 2, width, height);
            }
            using var stream = new MemoryStream();
            thumbnail.Save(stream, System.Drawing.Imaging.ImageFormat.Png);
            using var previewStream = new MemoryStream(stream.ToArray());
            using var preview = Image.FromStream(previewStream);
            _avatar.Image = new Bitmap(preview, _avatar.Size);
            _network.Send("update-avatar", new UpdateAvatarRequest(Convert.ToBase64String(stream.ToArray())));
        }
        catch (Exception ex)
        {
            MessageBox.Show(this, $"Không thể đọc ảnh: {ex.Message}", "Avatar", MessageBoxButtons.OK, MessageBoxIcon.Warning);
        }
    }

    private void ChangePassword()
    {
        var newPassword = PasswordChangeDialog.Show(this);
        if (newPassword == null) return;
        _pendingNewPassword = newPassword;
        _network.Send("request-password-change-otp", new RequestPasswordChangeOtp());
    }

    public void HandlePasswordOtpSent()
    {
        var otp = PromptInputBox.Show("Xác nhận đổi mật khẩu", "Nhập mã OTP đã gửi tới Gmail:");
        if (!string.IsNullOrWhiteSpace(otp) && !string.IsNullOrWhiteSpace(_pendingNewPassword))
        {
            _network.Send("verify-password-change", new VerifyPasswordChangeRequest(otp.Trim(), _pendingNewPassword));
        }
        _pendingNewPassword = null;
    }

    private void ConfirmLogout()
    {
        if (MessageBox.Show(this, "Bạn có chắc chắn muốn đăng xuất?", "Đăng xuất", MessageBoxButtons.YesNo, MessageBoxIcon.Question) == DialogResult.Yes)
        {
            _logout();
            Close();
        }
    }

    // Security page handlers
    public void HandleSessions(GetSessionsResponse response)
    {
        if (_sessionsListView == null) return;
        _sessionsListView.Items.Clear();
        foreach (var session in response.Sessions)
        {
            var item = new ListViewItem(session.DeviceInfo.Length > 0 ? session.DeviceInfo : "Thiết bị không xác định");
            item.SubItems.Add(session.IpAddress);
            item.SubItems.Add(session.LoginTime.LocalDateTime.ToString("dd/MM/yyyy HH:mm"));
            item.SubItems.Add(session.LastActive.LocalDateTime.ToString("dd/MM/yyyy HH:mm"));
            item.SubItems.Add(session.IsCurrentSession ? "✓" : "");
            item.Tag = session.SessionId;
            item.BackColor = session.IsCurrentSession ? Color.FromArgb(40, 80, 40) : UiTheme.SurfaceRaised;
            _sessionsListView.Items.Add(item);
        }
    }

    public void HandleSessionRevoked(RevokeSessionResponse response)
    {
        MessageBox.Show(this, response.Message, response.Success ? "Thành công" : "Lỗi", MessageBoxButtons.OK, response.Success ? MessageBoxIcon.Information : MessageBoxIcon.Warning);
        if (response.Success) LoadSessions();
    }

    public void HandleAllSessionsRevoked(RevokeAllSessionsResponse response)
    {
        MessageBox.Show(this, response.Message, response.Success ? "Thành công" : "Lỗi", MessageBoxButtons.OK, response.Success ? MessageBoxIcon.Information : MessageBoxIcon.Warning);
        if (response.Success) LoadSessions();
    }

    public void HandleTwoFactorSetup(TwoFactorSetupResponse response)
    {
        if (string.IsNullOrEmpty(response.Secret))
        {
            MessageBox.Show(this, "Không thể thiết lập 2FA. Vui lòng thử lại.", "Lỗi", MessageBoxButtons.OK, MessageBoxIcon.Error);
            return;
        }

        // Show QR code and backup codes
        using var qrDialog = new Form
        {
            Text = "Thiết lập xác thực hai bước",
            Size = new Size(500, 600),
            StartPosition = FormStartPosition.CenterParent,
            FormBorderStyle = FormBorderStyle.FixedDialog,
            MaximizeBox = false,
            MinimizeBox = false,
            BackColor = UiTheme.Canvas
        };

        var layout = new TableLayoutPanel { Dock = DockStyle.Fill, Padding = new Padding(20), RowCount = 5, ColumnCount = 1 };
        layout.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        layout.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        layout.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        layout.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        layout.RowStyles.Add(new RowStyle(SizeType.AutoSize));

        var titleLabel = new Label { Text = "Quét mã QR bằng ứng dụng xác thực", AutoSize = true, Font = new Font("Segoe UI Semibold", 12F), ForeColor = UiTheme.Text, TextAlign = ContentAlignment.MiddleCenter, Dock = DockStyle.Fill };
        layout.Controls.Add(titleLabel, 0, 0);

        // QR Code
        var qrPictureBox = new PictureBox { Size = new Size(256, 256), SizeMode = PictureBoxSizeMode.Zoom, Dock = DockStyle.Fill };
        if (!string.IsNullOrEmpty(response.QrCodeBase64))
        {
            try
            {
                var qrBytes = Convert.FromBase64String(response.QrCodeBase64);
                using var ms = new MemoryStream(qrBytes);
                qrPictureBox.Image = Image.FromStream(ms);
            }
            catch { }
        }
        layout.Controls.Add(qrPictureBox, 0, 1);

        var secretLabel = new Label { Text = $"Khóa bí mật: {response.Secret}", AutoSize = true, Font = new Font("Consolas", 10F), ForeColor = UiTheme.Text, Dock = DockStyle.Fill };
        layout.Controls.Add(secretLabel, 0, 2);

        // Backup codes
        var backupLabel = new Label
        {
            Text = "Mã dự phòng (lưu lại an toàn):\r\n" + string.Join("\r\n", response.BackupCodes),
            AutoSize = true,
            Font = new Font("Consolas", 10F),
            ForeColor = UiTheme.Text,
            Dock = DockStyle.Fill
        };
        layout.Controls.Add(backupLabel, 0, 3);

        var closeButton = new Button { Text = "Đóng", Width = 100, Height = 36, Anchor = AnchorStyles.None };
        UiTheme.StyleButton(closeButton, UiTheme.SurfaceRaised, UiTheme.Text);
        closeButton.Click += (_, _) => qrDialog.Close();
        layout.Controls.Add(closeButton, 0, 4);

        qrDialog.Controls.Add(layout);
        qrDialog.ShowDialog(this);
    }

    public void HandleTwoFactorStatus(TwoFactorStatusResponse response)
    {
        if (_2faStatusLabel == null) return;
        _2faStatusLabel.Text = response.Enabled ? "Trạng thái: Đã bật" : "Trạng thái: Chưa bật";
        _2faStatusLabel.ForeColor = response.Enabled ? UiTheme.Mint : UiTheme.Muted;
    }

    public void HandleTwoFactorEnabled(TwoFactorEnableResponse response)
    {
        MessageBox.Show(this, response.Message, response.Success ? "Thành công" : "Lỗi", MessageBoxButtons.OK, response.Success ? MessageBoxIcon.Information : MessageBoxIcon.Warning);
        if (response.Success)
        {
            LoadTwoFactorStatus();
            if (response.BackupCodes is { Count: > 0 })
            {
                MessageBox.Show(this, "Mã dự phòng:\r\n\r\n" + string.Join("\r\n", response.BackupCodes), "Mã dự phòng 2FA", MessageBoxButtons.OK, MessageBoxIcon.Information);
            }
        }
    }

    public void HandleTwoFactorDisabled(TwoFactorDisableResponse response)
    {
        MessageBox.Show(this, response.Message, response.Success ? "Thành công" : "Lỗi", MessageBoxButtons.OK, response.Success ? MessageBoxIcon.Information : MessageBoxIcon.Warning);
        if (response.Success) LoadTwoFactorStatus();
    }

    public void HandleLoginHistory(GetLoginHistoryResponse response)
    {
        if (_loginHistoryListView == null) return;
        _loginHistoryListView.Items.Clear();
        foreach (var entry in response.History)
        {
            var item = new ListViewItem(entry.LoginTime.LocalDateTime.ToString("dd/MM/yyyy HH:mm"));
            item.SubItems.Add(entry.IpAddress);
            item.SubItems.Add(entry.DeviceInfo);
            item.SubItems.Add(entry.Success ? "Thành công" : "Thất bại");
            item.ForeColor = entry.Success ? UiTheme.Text : Color.IndianRed;
            _loginHistoryListView.Items.Add(item);
        }
    }
}