using ChatProtocol;

namespace ChatClient;

/// <summary>
/// Persistent shell shown after authentication.  Server, channel and workflow
/// pages are rendered inside this one window rather than opening a new form.
/// </summary>
public sealed class UserMainForm : Form
{
    private readonly NetworkClient _network;
    private readonly string _username;
    private readonly ListBox _servers = new() { Dock = DockStyle.Fill, IntegralHeight = false };
    private readonly ListBox _channels = new() { Dock = DockStyle.Fill, IntegralHeight = false };
    private readonly Panel _content = new() { Dock = DockStyle.Fill, Padding = new Padding(34), BackColor = UiTheme.Canvas };
    private readonly Label _status = new() { Dock = DockStyle.Bottom, Height = 28, ForeColor = UiTheme.Muted, TextAlign = ContentAlignment.MiddleLeft };
    private readonly Dictionary<string, ServerSummary> _serverById = new();
    
    // Thay vì hoặc dùng song song với ListBox _channels:
    
    private readonly TreeView _channelTree = new() { Dock = DockStyle.Fill, BorderStyle = BorderStyle.None };
    
    private ServerSummary? _selectedServer;
    private TextBox? _newServerName;
    private TextBox? _newServerDescription;
    private TextBox? _serverOtp;
    private ChatForm? _activeChatForm;
    private LiveForm? _activeLiveForm;
    private bool _profileDialogRequested;
    private bool _settingsOpen;
    private bool _suppressNavigationEvents;
    private bool _updatingLists;
    private bool _serverSelectionQueued;
    private bool _channelSelectionQueued;
    private bool _navigationInProgress;

    private Panel _channelSplitter = null!;
    private bool _isResizingChannel = false;
    private int _resizeStartX;
    private int _resizeStartWidth;

    private const int ChannelMinWidth = 180;
    private const int ChannelMaxWidth = 450;
    


    // ----- Settings (chuyển từ ChatForm) -----
    private UserPreferences _preferences = UserPreferences.Load();
    private SettingsDialog? _settingsDialog;
    private Button _btnSettings = null!;
    private Button _btnProfile = null!;
    private Panel _appBar = null!;

    // ----- Sidebar SERVERS -----
    private Panel _serverPanel = null!;
    private Panel _serverHeader = null!;
    private Label _serverHeading = null!;
    private Button _btnToggleServers = null!;
    private Button _btnCreateServer = null!;
    private Button _btnJoinServer = null!;
    private bool _isServerExpanded = true;
    private const int ServerExpandedWidth = 210;
    private const int ServerCollapsedWidth = 56;

    // ----- Sidebar KÊNH -----
    private Panel _channelPanel = null!;
    private Panel _channelHeader = null!;
    private Label _channelHeading = null!;
    private Button _btnToggleChannels = null!;
    private bool _isChannelExpanded = true;
    private const int ChannelExpandedWidth = 220;
    private const int ChannelCollapsedWidth = 44;

    // Một timer chung để trượt cả hai sidebar
    private readonly System.Windows.Forms.Timer _sidebarTimer = new() { Interval = 10 };
    private int _serverTargetWidth = ServerExpandedWidth;
    private int _channelTargetWidth = ChannelExpandedWidth;
    private const int SlideStep = 22;

    public UserMainForm(NetworkClient network, string username)
    {
        _network = network;
        _username = username;
        ClientLog.Info($"UserMainForm created user={username}; log={ClientLog.FilePath}");
        Text = "RE:CHAT";
        try
        {
            string iconPath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "su.ico");
            if (File.Exists(iconPath))
            {
                Icon = new Icon(iconPath);
            }
        }
        catch (Exception ex)
        {
            Console.WriteLine($"Không thể tải icon ChatForm: {ex.Message}");
        }
        StartPosition = FormStartPosition.CenterScreen;
        Width = 1330;
        Height = 720;
        MinimumSize = new Size(920, 580);
        BackColor = UiTheme.Canvas;
        ForeColor = UiTheme.Text;
        Font = new Font("Segoe UI", 10F);

        BuildLayout();
        _network.MessageReceived += OnMessageReceived;
        _network.Disconnected += OnDisconnected;
        FormClosed += (_, _) =>
        {
            _sidebarTimer.Stop();
            _network.Close();
        };

        KeyPreview = true;
        KeyDown += (_, e) =>
        {
            if (e.Control && e.KeyCode == Keys.F)
            {
                e.SuppressKeyPress = true;
                OpenAddFriendDialog();
            }
        };

        ShowHome();
        ApplyPreferences(_preferences);
        _network.Send("get-my-servers", new GetMyServersRequest());
        _network.Send("profile", new ProfileRequest(_username)); // nạp cấu hình đã lưu trên server
    }

    private void BuildLayout()
    {
        _appBar = new Panel { Dock = DockStyle.Left, Width = 70, BackColor = Color.FromArgb(19, 24, 42), Padding = new Padding(10) };

        var home = MakeButton("R", UiTheme.Primary);
        home.Height = 46;
        home.Dock = DockStyle.Top;
        home.Click += (_, _) => ShowHome();

        var addServer = MakeButton("+", UiTheme.SurfaceRaised);
        addServer.Height = 42;
        addServer.Dock = DockStyle.Top;
        addServer.Margin = new Padding(0, 10, 0, 0);
        addServer.Click += (_, _) => ShowCreateServer();

                // === THÊM NÚT "THÊM BẠN" ===
        var btnAddFriend = MakeButton("👥", UiTheme.SurfaceRaised);
        btnAddFriend.Height = 42;
        btnAddFriend.Dock = DockStyle.Top;
        btnAddFriend.Margin = new Padding(0, 10, 0, 0);
        new ToolTip().SetToolTip(btnAddFriend, "Thêm bạn bè (Ctrl+F)");
        btnAddFriend.Click += (_, _) => OpenAddFriendDialog();
        // ============================


        // Nút cài đặt nằm dưới đáy thanh bên trái
        _btnSettings = MakeButton("⚙", UiTheme.SurfaceRaised);
        _btnSettings.Height = 42;
        _btnSettings.Click += (_, _) => OpenSettings();
        new ToolTip().SetToolTip(_btnSettings, "Cài đặt tài khoản");

        _btnProfile = MakeButton("◉", UiTheme.SurfaceRaised);
        _btnProfile.Height = 42;
        _btnProfile.Click += (_, _) => ShowProfile(_username);
        new ToolTip().SetToolTip(_btnProfile, $"Hồ sơ của {_username}");

        var accountButtons = new Panel { Dock = DockStyle.Bottom, Height = 92, BackColor = Color.Transparent };
        _btnSettings.Dock = DockStyle.Top;
        _btnProfile.Dock = DockStyle.Bottom;
        accountButtons.Controls.Add(_btnSettings);
        accountButtons.Controls.Add(_btnProfile);

        _appBar.Controls.Add(addServer);
        _appBar.Controls.Add(home);
        _appBar.Controls.Add(accountButtons);

        _serverPanel = BuildServerPanel();
        _channelPanel = BuildChannelPanel();


        var contentShell = new Panel { Dock = DockStyle.Fill, BackColor = UiTheme.Canvas };
        contentShell.Controls.Add(_content);
        contentShell.Controls.Add(_status);

        // Với DockStyle.Left, control thêm SAU sẽ nằm gần mép trái hơn,
        // nên thứ tự phải là: Fill -> kênh -> server -> appBar.
        Controls.Add(contentShell);
        Controls.Add(_channelPanel);
        Controls.Add(_serverPanel);
        Controls.Add(_appBar);

        InitSidebarAnimation();
    }

    private Panel BuildServerPanel()
    {
        var panel = new Panel
        {
            Dock = DockStyle.Left,
            Width = ServerExpandedWidth,
            BackColor = Color.FromArgb(48, 55, 76),
            Padding = new Padding(8, 12, 9, 12)
        };

        _serverHeader = new Panel { Dock = DockStyle.Top, Height = 34, BackColor = UiTheme.Surface };
        _serverHeading = new Label
        {
            Text = "SERVERS",
            Dock = DockStyle.Fill,
            ForeColor = UiTheme.Muted,
            Font = new Font("Segoe UI", 8.5F, FontStyle.Bold),
            TextAlign = ContentAlignment.MiddleLeft
        };
        _btnToggleServers = new Button { Text = "◀", Dock = DockStyle.Right, Width = 28 };
        UiTheme.StyleButton(_btnToggleServers, UiTheme.SurfaceRaised, UiTheme.Text);
        _btnToggleServers.Click += (_, _) => ToggleServerSidebar();
        _serverHeader.Controls.Add(_serverHeading);
        _serverHeader.Controls.Add(_btnToggleServers);

        StyleList(_servers);
        _servers.DisplayMember = nameof(ServerListItem.Name);
        _servers.SelectedIndexChanged += (_, _) => QueueServerSelection();

        _btnCreateServer = MakeButton("+ Tạo server", UiTheme.Primary);
        _btnCreateServer.Dock = DockStyle.Bottom;
        _btnCreateServer.Click += (_, _) => ShowCreateServer();

        _btnJoinServer = MakeButton("↗ Tham gia bằng link", UiTheme.SurfaceRaised);
        _btnJoinServer.Dock = DockStyle.Bottom;
        _btnJoinServer.Click += (_, _) => ShowJoinServer();

        panel.Controls.Add(_servers);
        panel.Controls.Add(_btnCreateServer);
        panel.Controls.Add(_btnJoinServer);
        panel.Controls.Add(_serverHeader);
        return panel;
    }

    private Panel BuildChannelPanel()
    {
        var panel = new Panel
        {
            Dock = DockStyle.Left,
            Width = ChannelExpandedWidth,
            BackColor = UiTheme.Surface,
            Padding = new Padding(9, 12, 8, 12)
        };

        _channelSplitter = new Panel
        {
            Dock = DockStyle.Right,
            Width = 6,
            BackColor = Color.Transparent,
            Cursor = Cursors.SizeWE
        };

        _channelSplitter.MouseDown += ChannelSplitter_MouseDown;
        _channelSplitter.MouseMove += ChannelSplitter_MouseMove;
        _channelSplitter.MouseUp += ChannelSplitter_MouseUp;

        panel.Controls.Add(_channelSplitter);

        _channelHeader = new Panel { Dock = DockStyle.Top, Height = 34, BackColor = UiTheme.Surface };
        _channelHeading = new Label
        {
            Text = "KÊNH",
            Dock = DockStyle.Fill,
            ForeColor = UiTheme.Muted,
            Font = new Font("Segoe UI", 8.5F, FontStyle.Bold),
            TextAlign = ContentAlignment.MiddleLeft
        };
        _btnToggleChannels = new Button { Text = "◀", Dock = DockStyle.Right, Width = 28 };
        UiTheme.StyleButton(_btnToggleChannels, UiTheme.SurfaceRaised, UiTheme.Text);
        _btnToggleChannels.Click += (_, _) => ToggleChannelSidebar();
       
          // Thêm nút "Tạo kênh"
        // 2. Nút tạo kênh (+) cho Dock Right và chỉnh kích thước phù hợp
        var btnCreateChannel = MakeButton("+", UiTheme.Primary);
        btnCreateChannel.Dock = DockStyle.Right; // 
        btnCreateChannel.Width = 28;             // <--- Đặt chiều rộng cho cân với nút ◀
        UiTheme.StyleButton(btnCreateChannel, UiTheme.Primary, Color.White);
        btnCreateChannel.Click += (_, _) => ShowCreateChannelDialog();


        _channelHeader.Controls.Add(_channelHeading);
        _channelHeader.Controls.Add(btnCreateChannel);
        _channelHeader.Controls.Add(_btnToggleChannels);
        


        // Danh sách kênh
        StyleList(_channels);
        _channels.SelectedIndexChanged += (_, _) => QueueChannelSelection();

     
        panel.Controls.Add(_channels);
        panel.Controls.Add(_channelHeader);
        return panel;
    }

    private void ShowCreateChannelDialog()
    {
        if (_selectedServer == null)
        {
            MessageBox.Show("Hãy chọn một server trước khi tạo kênh.", "Thông báo", MessageBoxButtons.OK, MessageBoxIcon.Information);
            return;
        }

        // Tạo Form Dialog tùy chỉnh
        using (var dialog = new Form())
        {
            dialog.Text = "Tạo kênh mới";
            dialog.Size = new Size(320, 230);
            dialog.StartPosition = FormStartPosition.CenterParent;
            dialog.FormBorderStyle = FormBorderStyle.FixedDialog;
            dialog.MaximizeBox = false;
            dialog.MinimizeBox = false;

            var lblName = new Label { Text = "Tên kênh:", Location = new Point(16, 12), AutoSize = true };
            var txtName = new TextBox { Location = new Point(16, 32), Width = 270 };

            var lblType = new Label { Text = "Loại kênh:", Location = new Point(16, 68), AutoSize = true };
            var rbText = new RadioButton { Text = "#  Kênh văn bản", Location = new Point(20, 88), Checked = true, AutoSize = true };
            var rbVoice = new RadioButton { Text = "🔊  Kênh đàm thoại", Location = new Point(140, 88), AutoSize = true };

            var btnOk = new Button { Text = "Tạo", Location = new Point(130, 140), Width = 75, DialogResult = DialogResult.OK };
            var btnCancel = new Button { Text = "Hủy", Location = new Point(210, 140), Width = 75, DialogResult = DialogResult.Cancel };

            dialog.Controls.AddRange(new Control[] { lblName, txtName, lblType, rbText, rbVoice, btnOk, btnCancel });
            dialog.AcceptButton = btnOk;

            if (dialog.ShowDialog(this) == DialogResult.OK)
            {
                string channelName = txtName.Text.Trim();

                if (!ChatLimits.IsValidChannelName(channelName))
                {
                    const int MaxChannelNameLength = 50;
                    MessageBox.Show($"Tên kênh phải có từ 1 đến {MaxChannelNameLength} ký tự và không chứa ký tự điều khiển.", "Tên kênh không hợp lệ", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                    return;
                }

                // Xác định loại kênh được chọn
                ServerChannelType selectedType = rbVoice.Checked ? ServerChannelType.Voice : ServerChannelType.Text;

                // Gửi yêu cầu tới Server
                _network.Send("create-server-channel", new CreateServerChannelRequest(
                    _selectedServer.Id, channelName, selectedType));

                SetStatus($"Đang tạo kênh {channelName}...");
            }
        }
    }
    /*
    private void PopulateChannelTree(List<ChannelListItem> channels)
    {
        _treeChannels.Nodes.Clear();

        // 1. Tạo node Cha cho Kênh Văn Bản
        var textCategoryNode = new TreeNode("v  KÊNH VĂN BẢN") { Tag = "category" };
        // 2. Tạo node Cha cho Kênh Đàm Thoại
        var voiceCategoryNode = new TreeNode("v  KÊNH ĐÀM THOẠI") { Tag = "category" };

        foreach (var ch in channels)
        {
            if (ch.Type == ServerChannelType.Text)
            {
                textCategoryNode.Nodes.Add(new TreeNode($"#  {ch.Label}") { Tag = ch });
            }
            else if (ch.Type == ServerChannelType.Voice)
            {
                voiceCategoryNode.Nodes.Add(new TreeNode($"🔊  {ch.Label}") { Tag = ch });
            }
        }

        _treeChannels.Nodes.Add(textCategoryNode);
        _treeChannels.Nodes.Add(voiceCategoryNode);

        _treeChannels.ExpandAll(); // Mở rộng tất cả các mục
    }*/

    private void ChannelSplitter_MouseDown(
    object? sender,
    MouseEventArgs e)
    {
        if (e.Button != MouseButtons.Left)
            return;

        _isResizingChannel = true;
        _resizeStartX = Cursor.Position.X;
        _resizeStartWidth = _channelPanel.Width;

        _channelSplitter.Capture = true;
    }

    private void ChannelSplitter_MouseMove(
        object? sender,
        MouseEventArgs e)
    {
        if (!_isResizingChannel)
            return;

        int delta = Cursor.Position.X - _resizeStartX;

        int newWidth = _resizeStartWidth + delta;

        newWidth = Math.Clamp(
            newWidth,
            ChannelMinWidth,
            ChannelMaxWidth
        );

        _channelPanel.Width = newWidth;

        _channelTargetWidth = newWidth;
    }

    private void ChannelSplitter_MouseUp(
        object? sender,
        MouseEventArgs e)
    {
        if (e.Button != MouseButtons.Left)
            return;

        _isResizingChannel = false;
        _channelSplitter.Capture = false;
    }
    private static void StyleList(ListBox box)
    {
        box.Dock = DockStyle.Fill;
        box.BackColor = UiTheme.Surface;
        box.ForeColor = UiTheme.Text;
        box.BorderStyle = BorderStyle.None;
        box.Font = new Font("Segoe UI Semibold", 10F);
        UiTheme.UseDarkScrollbars(box);
    }

    private static Button MakeButton(string text, Color color)
    {
        var button = new Button { Text = text, Margin = new Padding(0, 4, 0, 4) };
        UiTheme.StyleButton(button, color, UiTheme.Text);
        return button;
    }

    // ---------------- Settings ----------------

    private void OpenSettings()
    {
        if (_settingsOpen) return;

        ClientLog.Info($"OpenSettings start; activeChat={DescribeActiveChat()}; contentControls={_content.Controls.Count}; channels={_channels.Items.Count}");
        _settingsOpen = true;
        try
        {
            using var dialog = new SettingsDialog(_network, _username, Logout, ApplyPreferences);
            _settingsDialog = dialog;
            dialog.ShowDialog(this);
        }
        catch (Exception ex)
        {
            ClientLog.Error("OpenSettings failed", ex);
            throw;
        }
        finally
        {
            _settingsDialog = null;
            _settingsOpen = false;
            ClientLog.Info($"OpenSettings end; activeChat={DescribeActiveChat()}; contentControls={_content.Controls.Count}; channels={_channels.Items.Count}");
        }
    }

    private string DescribeActiveChat() => _activeChatForm == null
        ? "null"
        : $"disposed={_activeChatForm.IsDisposed}, parent={_activeChatForm.Parent?.GetType().Name ?? "null"}, visible={_activeChatForm.Visible}";

    private void ApplyPreferences(UserPreferences preferences)
    {
        _preferences = preferences;

        _suppressNavigationEvents = true;
        try
        {
            ApplyPreferencesCore(preferences);
        }
        finally
        {
            _suppressNavigationEvents = false;
        }
    }

    private void ApplyPreferencesCore(UserPreferences preferences)
    {

        var accent = ParsePreferenceColor(preferences.AccentColor, UiTheme.Primary);
        UiTheme.StyleButton(_btnCreateServer, accent, Color.White);

        var light = preferences.Theme == "light";
        var canvas = light ? Color.FromArgb(245, 247, 252) : UiTheme.Canvas;
        var surface = light ? Color.White : UiTheme.Surface;
        var text = light ? Color.FromArgb(26, 30, 46) : UiTheme.Text;
        var muted = light ? Color.FromArgb(110, 118, 140) : UiTheme.Muted;

        BackColor = canvas;
        _content.BackColor = canvas;
        if (_content.Parent != null) _content.Parent.BackColor = canvas;
        _status.ForeColor = muted;

        _serverPanel.BackColor = light ? Color.FromArgb(210, 214, 224) : Color.FromArgb(48, 55, 76);
        _serverHeader.BackColor = surface;
        _channelPanel.BackColor = surface;
        _channelHeader.BackColor = surface;
        _serverHeading.ForeColor = muted;
        _channelHeading.ForeColor = muted;

        foreach (var box in new[] { _servers, _channels })
        {
            box.BackColor = surface;
            box.ForeColor = text;
            box.Font = new Font("Segoe UI Semibold", preferences.ChatFontSize);
        }

        // Chữ trên trang nội dung cũng đổi màu theo theme
        foreach (Control control in _content.Controls)
        {
            if (control is Label label && label.ForeColor != muted) label.ForeColor = text;
        }
    }

    private static Color ParsePreferenceColor(string value, Color fallback)
    {
        try { return ColorTranslator.FromHtml(value); }
        catch { return fallback; }
    }

    private static UserPreferences CreatePreferences(UserProfile profile) => new()
    {
        Theme = profile.Theme,
        AccentColor = profile.AccentColor,
        ChatFontSize = profile.ChatFontSize,
        ShowAvatarsInChat = profile.ShowAvatarsInChat,
        ShowImagePreviews = profile.ShowImagePreviews,
        MessageSound = profile.MessageSound,
        MentionNotifications = profile.MentionNotifications,
        DirectMessageNotifications = profile.DirectMessageNotifications,
        OnlineNotifications = profile.OnlineNotifications
    };

    private void Logout()
    {
        var result = MessageBox.Show("Bạn có chắc chắn muốn đăng xuất?", "Đăng xuất", MessageBoxButtons.YesNo, MessageBoxIcon.Question);
        if (result != DialogResult.Yes) return;

        _network.Close();
        Invoke(() =>
        {
            var loginForm = new LoginForm();
            loginForm.Show();
            Close();
        });
    }

    private void ShowProfile(string username)
    {
        _profileDialogRequested = true;
        _network.Send("profile", new ProfileRequest(username));
    }

    private void StartDirectMessage(string username)
    {
        // Chat 1-1 vẫn nằm ở ChatForm; tạm báo trạng thái cho tới khi chuyển hẳn sang shell này.
        SetStatus($"Đang mở tin nhắn riêng với {username}...");
    }

    // ---------------- Sidebar toggle ----------------

    private void ToggleServerSidebar()
    {
        _isServerExpanded = !_isServerExpanded;
        _serverTargetWidth = _isServerExpanded ? ServerExpandedWidth : ServerCollapsedWidth;
        _btnToggleServers.Text = _isServerExpanded ? "◀" : "▶";
        _serverHeading.Visible = _isServerExpanded;
        _btnCreateServer.Text = _isServerExpanded ? "+ Tạo server" : "+";
        _btnJoinServer.Text = _isServerExpanded ? "↗ Tham gia bằng link" : "↗";
        _servers.DisplayMember = _isServerExpanded ? nameof(ServerListItem.Name) : nameof(ServerListItem.ShortName);
        RefreshList(_servers);
        _sidebarTimer.Start();
    }

    private void ToggleChannelSidebar()
    {
        _isChannelExpanded = !_isChannelExpanded;
        _channelTargetWidth = _isChannelExpanded ? ChannelExpandedWidth : ChannelCollapsedWidth;
        _btnToggleChannels.Text = _isChannelExpanded ? "◀" : "▶";
        _channelHeading.Visible = _isChannelExpanded;
        _channels.Visible = _isChannelExpanded;
        _sidebarTimer.Start();
    }

    /// <summary>Ép ListBox vẽ lại theo DisplayMember mới.</summary>
    private static void RefreshList(ListBox box)
    {
        var selected = box.SelectedIndex;
        var items = box.Items.Cast<object>().ToArray();
        box.BeginUpdate();
        box.Items.Clear();
        box.Items.AddRange(items);
        if (selected >= 0 && selected < box.Items.Count) box.SelectedIndex = selected;
        box.EndUpdate();
    }

    private void InitSidebarAnimation()
    {
        _sidebarTimer.Tick += (_, _) =>
        {
            bool done = true;
            done &= StepWidth(_serverPanel, _serverTargetWidth);
            done &= StepWidth(_channelPanel, _channelTargetWidth);
            if (done) _sidebarTimer.Stop();
        };
    }


    /// <summary>Kéo chiều rộng panel về target, trả về true khi đã tới nơi.</summary>
    private static bool StepWidth(Panel panel, int target)
    {
        int width = panel.Width;
        if (width == target) return true;
        width = width < target
            ? Math.Min(target, width + SlideStep)
            : Math.Max(target, width - SlideStep);
        panel.Width = width;
        return width == target;
    }

    // ---------------- Pages ----------------

    private void ShowHome()
    {
        _selectedServer = null;
        _channels.Items.Clear();
        var title = new Label { Text = $"Chào, {_username}", AutoSize = true, ForeColor = UiTheme.Text, Font = new Font("Segoe UI Semibold", 25F) };
        var copy = new Label
        {
            Text = "Chọn một server ở sidebar hoặc tạo server mới.",
            AutoSize = true, Top = 60, ForeColor = UiTheme.Muted, Font = new Font("Segoe UI", 11F)
        };
        var create = MakeButton("Tạo server", UiTheme.Primary);
        create.Location = new Point(0, 135);
        create.Click += (_, _) => ShowCreateServer();
        var join = MakeButton("Tham gia bằng mã mời", UiTheme.SurfaceRaised);
        join.Location = new Point(150, 135);
        join.Click += (_, _) => ShowJoinServer();
        SetPage(title, copy, create, join);
        SetStatus("Đang tải danh sách server...");
    }

    private void ShowCreateServer()
    {
        var title = PageTitle("Tạo RE:CHAT server");
        var explanation = PageCopy("Để bảo vệ cộng đồng mới, RE:CHAT sẽ gửi mã xác nhận đến Gmail đã đăng ký của bạn trước khi tạo server.", 52);
        var nameLabel = PageCopy("Tên server", 110, UiTheme.Text);
        _newServerName = new TextBox { Location = new Point(0, 136), Width = 390, PlaceholderText = "Ví dụ: Nhóm đồ án", MaxLength = 80 };
        UiTheme.StyleInput(_newServerName);
        var descLabel = PageCopy("Mô tả (không bắt buộc)", 185, UiTheme.Text);
        _newServerDescription = new TextBox { Location = new Point(0, 211), Width = 390, Height = 80, Multiline = true, MaxLength = 250 };
        UiTheme.StyleInput(_newServerDescription);
        var sendOtp = MakeButton("Gửi mã xác nhận Gmail", UiTheme.Primary);
        sendOtp.Location = new Point(0, 310);
        sendOtp.Click += (_, _) => RequestServerOtp();
        SetPage(title, explanation, nameLabel, _newServerName, descLabel, _newServerDescription, sendOtp);
        SetStatus("Điền thông tin server để tiếp tục.");
    }

    private void RequestServerOtp()
    {
        var name = _newServerName?.Text.Trim();
        if (string.IsNullOrWhiteSpace(name)) { SetStatus("Hãy nhập tên server.", true); return; }
        _network.Send("request-server-creation-otp", new CreateServerRequest(name, _newServerDescription?.Text.Trim()));
        SetStatus("Đang gửi mã xác nhận Gmail...");
    }

    private void ShowServerOtp()
    {
        var title = PageTitle("Xác nhận tạo server");
        var copy = PageCopy("Nhập mã gồm 6 chữ số vừa được gửi đến Gmail đã đăng ký. Mã có hiệu lực trong 10 phút.", 52);
        _serverOtp = new TextBox { Location = new Point(0, 112), Width = 230, MaxLength = 6, PlaceholderText = "Mã OTP" };
        UiTheme.StyleInput(_serverOtp);
        var verify = MakeButton("Tạo server", UiTheme.Primary);
        verify.Location = new Point(0, 164);
        verify.Click += (_, _) =>
        {
            if (_serverOtp?.Text.Trim().Length != 6) { SetStatus("Mã xác nhận cần đủ 6 chữ số.", true); return; }
            _network.Send("verify-server-creation", new VerifyServerCreationRequest(_serverOtp.Text.Trim()));
            SetStatus("Đang xác nhận và tạo server...");
        };
        SetPage(title, copy, _serverOtp, verify);
    }

    private void ShowJoinServer()
    {
        var title = PageTitle("Tham gia server");
        var copy = PageCopy("Dán mã mời hoặc phần mã ở cuối link mời RE:CHAT.", 52);
        var code = new TextBox { Location = new Point(0, 112), Width = 330, MaxLength = 32, PlaceholderText = "Mã mời" };
        UiTheme.StyleInput(code);
        var join = MakeButton("Tham gia", UiTheme.Primary);
        join.Location = new Point(0, 164);
        join.Click += (_, _) =>
        {
            if (string.IsNullOrWhiteSpace(code.Text)) { SetStatus("Hãy nhập mã mời.", true); return; }
            _network.Send("join-server-invite", new JoinServerInviteRequest(NormalizeInviteCode(code.Text)));
            SetStatus("Đang kiểm tra link mời...");
        };
        SetPage(title, copy, code, join);
        SetStatus("Nhập mã mời để tham gia một server.");
    }
    private void OpenAddFriendDialog()
    {
        // Không cần prefill username - đây là dialog để TÌM KIẾM bạn mới
        var dialog = new AddFriendDialog(_network, _username);
        if (dialog.ShowDialog(this) == DialogResult.OK)
        {
            // Refresh friend list nếu đang mở
            _network.Send("get-friends", new GetFriendsRequest());
        }
    }
    private void ShowCreateInvite()
    {
        if (_selectedServer == null) return;
        if (_selectedServer.Role is not (ServerRole.Owner or ServerRole.Admin))
        {
            SetStatus("Chỉ chủ server hoặc quản trị viên mới có thể tạo link mời.", true);
            return;
        }

        var maxUsesText = PromptInputBox.Show("Tạo link mời", "Số lượt sử dụng (để trống = không giới hạn):", "");
        if (maxUsesText == null) return;
        if (!string.IsNullOrWhiteSpace(maxUsesText) && (!int.TryParse(maxUsesText.Trim(), out var maxUses) || maxUses <= 0))
        {
            SetStatus("Số lượt sử dụng phải là số nguyên dương.", true);
            return;
        }

        var expiresText = PromptInputBox.Show("Tạo link mời", "Thời hạn theo phút (để trống = không hết hạn):", "");
        if (expiresText == null) return;
        if (!string.IsNullOrWhiteSpace(expiresText) && (!int.TryParse(expiresText.Trim(), out var expiresInMinutes) || expiresInMinutes <= 0))
        {
            SetStatus("Thời hạn phải là số nguyên dương.", true);
            return;
        }

        int? requestedMaxUses = string.IsNullOrWhiteSpace(maxUsesText) ? null : int.Parse(maxUsesText.Trim());
        int? requestedExpires = string.IsNullOrWhiteSpace(expiresText) ? null : int.Parse(expiresText.Trim());
        _network.Send("create-server-invite", new CreateServerInviteRequest(_selectedServer.Id, requestedMaxUses, requestedExpires));
        SetStatus("Đang tạo link mời...");
    }

    private static string NormalizeInviteCode(string value)
    {
        var trimmed = value.Trim();
        if (Uri.TryCreate(trimmed, UriKind.Absolute, out var uri) && uri.Segments.Length > 0)
            return uri.Segments[^1].Trim('/');

        var slash = trimmed.LastIndexOf('/');
        return slash >= 0 ? trimmed[(slash + 1)..].Split('?', '#')[0] : trimmed;
    }

    private void SelectServer()
    {
        if (_updatingLists || _navigationInProgress) return;
        if (_suppressNavigationEvents)
        {
            ClientLog.Info("SelectServer ignored during preferences refresh");
            return;
        }

        if (_servers.SelectedItem is not ServerListItem item) return;
        _selectedServer = item.Server;
        _channels.Items.Clear();
        _network.Send("get-server-channels", new GetServerChannelsRequest(item.Server.Id));
        var title = PageTitle(item.Server.Name);
        var copy = PageCopy(item.Server.Description ?? "Chọn một kênh để xem nội dung.", 52);
        var invite = MakeButton("Tạo link mời", UiTheme.Primary);
        invite.Location = new Point(0, 110);
        invite.Width = 180;
        invite.Visible = item.Server.Role is ServerRole.Owner or ServerRole.Admin;
        invite.Click += (_, _) => ShowCreateInvite();
        SetPage(title, copy, invite);
        SetStatus("Đang tải kênh...");
    }

    private void SelectChannel()
    {
        if (_updatingLists || _navigationInProgress) return;
        if (_suppressNavigationEvents)
        {
            ClientLog.Info("SelectChannel ignored during preferences refresh");
            return;
        }

        if (_channels.SelectedItem is not ChannelListItem item || _selectedServer == null) return;
        ClientLog.Info($"SelectChannel start server={_selectedServer.Id} channel={item.Channel.Id}; activeChat={DescribeActiveChat()}; contentControls={_content.Controls.Count}");
        var title = PageTitle(item.Channel.Type == ServerChannelType.Voice ? $"🔊 {item.Channel.Name}" : $"# {item.Channel.Name}");
        var detail = item.Channel.Type switch
        {
            ServerChannelType.Voice => "Kênh thoại đã được tạo. Luồng âm thanh thời gian thực sẽ được kết nối ở bước tiếp theo.",
            ServerChannelType.Announcement => "Kênh thông báo của server.",
            _ => "Kênh văn bản đã sẵn sàng. Lịch sử và gửi tin nhắn sẽ được chuyển sang ChannelId trong bước chat server tiếp theo."
        };
        _navigationInProgress = true;
        try
        {
            ClearContent();

            if (item.Channel.Type == ServerChannelType.Voice)
            {
                _activeLiveForm = new LiveForm(_network, _username, item.Channel.Id, () => _settingsOpen, ShowSelectedServerHome)
                {
                    Dock = DockStyle.Fill
                };
                _content.Controls.Add(_activeLiveForm);
                ClientLog.Info($"SelectChannel created LiveForm channel={item.Channel.Id}; contentControls={_content.Controls.Count}");
            }
            else
            {
                _activeChatForm = new ChatForm(_network, _username, item.Channel.Id, () => _settingsOpen)
                {
                    Dock = DockStyle.Fill
                };
                _content.Controls.Add(_activeChatForm);
                ClientLog.Info($"SelectChannel created ChatForm channel={item.Channel.Id}; activeChat={DescribeActiveChat()}; contentControls={_content.Controls.Count}");
            }
            if (item.Channel.Type != ServerChannelType.Voice)
                _network.Send("join", new JoinRequest(item.Channel.Id));
            SetStatus($"{_selectedServer.Name} · {item.Channel.Name}");
        }
        finally
        {
            _navigationInProgress = false;
        }
    }

    private void QueueServerSelection()
    {
        if (_updatingLists || _suppressNavigationEvents || _serverSelectionQueued) return;
        _serverSelectionQueued = true;
        BeginInvoke(() =>
        {
            _serverSelectionQueued = false;
            if (!IsDisposed) SelectServer();
        });
    }

    private void QueueChannelSelection()
    {
        if (_updatingLists || _suppressNavigationEvents || _channelSelectionQueued) return;
        _channelSelectionQueued = true;
        BeginInvoke(() =>
        {
            _channelSelectionQueued = false;
            if (!IsDisposed) SelectChannel();
        });
    }

    // ---------------- Networking ----------------

    private void OnMessageReceived(Envelope envelope)
    {
        if (IsDisposed) return;
        if (InvokeRequired) { BeginInvoke(() => OnMessageReceived(envelope)); return; }
        switch (envelope.Type)
        {
            case "my-servers":
                UpdateServers(envelope.As<MyServersResponse>()?.Servers ?? []);
                break;

            case "server-creation-otp-sent":
                ShowServerOtp();
                SetStatus(envelope.As<ServerCreationOtpSentResponse>()?.Message ?? "Đã gửi mã xác nhận.");
                break;

            case "server-created":
                var created = envelope.As<ServerCreatedResponse>();
                if (created != null) AddOrSelectServer(created.Server, created.Channels);
                break;

            case "server-invite-accepted":
                var accepted = envelope.As<ServerInviteAcceptedResponse>();
                if (accepted != null) AddOrSelectServer(accepted.Server, accepted.Channels);
                break;

            case "server-invite":
                var invite = envelope.As<ServerInviteInfo>();
                if (invite != null) ShowCreatedInvite(invite);
                break;

            case "server-channels":
                var channelResponse = envelope.As<ServerChannelsResponse>();
                if (channelResponse != null && _selectedServer?.Id == channelResponse.ServerId) UpdateChannels(channelResponse.Channels);
                break;

            case "voice-channel-state":
                var voiceState = envelope.As<VoiceChannelState>();
                if (voiceState != null) _activeLiveForm?.UpdateVoiceState(voiceState);
                break;

            case "livekit-credentials":
                var credentials = envelope.As<LiveKitCredentials>();
                if (credentials != null) _activeLiveForm?.ConfigureLiveKit(credentials);
                break;

            // ----- 3 case dưới đây bắt buộc phải có, nếu thiếu SettingsDialog sẽ đứng chờ mãi -----
            case "profile":
                ClientLog.Info($"UserMain received profile; settingsOpen={_settingsOpen}; settingsDialog={_settingsDialog != null}; activeChat={DescribeActiveChat()}");
                var profile = envelope.As<ProfileResponse>()?.Profile;
                if (profile != null && string.Equals(profile.Username, _username, StringComparison.OrdinalIgnoreCase))
                    ApplyPreferences(CreatePreferences(profile));
                if (_settingsDialog != null)
                {
                    _settingsDialog.HandleProfile(profile);
                    break;
                }
                if (!_profileDialogRequested) break;
                _profileDialogRequested = false;
                if (profile == null)
                    MessageBox.Show("Không tìm thấy hồ sơ người dùng.", "Hồ sơ", MessageBoxButtons.OK, MessageBoxIcon.Information);
                else
                    ProfileDialog.Show(_network,profile, string.Equals(profile.Username, _username, StringComparison.OrdinalIgnoreCase), StartDirectMessage);
                break;

            case "settings-updated":
                var updated = envelope.As<SettingsUpdatedResponse>();
                if (_settingsDialog != null && updated != null)
                    _settingsDialog.HandleSettingsUpdated(updated.Message);
                break;

            case "password-otp-sent":
                _settingsDialog?.HandlePasswordOtpSent();
                break;
            case "error":
                SetStatus(envelope.As<ErrorResponse>()?.Message ?? "Có lỗi xảy ra.", true);
                break;
        }
    }

    private void UpdateServers(List<ServerSummary> servers)
    {
        _serverById.Clear();
        _updatingLists = true;
        try
        {
            _servers.Items.Clear();
            foreach (var server in servers.OrderBy(x => x.Name))
            {
                _serverById[server.Id] = server;
                _servers.Items.Add(new ServerListItem(server));
            }
        }
        finally
        {
            _updatingLists = false;
        }
        SetStatus(servers.Count == 0 ? "Bạn chưa tham gia server nào." : $"Bạn có {servers.Count} server.");
    }

    private void AddOrSelectServer(ServerSummary server, List<ServerChannelInfo> channels)
    {
        _serverById[server.Id] = server;
        var existing = _servers.Items.OfType<ServerListItem>().FirstOrDefault(x => x.Server.Id == server.Id);
        if (existing == null) _servers.Items.Add(new ServerListItem(server));
        _selectedServer = server;
        _servers.SelectedItem = _servers.Items.OfType<ServerListItem>().First(x => x.Server.Id == server.Id);
        UpdateChannels(channels);
        SetStatus($"Đã vào server {server.Name}.");
    }

    private void UpdateChannels(List<ServerChannelInfo> channels)
    {
        ClientLog.Info($"UpdateChannels count={channels.Count}; selectedServer={_selectedServer?.Id ?? "null"}; activeChat={DescribeActiveChat()}");
        _updatingLists = true;
        try
        {
            _channels.Items.Clear();
            AddChannelGroup("KÊNH VĂN BẢN", channels.Where(x => x.Type == ServerChannelType.Text));
            AddChannelGroup("KÊNH ĐÀM THOẠI", channels.Where(x => x.Type == ServerChannelType.Voice));
            AddChannelGroup("KÊNH THÔNG BÁO", channels.Where(x => x.Type == ServerChannelType.Announcement));
        }
        finally
        {
            _updatingLists = false;
        }
        SetStatus(channels.Count == 0 ? "Server chưa có kênh." : "Chọn một kênh để tiếp tục.");
    }

    private void ShowSelectedServerHome()
    {
        if (_selectedServer == null)
        {
            ShowHome();
            return;
        }

        var server = _selectedServer;
        var title = PageTitle(server.Name);
        var copy = PageCopy(server.Description ?? "Chọn một kênh để xem nội dung.", 52);
        SetPage(title, copy);
        SetStatus($"Đã rời kênh thoại trong {server.Name}.");
    }

    private void AddChannelGroup(string title, IEnumerable<ServerChannelInfo> channels)
    {
        var orderedChannels = channels.OrderBy(x => x.Position).ToList();
        if (orderedChannels.Count == 0) return;

        _channels.Items.Add(new ChannelGroupItem(title));
        foreach (var channel in orderedChannels)
            _channels.Items.Add(new ChannelListItem(channel));
    }

    private void OnDisconnected(string reason)
    {
        if (IsDisposed) return;
        if (InvokeRequired) { BeginInvoke(() => OnDisconnected(reason)); return; }
        SetStatus(reason, true);
    }

    // ---------------- Helpers ----------------

    private void SetPage(params Control[] controls)
    {
        ClearContent();
        _content.Controls.AddRange(controls);
    }

    private void ClearContent()
    {
        if (_settingsOpen && _activeChatForm != null)
        {
            ClientLog.Error($"ClearContent blocked while Settings is open; activeChat={DescribeActiveChat()}");
            return;
        }

        ClientLog.Info($"ClearContent caller={Environment.StackTrace}");
        ClientLog.Info($"ClearContent before; activeChat={DescribeActiveChat()}; contentControls={_content.Controls.Count}");
        if (_activeChatForm != null)
        {
            _content.Controls.Remove(_activeChatForm);
            _activeChatForm.Dispose();
            _activeChatForm = null;
        }
        if (_activeLiveForm != null)
        {
            _content.Controls.Remove(_activeLiveForm);
            _activeLiveForm.Dispose();
            _activeLiveForm = null;
        }
        _content.Controls.Clear();
        ClientLog.Info($"ClearContent after; contentControls={_content.Controls.Count}");
    }

    private void SetStatus(string text, bool error = false)
    {
        _status.Text = text;
        _status.ForeColor = error ? Color.FromArgb(251, 146, 160) : UiTheme.Muted;
    }

    private static void ShowCreatedInvite(ServerInviteInfo invite)
    {
        var link = $"rechat://invite/{invite.Code}";
        var details = invite.ExpiresAt is null
            ? "Không hết hạn"
            : $"Hết hạn: {invite.ExpiresAt.Value.LocalDateTime:g}";
        if (invite.MaxUses is not null) details += $"\nSố lượt: {invite.MaxUses.Value}";

        var result = MessageBox.Show(
            $"Link mời:\n{link}\n\n{details}\n\nBạn có muốn sao chép link này không?",
            "Đã tạo link mời",
            MessageBoxButtons.YesNo,
            MessageBoxIcon.Information);
        if (result == DialogResult.Yes) Clipboard.SetText(link);
    }

    private static Label PageTitle(string text) => new() { Text = text, AutoSize = true, ForeColor = UiTheme.Text, Font = new Font("Segoe UI Semibold", 24F), Location = Point.Empty };
    private static Label PageCopy(string text, int top, Color? color = null) => new() { Text = text, AutoSize = true, MaximumSize = new Size(620, 0), Location = new Point(0, top), ForeColor = color ?? UiTheme.Muted, Font = new Font("Segoe UI", 10.5F) };

    private sealed record ServerListItem(ServerSummary Server)
    {
        public string Name => Server.Name;
        public override string ToString() => Server.Name;
        public string ShortName => string.IsNullOrWhiteSpace(Server.Name)
            ? "?"
            : Server.Name[0].ToString().ToUpper();
    }

    private sealed record ChannelListItem(ServerChannelInfo Channel)
    {
        public string Label => Channel.Type == ServerChannelType.Voice ? $"🔊  {Channel.Name}" : $"#  {Channel.Name}";
        public override string ToString() => Label;
    }

    private sealed record ChannelGroupItem(string Title)
    {
        public override string ToString() => $"── {Title} ──";
    }
}
