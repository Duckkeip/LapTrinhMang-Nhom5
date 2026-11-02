using System.Globalization;
using ChatProtocol;

namespace ChatClient;

public class ChatForm : UserControl
{
    private static readonly Color BackgroundColor = UiTheme.Canvas;
    private static readonly Color PanelColor = UiTheme.Surface;
    private static readonly Color InputColor = UiTheme.SurfaceRaised;
    private static readonly Color AccentColor = UiTheme.Primary;
    private static readonly Color TextColor = UiTheme.Text; // Define TextColor

    // Màu đen với độ trong suốt 50% (Alpha = 128)
private static readonly Color mauchu = Color.FromArgb(128, 45, 123, 197); // Define TextColor

    private readonly NetworkClient _network;
    private readonly string _username;
    private readonly Func<bool>? _ignoreProfileResponses;
    private string _currentRoom;
    private UserPreferences _preferences = UserPreferences.Load();

    private readonly ListBox _lstUsers = new();
    private readonly Panel _rightPanel = new() { Dock = DockStyle.Right, Width = 200, BackColor = PanelColor };
    private readonly Panel _rightContent = new() { Dock = DockStyle.Fill, Padding = new Padding(14, 10, 14, 14) };
    private readonly Button _btnToggleRight = new() { Text = "›", Dock = DockStyle.Left, Width = 34 };
    private readonly System.Windows.Forms.Timer _sidePanelTimer = new() { Interval = 15 };
    private readonly RichTextBox _txtChatLog = new() 
        {
        Dock = DockStyle.Fill,
        ReadOnly = true,
        BorderStyle = BorderStyle.None,
        BackColor = Color.FromArgb(30, 34, 45),
        ForeColor = mauchu,
        Font = new Font("Times New Roman", 10.5F),
        Multiline = true,
        WordWrap = true,
        ScrollBars = RichTextBoxScrollBars.Vertical
    };
    private readonly FlowLayoutPanel _messagesPanel = new() { Dock = DockStyle.Fill, AutoScroll = true, FlowDirection = FlowDirection.TopDown, WrapContents = false, BackColor = BackgroundColor, Padding = new Padding(8, 8, 8, 8) };
    private readonly TextBox _txtInput = new() { Dock = DockStyle.Fill, MaxLength = ChatLimits.MaxChatTextLength };
    private readonly Button _btnSend = new() { Text = "Gửi", Dock = DockStyle.Right, Width = 80 };
    private readonly Button _btnAttach = new() { Text = "📎", Dock = DockStyle.Right, Width = 48 };
    private readonly Button _btnProfile = new() { Dock = DockStyle.Right, Width = 120 };
    private readonly ContextMenuStrip _userMenu = new();
    private readonly ListBox _lstFiles = new();
    private readonly Button _btnDownloadFile = new() { Text = "↓ Tải tệp đã chọn", Dock = DockStyle.Bottom, Height = 34 };
    private readonly Dictionary<string, PendingDownload> _downloads = new();
    private readonly List<FileLinkRange> _fileLinks = new();
    
    private readonly Dictionary<string, List<string>> _messageReactions = new(); // MessageId -> reactions currently displayed
    private readonly Dictionary<string, string> _messageTexts = new();
    private readonly ProgressBar _transferProgress = new() { Dock = DockStyle.Fill, Style = ProgressBarStyle.Continuous, Maximum = 100 };
    private readonly Label _transferLabel = new() { Dock = DockStyle.Left, Width = 175, TextAlign = ContentAlignment.MiddleLeft, ForeColor = UiTheme.Muted, Font = new Font("Times New Roman", 8.5F) };
    private readonly Panel _transferPanel = new() { Dock = DockStyle.Bottom, Height = 26, Visible = false, Padding = new Padding(0, 2, 0, 2) };
    private readonly Label _lblTyping = new() { Dock = DockStyle.Bottom, Height = 20, ForeColor = Color.Gray };
    private readonly System.Windows.Forms.Timer _typingStopTimer = new() { Interval = 1500 };
    
    private Panel? _panelBeingAnimated;

    private int _panelTargetWidth;

    public ChatForm(NetworkClient network, string username, string room, Func<bool>? ignoreProfileResponses = null)
    {
        _network = network;
        _username = username;
        _ignoreProfileResponses = ignoreProfileResponses;
        _currentRoom = room;
        ClientLog.Info($"ChatForm created room={room}; ignoreProfileCallback={ignoreProfileResponses != null}");

        Text = $"RE:CHAT · {room}";
        

        BuildLayout();

        //các sự kiện
        
        _btnProfile.Text = $"◉ {_username}";
        _btnProfile.Click += (_, _) => ShowProfile(_username);
        _btnAttach.Click += BtnAttach_Click;
        _btnDownloadFile.Click += (_, _) => DownloadSelectedFile();
        _btnToggleRight.Click += (_, _) => ToggleSidebar(_rightPanel, _rightContent, _btnToggleRight, 200, "›", "☰");
        _userMenu.Items.Add("Xem hồ sơ", null, (_, _) => ShowSelectedUserProfile());
        _userMenu.Items.Add("Nhắn tin riêng", null, (_, _) => StartDirectMessageToSelectedUser());
        
        _userMenu.Items.Add("Kết bạn", null, (_, _) => OpenAddFriendForSelectedUser());
        
        _lstUsers.MouseDown += LstUsers_MouseDown;
        _lstUsers.DoubleClick += (_, _) => ShowSelectedUserProfile();
        _txtChatLog.MouseClick += TxtChatLog_MouseClick;
        _transferPanel.Controls.Add(_transferProgress);
        _transferPanel.Controls.Add(_transferLabel);

        
        

        // Gắn sự kiện cho các nút
        //hiện mở giao diện đăng ký
        // Thêm nút "Đăng ký" vào giao diện chính


        _network.MessageReceived += OnMessageReceived;
        _network.Disconnected += OnDisconnected;
        _network.Send("get-unread-direct-messages");

        _typingStopTimer.Tick += (_, _) =>
        {
            _typingStopTimer.Stop();
            _network.Send("typing", new TypingRequest(false));
        };
        _sidePanelTimer.Tick += (_, _) => AnimateSidebar();

    }

    private void BuildLayout()
    {
        var rightHeader = CreateSidebarHeader("", UiTheme.Mint, _btnToggleRight);
        rightHeader.Padding = new Padding(5, 2, 0, 0);
        _rightContent.Controls.Add(_lstUsers);
        _rightContent.Controls.Add(new Label { Text = "●  ĐANG TRỰC TUYẾN", Dock = DockStyle.Top, Height = 40, ForeColor = UiTheme.Mint, Font = new Font("Times New Roman Semibold", 8.5F), TextAlign = ContentAlignment.MiddleLeft });
        _lstUsers.Dock = DockStyle.Fill;
        _lstUsers.DrawMode = DrawMode.OwnerDrawFixed;
        _lstUsers.MeasureItem += LstUsers_MeasureItem;
        _lstUsers.DrawItem += LstUsers_DrawItem;
        StyleList(_lstUsers);

        var filesPanel = new Panel { Dock = DockStyle.Bottom, Height = 170, BackColor = PanelColor, Padding = new Padding(0, 8, 0, 0) };
        filesPanel.Controls.Add(_lstFiles);
        filesPanel.Controls.Add(_btnDownloadFile);
        filesPanel.Controls.Add(new Label { Text = "TỆP TRONG PHÒNG", Dock = DockStyle.Top, Height = 26, ForeColor = UiTheme.Muted, Font = new Font("Times New Roman Semibold", 8.5F), TextAlign = ContentAlignment.MiddleLeft });
        _lstFiles.Dock = DockStyle.Fill;
        StyleList(_lstFiles);
        _lstFiles.ItemHeight = 27;
        UiTheme.StyleButton(_btnDownloadFile, UiTheme.SurfaceRaised, UiTheme.Text);
        _btnDownloadFile.Height = 34;
        _rightContent.Controls.Add(filesPanel);

        var header = new Panel { Dock = DockStyle.Top, Height = 72, BackColor = BackgroundColor, Padding = new Padding(0, 25, 0, 0) };


        var inputPanel = new CardPanel { Dock = DockStyle.Bottom,Height = 65 , CornerRadius = 12 };
        
        inputPanel.Controls.Add(_txtInput);
        inputPanel.Controls.Add(_btnSend);
        inputPanel.Controls.Add(_btnAttach);
        _txtInput.BackColor = InputColor;
        _txtInput.ForeColor = Color.White;
        _txtInput.BorderStyle = BorderStyle.None;
        _txtInput.Font = new Font("Times New Roman", 10.5F);
        _txtInput.Margin = new Padding(8, 2, 8, 2);
        _btnSend.BackColor = AccentColor;
        _btnSend.ForeColor = Color.White;
        UiTheme.StyleButton(_btnSend, AccentColor, Color.White);
        UiTheme.StyleButton(_btnAttach, UiTheme.SurfaceRaised, UiTheme.Text);
        _txtInput.PlaceholderText = "Nhập tin nhắn...";
        _txtInput.TextChanged += TxtInput_TextChanged;
        _txtInput.Multiline = true;
        _txtInput.AcceptsReturn = true;
        _txtInput.ScrollBars = ScrollBars.Vertical;
        _txtInput.AutoSize = false;
        _txtInput.KeyDown += (_, e) =>
        {
            if (e.KeyCode == Keys.Enter && !e.Shift)
            {
                e.SuppressKeyPress = true;
                e.Handled = true;

                SendMessage();
            }
        };
        _btnSend.Click += (_, _) => SendMessage();

        var centerPanel = new Panel { Dock = DockStyle.Fill, BackColor = BackgroundColor, Padding = new Padding(5, 3, 8, 0) };
        centerPanel.Controls.Add(_messagesPanel);
        centerPanel.Controls.Add(_txtChatLog);
        centerPanel.Controls.Add(inputPanel);
        centerPanel.Controls.Add(_transferPanel);
        centerPanel.Controls.Add(_lblTyping);
        UiTheme.StyleButton(_btnProfile, UiTheme.SurfaceRaised, UiTheme.Text);
        _btnProfile.Height = 34;
        header.Controls.Add(_btnProfile);

        header.Controls.Add(new Label { Name = "RoomTitle", Text = $"# {_currentRoom}", Dock = DockStyle.Top, Height = 32, ForeColor = UiTheme.Text, Font = new Font("Times New Roman Semibold", 17F) });
        header.Controls.Add(new Label { Text = "Nơi mọi người cùng trao đổi và chia sẻ.", Dock = DockStyle.Bottom, Height = 20, ForeColor = UiTheme.Muted, Font = new Font("Times New Roman", 9F) });
        centerPanel.Controls.Add(header);
        
     

        _txtChatLog.BackColor = BackgroundColor;
        _txtChatLog.ForeColor = Color.FromArgb(225, 229, 242);
        _txtChatLog.BorderStyle = BorderStyle.None;
        _txtChatLog.Font = new Font("Times New Roman", 10F);
        UiTheme.UseDarkScrollbars(_txtChatLog);
        UiTheme.UseDarkScrollbars(_messagesPanel);
        _lblTyping.ForeColor = Color.Silver;
        _rightPanel.Controls.Add(_rightContent);
        _rightPanel.Controls.Add(rightHeader);

        Controls.Add(centerPanel);
        Controls.Add(_rightPanel);
    }
    private void ApplyPreferences(UserPreferences preferences)
    {
        _preferences = preferences;
        var accent = ParsePreferenceColor(preferences.AccentColor, UiTheme.Primary);
        UiTheme.StyleButton(_btnSend, accent, Color.White);
        _txtInput.Font = new Font("Times New Roman", preferences.ChatFontSize);

        foreach (Control control in _messagesPanel.Controls)
            ApplyMessagePreferences(control, preferences);

        var light = preferences.Theme == "light";
        var canvas = light ? Color.FromArgb(245, 247, 252) : UiTheme.Canvas;
        var surface = light ? Color.White : UiTheme.Surface;
        BackColor = canvas;
        _messagesPanel.BackColor = canvas;
        _rightPanel.BackColor = surface;
    }

    private static void ApplyMessagePreferences(Control control, UserPreferences preferences)
    {
        if (control is not TableLayoutPanel row) return;
        if (row.Controls.Count > 0 && row.Controls[0] is PictureBox avatar)
        {
            avatar.Visible = preferences.ShowAvatarsInChat;
            row.ColumnStyles[0].Width = preferences.ShowAvatarsInChat ? 34 : 0;
        }

        foreach (Control child in row.Controls)
        {
            if (child is Label bubble) bubble.Font = new Font("Times New Roman", preferences.ChatFontSize);
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
   
    private static void StyleList(ListBox list)
    {
        list.BackColor = PanelColor;
        list.ForeColor = Color.FromArgb(220, 224, 240);
        list.BorderStyle = BorderStyle.None;
        list.ItemHeight = 34;
        list.Font = new Font("Times New Roman", 9.5F);
        UiTheme.UseDarkScrollbars(list);
    }

    private static Panel CreateSidebarHeader(string title, Color color, Button toggle)
    {
        UiTheme.StyleButton(toggle, UiTheme.SurfaceRaised, UiTheme.Text);
        toggle.Height = 32;
        var header = new Panel { Dock = DockStyle.Top, Height = 54, Padding = new Padding(12, 10, 8, 8) };
        header.Controls.Add(toggle);
        header.Controls.Add(new Label { Text = title, Dock = DockStyle.Fill, ForeColor = color, Font = new Font("Times New Roman Semibold", 10F), TextAlign = ContentAlignment.MiddleLeft });
        return header;
    }

    private void ToggleSidebar(Panel panel, Panel content, Button toggle, int expandedWidth, string expandedGlyph, string collapsedGlyph)
    {
        var isCollapsed = panel.Width <= 52;
        _panelBeingAnimated = panel;
        _panelTargetWidth = isCollapsed ? expandedWidth : 48;
        if (isCollapsed) content.Visible = true;
        toggle.Text = isCollapsed ? expandedGlyph : collapsedGlyph;
        _sidePanelTimer.Start();
    }
    private void OpenAddFriendForSelectedUser()
    {
        if (_lstUsers.SelectedItem is UserListItem user)
        {
            // Dùng constructor 3 tham số mới - KHÔNG dùng reflection
            var dialog = new AddFriendDialog(_network, _username, user.Username);
            dialog.ShowDialog(this);
        }
    }
    private void AnimateSidebar()
    {
        if (_panelBeingAnimated == null) { _sidePanelTimer.Stop(); return; }
        var distance = _panelTargetWidth - _panelBeingAnimated.Width;
        if (Math.Abs(distance) <= 12)
        {
            _panelBeingAnimated.Width = _panelTargetWidth;
            _rightContent.Visible = _panelTargetWidth > 52;
            _panelBeingAnimated = null;
            _sidePanelTimer.Stop();
            return;
        }
        _panelBeingAnimated.Width += Math.Sign(distance) * Math.Max(12, Math.Abs(distance) / 4);
    }

    private void SendMessage()
    {
        var text = _txtInput.Text.Trim();
        if (text.Length == 0) return;
        if (!ChatLimits.IsValidChatText(text))
        {
            MessageBox.Show($"Tin nhắn tối đa {ChatLimits.MaxChatTextLength} ký tự.", "Tin nhắn quá dài", MessageBoxButtons.OK, MessageBoxIcon.Warning);
            return;
        }
        _network.Send("chat", new ChatRequest(text));
        _txtInput.Clear();
    }

    private async void BtnAttach_Click(object? sender, EventArgs e)
    {
        using var dialog = new OpenFileDialog { Title = "Chọn tệp để gửi" };
        if (dialog.ShowDialog(this) != DialogResult.OK) return;
        var info = new FileInfo(dialog.FileName);
        if (info.Length <= 0 || info.Length > ChatLimits.MaxFileBytes)
        {
            MessageBox.Show("Chỉ có thể gửi tệp có dung lượng tối đa 12 MB.", "Tệp quá lớn", MessageBoxButtons.OK, MessageBoxIcon.Warning);
            return;
        }

        _btnAttach.Enabled = false;
        try
        {
            await UploadFileAsync(info);
            SetTransferProgress("Đang hoàn tất gửi tệp…", 100);
        }
        catch (Exception ex)
        {
            MessageBox.Show($"Không thể gửi tệp: {ex.Message}", "Lỗi gửi tệp", MessageBoxButtons.OK, MessageBoxIcon.Error);
        }
        finally
        {
            _btnAttach.Enabled = true;
            HideTransferProgress();
        }
    }

    private async Task UploadFileAsync(FileInfo info)
    {
        var uploadId = Guid.NewGuid().ToString();
        var thumbnail = CreateImageThumbnail(info);
        await _network.SendAsync("file-upload-start", new FileUploadStartRequest(uploadId, info.Name, info.Length, thumbnail));
        var buffer = new byte[ChatLimits.FileChunkBytes];
        var index = 0;
        long sent = 0;
        SetTransferProgress($"Đang gửi {info.Name}", 0);
        await using var stream = info.OpenRead();
        int read;
        while ((read = await stream.ReadAsync(buffer.AsMemory(0, buffer.Length))) > 0)
        {
            await _network.SendAsync("file-upload-chunk", new FileUploadChunkRequest(uploadId, index++, Convert.ToBase64String(buffer, 0, read)));
            sent += read;
            SetTransferProgress($"Đang gửi {info.Name}", (int)(sent * 100 / info.Length));
        }
        await _network.SendAsync("file-upload-complete", new FileUploadCompleteRequest(uploadId));
    }

    private static string? CreateImageThumbnail(FileInfo info)
    {
        if (!new[] { ".png", ".jpg", ".jpeg", ".bmp", ".gif" }.Contains(info.Extension, StringComparer.OrdinalIgnoreCase)) return null;
        try
        {
            using var source = Image.FromFile(info.FullName);
            var scale = Math.Min(180d / source.Width, 120d / source.Height);
            var width = Math.Max(1, (int)(source.Width * Math.Min(1, scale)));
            var height = Math.Max(1, (int)(source.Height * Math.Min(1, scale)));
            using var thumbnail = new Bitmap(width, height);
            using (var graphics = Graphics.FromImage(thumbnail))
            {
                graphics.Clear(Color.White);
                graphics.InterpolationMode = System.Drawing.Drawing2D.InterpolationMode.HighQualityBicubic;
                graphics.DrawImage(source, 0, 0, width, height);
            }
            using var output = new MemoryStream();
            thumbnail.Save(output, System.Drawing.Imaging.ImageFormat.Jpeg);
            return output.Length <= 250_000 ? Convert.ToBase64String(output.ToArray()) : null;
        }
        catch { return null; }
    }

    private void TxtInput_TextChanged(object? sender, EventArgs e)
    {
        _network.Send("typing", new TypingRequest(true));
        _typingStopTimer.Stop();
        _typingStopTimer.Start();
    }

    private void LstRooms_Click(object? sender, EventArgs e)
    {
        SwitchToSelectedRoom();
    }

    private void LstRooms_KeyDown(object? sender, KeyEventArgs e)
    {
        if (e.KeyCode != Keys.Enter) return;
        e.SuppressKeyPress = true;
        SwitchToSelectedRoom();
    }

    private void SwitchToSelectedRoom()
    {
        Text = $"RE:CHAT · {_currentRoom}";
        if (Controls.Find("RoomTitle", true).FirstOrDefault() is Label roomTitle) roomTitle.Text = $"# {_currentRoom}";

        // Không để tin nhắn, tệp và trạng thái nhập của phòng cũ xuất hiện
        // trong phòng mới trong lúc server gửi lịch sử của phòng được chọn.
        _messagesPanel.Controls.Clear();
        _messageReactions.Clear();
        _messageTexts.Clear();
        _lstFiles.Items.Clear();
        _lblTyping.Text = string.Empty;
        _txtChatLog.Clear();
        _fileLinks.Clear();
        _network.Send("join", new JoinRequest(_currentRoom));
    }
    public void SwitchRoom(string roomName)
    {
        if (_currentRoom == roomName) return;

        _currentRoom = roomName;
        Text = $"RE:CHAT · {_currentRoom}";

        // Xóa nội dung cũ
        _messagesPanel.Controls.Clear();
        _messageReactions.Clear();
        _messageTexts.Clear();
        _lstFiles.Items.Clear();
        _lblTyping.Text = string.Empty;

        // Gửi yêu cầu tham gia phòng mới
        _network.Send("join", new JoinRequest(_currentRoom));
    }
    private void LstUsers_MouseDown(object? sender, MouseEventArgs e)
    {
        var index = _lstUsers.IndexFromPoint(e.Location);
        if (index < 0) return;
        _lstUsers.SelectedIndex = index;
        if (e.Button == MouseButtons.Right) _userMenu.Show(_lstUsers, e.Location);
    }

    private void ShowSelectedUserProfile()
    {
        if (_lstUsers.SelectedItem is UserListItem user) ShowProfile(user.Username);
    }

    private void StartDirectMessageToSelectedUser()
    {
        if (_lstUsers.SelectedItem is UserListItem user) StartDirectMessage(user.Username);
    }

    private void LstUsers_MeasureItem(object? sender, MeasureItemEventArgs e)
    {
        e.ItemHeight = 34;
    }

    private void LstUsers_DrawItem(object? sender, DrawItemEventArgs e)
    {
        if (e.Index < 0) return;

        var item = _lstUsers.Items[e.Index] as UserListItem;
        if (item == null) return;

        e.DrawBackground();

        var avatarSize = 22;
        var avatar = AvatarHelper.CreateRoundAvatar(null, item.Username, avatarSize);
        var avatarRect = new Rectangle(e.Bounds.X + 8, e.Bounds.Y + 6, avatarSize, avatarSize);
        e.Graphics.DrawImage(avatar, avatarRect);

        using var textBrush = (e.State & DrawItemState.Selected) == DrawItemState.Selected
            ? new SolidBrush(Color.White)
            : new SolidBrush(Color.FromArgb(225, 229, 242));
        var textRect = new Rectangle(e.Bounds.X + 42, e.Bounds.Y + 8, e.Bounds.Width - 46, 18);
        e.Graphics.DrawString(item.Username, e.Font ?? new Font(FontFamily.GenericSansSerif, 10F), textBrush, textRect);

        e.DrawFocusRectangle();
    }

    private void ShowProfile(string username) => _network.Send("profile", new ProfileRequest(username));

    private void StartDirectMessage(string username)
    {
        if (string.Equals(username, _username, StringComparison.OrdinalIgnoreCase))
        {
            ShowProfile(_username);
            return;
        }

        var text = PromptInputBox.Show($"Nhắn riêng · {username}", "Nội dung tin nhắn:");
        if (string.IsNullOrWhiteSpace(text)) return;
        if (!ChatLimits.IsValidChatText(text.Trim()))
        {
            MessageBox.Show($"Tin nhắn tối đa {ChatLimits.MaxChatTextLength} ký tự.", "Tin nhắn quá dài", MessageBoxButtons.OK, MessageBoxIcon.Warning);
            return;
        }
        _network.Send("direct-message", new DirectMessageRequest(username, text.Trim()));
    }

    private void StartNewDirectMessage()
    {
        var username = PromptInputBox.Show("Nhắn tin riêng", "Tên người nhận:");
        if (!string.IsNullOrWhiteSpace(username)) StartDirectMessage(username.Trim());
    }

    private void BtnCreateRoom_Click(object? sender, EventArgs e)
    {
        var name = PromptInputBox.Show("Tao phong moi", "Ten phong:");
        if (string.IsNullOrWhiteSpace(name)) return;
        var room = name.Trim();
        if (!ChatLimits.IsValidRoomName(room))
        {
            MessageBox.Show($"Tên phòng phải có từ 1 đến {ChatLimits.MaxRoomNameLength} ký tự và không chứa ký tự điều khiển.", "Tên phòng không hợp lệ", MessageBoxButtons.OK, MessageBoxIcon.Warning);
            return;
        }
        _network.Send("create-room", new CreateRoomRequest(room));
    }

    // Cac ham OnXxx duoi day chay tren thread nen (NetworkClient doc socket tren Task rieng)
    // nen phai Invoke ve UI thread truoc khi dung cham vao Control.
    private void OnMessageReceived(Envelope envelope)
    {
        if (InvokeRequired)
        {
            Invoke(() => OnMessageReceived(envelope));
            return;
        }

        switch (envelope.Type)
        {
             case "chat":
                 var chat = envelope.As<ChatMessage>();
                 if (chat != null)
                 {
                     AppendUserMessage(chat);
                     if (!string.IsNullOrWhiteSpace(chat.FileName) && !string.IsNullOrWhiteSpace(chat.StoredFile))
                         AddAttachment(chat.FileName, chat.StoredFile, chat.FileSize, chat.ThumbnailBase64);
                 }
                 break;

            case "system":
                var sys = envelope.As<SystemNotice>();
                if (sys != null) AppendLine($"[{FormatMessageTime(sys.Time)}] * {sys.Text}", Color.Gray);
                break;

            case "join":
                var jr = envelope.As<JoinResponse>();
                if (jr != null) AppendLine($"* {jr.Text}", Color.Gray);
                break;

            case "history":
                var hist = envelope.As<HistoryResponse>();
                if (hist != null && hist.Room == _currentRoom && hist.Messages.Count > 0)
                {
                    AppendLine("--- Lich su phong ---", Color.DarkGray);
                    foreach (var m in hist.Messages)
                    {
                        AppendUserMessage(m);
                        if (!string.IsNullOrWhiteSpace(m.FileName) && !string.IsNullOrWhiteSpace(m.StoredFile))
                            AddAttachment(m.FileName, m.StoredFile, m.FileSize, m.ThumbnailBase64);
                    }
                    AppendLine("--- Het lich su ---", Color.DarkGray);
                }
                break;

            case "online-users":
                var ou = envelope.As<OnlineUsersResponse>();
                if (ou != null && ou.Room == _currentRoom)
                {
                    _lstUsers.Items.Clear();
                    foreach (var u in ou.Users) _lstUsers.Items.Add(new UserListItem(u));
                }
                break;


            case "typing":
                var tn = envelope.As<TypingNotice>();
                if (tn != null) _lblTyping.Text = tn.IsTyping ? $"{tn.Username} đang nhập tin nhắn..." : "";
                break;

            case "direct-message":
                var direct = envelope.As<DirectMessage>();
                if (direct != null)
                {
                    var sentByMe = string.Equals(direct.From, _username, StringComparison.OrdinalIgnoreCase);
                    var title = sentByMe ? $"✉ Tin nhắn riêng đến {direct.To}" : $"✉ Tin nhắn riêng từ {direct.From}";
                    AppendLine($"{title}  ·  {FormatMessageTime(direct.Time)}\n{direct.Text}", sentByMe ? UiTheme.Mint : Color.FromArgb(196, 181, 253));
                }
                break;

            case "unread-direct-messages":
                var unread = envelope.As<UnreadDirectMessagesResponse>();
                if (unread?.Messages.Count > 0)
                {
                    AppendLine($"— Bạn có {unread.Messages.Count} tin nhắn riêng khi offline —", UiTheme.Muted);
                    foreach (var message in unread.Messages)
                        AppendLine($"✉ Tin nhắn riêng từ {message.From}  ·  {FormatMessageTime(message.Time)}\n{message.Text}", Color.FromArgb(196, 181, 253));
                }
                break;

                case "profile":
                    if (_ignoreProfileResponses?.Invoke() == true)
                    {
                        ClientLog.Info($"ChatForm ignored profile while Settings open; room={_currentRoom}; disposed={IsDisposed}");
                        break;
                    }
                    ClientLog.Info($"ChatForm handling profile; room={_currentRoom}; disposed={IsDisposed}");
                var profile = envelope.As<ProfileResponse>()?.Profile;
                    if (profile != null && string.Equals(profile.Username, _username, StringComparison.OrdinalIgnoreCase))
                        ApplyPreferences(CreatePreferences(profile));
                if (profile == null)
                {
                    MessageBox.Show("Không tìm thấy hồ sơ người dùng.", "Hồ sơ", MessageBoxButtons.OK, MessageBoxIcon.Information);
                }
                else
                {
                    ProfileDialog.Show(_network,profile, string.Equals(profile.Username, _username, StringComparison.OrdinalIgnoreCase), StartDirectMessage);
                }
                break;

            case "file-download-start":
                StartDownload(envelope.As<FileDownloadStart>());
                break;

            case "file-download-chunk":
                ReceiveDownloadChunk(envelope.As<FileDownloadChunk>());
                break;

            case "file-download-complete":
                CompleteDownload(envelope.As<FileDownloadComplete>());
                break;

            case "error":
                var er = envelope.As<ErrorResponse>();
                MessageBox.Show(er?.Message ?? "Loi khong xac dinh.", "Loi", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                break;
            case "reaction-update":
                var ru = envelope.As<ReactionUpdate>();
                if (ru != null) UpdateMessageReaction(ru.MessageId, ru.Reaction, ru.Users);
                break;

            case "message-updated":
                var messageUpdated = envelope.As<MessageUpdated>();
                if (messageUpdated != null) UpdateMessageText(messageUpdated.MessageId, messageUpdated.Text);
                break;

            case "message-deleted":
                var deleted = envelope.As<MessageDeleted>();
                if (deleted != null) RemoveMessage(deleted.MessageId);
                break;
        

        }
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing)
        {
            ClientLog.Info($"ChatForm disposing room={_currentRoom}; parent={Parent?.GetType().Name ?? "null"}; visible={Visible}");
            _typingStopTimer.Stop();
            _sidePanelTimer.Stop();
            _network.MessageReceived -= OnMessageReceived;
            _network.Disconnected -= OnDisconnected;
        }
        base.Dispose(disposing);
    }

        private void UpdateMessageReaction(string messageId, string reaction, List<string> users)
    {
        if (!_messageReactions.ContainsKey(messageId)) _messageReactions[messageId] = new List<string>();
        var list = _messageReactions[messageId];
        
        if (users.Count > 0) // Add reaction
        {
            if (!list.Contains(reaction)) list.Add(reaction);
        }
        else // Remove reaction
        {
            list.Remove(reaction);
        }
        
        // Refresh UI cho message đó - tìm control trong _messagesPanel và cập nhật
        RefreshMessageReactions(messageId);
    }

    private void RefreshMessageReactions(string messageId)
    {
        foreach (Control control in _messagesPanel.Controls)
        {
            if (control is TableLayoutPanel row && row.Tag is MessageView view && view.MessageId == messageId)
            {
                RenderReactionBadges(view.ReactionBadges, messageId);
                break;
            }
        }
    }

    private void RenderReactionBadges(FlowLayoutPanel badges, string messageId)
    {
        badges.SuspendLayout();
        badges.Controls.Clear();
        foreach (var reaction in _messageReactions.GetValueOrDefault(messageId) ?? [])
        {
            var badge = new Button
            {
                AutoSize = true,
                FlatStyle = FlatStyle.Flat,
                FlatAppearance = { BorderSize = 0 },
                BackColor = Color.FromArgb(54, 63, 81),
                ForeColor = UiTheme.Text,
                Font = new Font("Times New Roman Emoji", 9F),
                Margin = new Padding(0, 2, 4, 0),
                Padding = new Padding(5, 1, 5, 1),
                Text = reaction,
                UseVisualStyleBackColor = false
            };
            badge.Click += (_, _) => ToggleReaction(messageId, reaction);
            badges.Controls.Add(badge);
        }
        badges.Visible = badges.Controls.Count > 0;
        badges.ResumeLayout();
    }

    private void ToggleReaction(string messageId, string reaction)
    {
        if (_messageReactions.GetValueOrDefault(messageId)?.Contains(reaction) == true)
            _network.Send("remove-reaction", new RemoveReactionRequest(messageId, reaction));
        else
            _network.Send("add-reaction", new AddReactionRequest(messageId, reaction));
    }

    private void UpdateMessageText(string messageId, string text)
    {
        foreach (Control control in _messagesPanel.Controls)
        {
            if (control is TableLayoutPanel row && row.Tag is MessageView view && view.MessageId == messageId)
            {
                view.Bubble.Text = $"{view.DisplayName}  ·  {FormatMessageTime(view.Time)} · đã sửa\n{text}";
                _messageTexts[messageId] = text;
                break;
            }
        }
    }

    private void RemoveMessage(string messageId)
    {
        foreach (Control control in _messagesPanel.Controls.Cast<Control>().ToList())
        {
            if (control is TableLayoutPanel row && row.Tag is MessageView view && view.MessageId == messageId)
            {
                _messagesPanel.Controls.Remove(row);
                row.Dispose();
                _messageReactions.Remove(messageId);
                _messageTexts.Remove(messageId);
                break;
            }
        }
    }

    private void AppendUserMessage(ChatMessage chat)
    {
        var displayName = string.IsNullOrWhiteSpace(chat.DisplayName) ? chat.From : chat.DisplayName;
        var row = new TableLayoutPanel
        {
            AutoSize = true,
            AutoSizeMode = AutoSizeMode.GrowAndShrink,
            Dock = DockStyle.Top,
            Margin = new Padding(0, 0, 0, 8),
            BackColor = Color.Transparent,
            Tag = null
        };
        row.ColumnCount = 2;
        row.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 34F));
        row.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
        row.RowCount = 1;
        row.RowStyles.Add(new RowStyle(SizeType.AutoSize));

        var avatar = new PictureBox
        {
            Size = new Size(28, 28),
            SizeMode = PictureBoxSizeMode.Zoom,
            Margin = new Padding(0, 2, 6, 0),
            Image = AvatarHelper.CreateRoundAvatar(chat.AvatarBase64, displayName, 28),
            BorderStyle = BorderStyle.None,
            Visible = _preferences.ShowAvatarsInChat
        };

        var bubble = new Label
        {
            AutoSize = true,
            MaximumSize = new Size(620, 0),
            Padding = new Padding(10, 6, 10, 6),
            ForeColor = chat.From == _username ? UiTheme.Text : Color.FromArgb(225, 229, 242),
            BackColor = chat.From == _username ? Color.FromArgb(44, 92, 86) : Color.FromArgb(41, 48, 61),
            Font = new Font("Times New Roman", _preferences.ChatFontSize),
            Text = $"{displayName}  ·  {FormatMessageTime(chat.Time)}\n{chat.Text}",
            Margin = new Padding(0, 2, 0, 0)
        };
        _messageReactions[chat.Id] = chat.Reactions?.Distinct().ToList() ?? new List<string>();
        _messageTexts[chat.Id] = chat.Text;

        var reactionMenu = new ContextMenuStrip();
        foreach (var emoji in new[] { "👍", "❤️", "😂", "😮", "😢", "👎" })
        {
            var item = reactionMenu.Items.Add(emoji);
            item.Click += (_, _) => ToggleReaction(chat.Id, emoji);
        }

        var actions = new FlowLayoutPanel
        {
            AutoSize = true,
            AutoSizeMode = AutoSizeMode.GrowAndShrink,
            FlowDirection = FlowDirection.LeftToRight,
            Margin = new Padding(0, 2, 0, 0),
            Visible = false
        };
        var reactButton = new Button
        {
            Text = "☺  Cảm xúc",
            AutoSize = true,
            FlatStyle = FlatStyle.Flat,
            FlatAppearance = { BorderSize = 0 },
            BackColor = Color.FromArgb(56, 65, 84),
            ForeColor = Color.FromArgb(190, 198, 220),
            Font = new Font("Times New Roman", 8.5F),
            Margin = new Padding(0),
            Padding = new Padding(6, 2, 6, 2),
            UseVisualStyleBackColor = false
        };
        reactButton.Click += (_, _) => reactionMenu.Show(reactButton, new Point(0, reactButton.Height));
        actions.Controls.Add(reactButton);

        if (string.Equals(chat.From, _username, StringComparison.OrdinalIgnoreCase))
        {
            var editButton = CreateMessageActionButton("✎  Sửa");
            editButton.Click += (_, _) =>
            {
                var currentText = _messageTexts.TryGetValue(chat.Id, out var savedText) ? savedText : chat.Text;
                var text = PromptInputBox.Show("Sửa tin nhắn", "Nội dung mới", currentText);
                if (text is null) return;
                _network.Send("edit-message", new EditMessageRequest(chat.Id, text));
            };
            var deleteButton = CreateMessageActionButton("🗑  Xóa");
            deleteButton.Click += (_, _) =>
            {
                if (MessageBox.Show(this, "Xóa tin nhắn này?", "Xóa tin nhắn", MessageBoxButtons.YesNo, MessageBoxIcon.Warning) == DialogResult.Yes)
                    _network.Send("delete-message", new DeleteMessageRequest(chat.Id));
            };
            actions.Controls.Add(editButton);
            actions.Controls.Add(deleteButton);
        }

        var reactionBadges = new FlowLayoutPanel
        {
            AutoSize = true,
            AutoSizeMode = AutoSizeMode.GrowAndShrink,
            FlowDirection = FlowDirection.LeftToRight,
            Margin = new Padding(0),
            Visible = false
        };
        var messageContent = new FlowLayoutPanel
        {
            AutoSize = true,
            AutoSizeMode = AutoSizeMode.GrowAndShrink,
            FlowDirection = FlowDirection.TopDown,
            WrapContents = false,
            Margin = new Padding(0)
        };
        messageContent.Controls.Add(bubble);
        messageContent.Controls.Add(actions);
        messageContent.Controls.Add(reactionBadges);
        EventHandler showActions = (_, _) => actions.Visible = true;
        EventHandler hideActionsWhenPointerLeaves = (_, _) => BeginInvoke(() =>
        {
            if (!messageContent.ClientRectangle.Contains(messageContent.PointToClient(Cursor.Position)))
                actions.Visible = false;
        });
        foreach (var control in new Control[] { messageContent, bubble, actions, reactButton, reactionBadges })
        {
            control.MouseEnter += showActions;
            control.MouseLeave += hideActionsWhenPointerLeaves;
        }

        row.Tag = new MessageView(chat.Id, reactionBadges, bubble, displayName, chat.Time);
        row.Controls.Add(avatar, 0, 0);
        row.Controls.Add(messageContent, 1, 0);
        if (!_preferences.ShowAvatarsInChat)
            row.ColumnStyles[0].Width = 0;
        _messagesPanel.Controls.Add(row);
        RenderReactionBadges(reactionBadges, chat.Id);
        _messagesPanel.ScrollControlIntoView(row);
    }

    private static Button CreateMessageActionButton(string text) => new()
    {
        Text = text,
        AutoSize = true,
        FlatStyle = FlatStyle.Flat,
        FlatAppearance = { BorderSize = 0 },
        BackColor = Color.FromArgb(56, 65, 84),
        ForeColor = Color.FromArgb(190, 198, 220),
        Font = new Font("Times New Roman", 8.5F),
        Margin = new Padding(2, 0, 0, 0),
        Padding = new Padding(6, 2, 6, 2),
        UseVisualStyleBackColor = false
    };

    private void AddAttachment(string fileName, string storedFile, long fileSize, string? thumbnailBase64 = null)
    {
        var attachment = new FileAttachment(fileName, storedFile, fileSize);
        if (!_lstFiles.Items.OfType<FileAttachment>().Any(file => file.StoredFile == storedFile))
            _lstFiles.Items.Add(attachment);

        if (_preferences.ShowImagePreviews)
            AppendAttachmentPreview(attachment, thumbnailBase64);
        var line = $"📎 {fileName} · {FormatSize(fileSize)}    [Tải xuống]";
        var start = _txtChatLog.TextLength;
        _txtChatLog.SelectionStart = start;
        _txtChatLog.SelectionLength = 0;
        _txtChatLog.SelectionColor = UiTheme.Primary;
        _txtChatLog.SelectionFont = new Font(_txtChatLog.Font, FontStyle.Underline);
        _txtChatLog.AppendText(line + Environment.NewLine);
        _txtChatLog.SelectionColor = _txtChatLog.ForeColor;
        _txtChatLog.SelectionFont = _txtChatLog.Font;
        _fileLinks.Add(new FileLinkRange(start, start + line.Length, new FileAttachment(fileName, storedFile, fileSize)));
        _txtChatLog.ScrollToCaret();
    }

    private void AppendAttachmentPreview(FileAttachment attachment, string? thumbnailBase64)
    {
        var row = new Panel
        {
            AutoSize = true,
            Dock = DockStyle.Top,
            Margin = new Padding(42, 0, 0, 8),
            BackColor = Color.Transparent,
            Cursor = Cursors.Hand
        };

        Control preview;
        var image = CreateThumbnailImage(thumbnailBase64);
        if (image != null)
        {
            var picture = new PictureBox
            {
                Image = image,
                Size = new Size(Math.Min(image.Width, 360), Math.Min(image.Height, 240)),
                SizeMode = PictureBoxSizeMode.Zoom,
                BackColor = Color.FromArgb(30, 36, 54),
                BorderStyle = BorderStyle.FixedSingle,
                Cursor = Cursors.Hand
            };
            picture.Click += (_, _) => DownloadAttachment(attachment);
            preview = picture;
        }
        else
        {
            preview = new Label
            {
                AutoSize = true,
                Padding = new Padding(10, 8, 10, 8),
                BackColor = Color.FromArgb(30, 36, 54),
                ForeColor = UiTheme.Primary,
                Text = $"📎 {attachment.FileName}"
            };
            preview.Click += (_, _) => DownloadAttachment(attachment);
        }

        var caption = new Label
        {
            AutoSize = true,
            Dock = DockStyle.Bottom,
            ForeColor = UiTheme.Muted,
            Font = new Font("Times New Roman", 8.5F),
            Text = $"{attachment.FileName} · {FormatSize(attachment.FileSize)} · nhấn để tải xuống",
            Cursor = Cursors.Hand
        };
        caption.Click += (_, _) => DownloadAttachment(attachment);
        row.Controls.Add(caption);
        row.Controls.Add(preview);
        _messagesPanel.Controls.Add(row);
        _messagesPanel.ScrollControlIntoView(row);
    }

    private static Bitmap? CreateThumbnailImage(string? thumbnailBase64)
    {
        if (string.IsNullOrWhiteSpace(thumbnailBase64)) return null;
        try
        {
            using var stream = new MemoryStream(Convert.FromBase64String(thumbnailBase64));
            using var source = Image.FromStream(stream);
            return new Bitmap(source);
        }
        catch (Exception)
        {
            return null;
        }
    }

    private void DownloadSelectedFile()
    {
        if (_lstFiles.SelectedItem is not FileAttachment attachment)
        {
            MessageBox.Show("Hãy chọn một tệp trong danh sách.", "Tải tệp", MessageBoxButtons.OK, MessageBoxIcon.Information);
            return;
        }
        DownloadAttachment(attachment);
    }

    private void TxtChatLog_MouseClick(object? sender, MouseEventArgs e)
    {
        var charIndex = _txtChatLog.GetCharIndexFromPosition(e.Location);
        var link = _fileLinks.LastOrDefault(range => charIndex >= range.Start && charIndex <= range.End);
        if (link != null) DownloadAttachment(link.Attachment);
    }

    private void DownloadAttachment(FileAttachment attachment) => _network.Send("file-download", new FileDownloadRequest(attachment.StoredFile));

    private void AppendThumbnail(string? thumbnailBase64)
    {
        if (string.IsNullOrWhiteSpace(thumbnailBase64)) return;
        try
        {
            var bytes = Convert.FromBase64String(thumbnailBase64);
            using var imageStream = new MemoryStream(bytes);
            using var image = Image.FromStream(imageStream);
            var hex = Convert.ToHexString(bytes);
            var rtf = $"{{\\rtf1\\ansi{{\\pict\\jpegblip\\picw{image.Width}\\pich{image.Height}\\picwgoal{image.Width * 15}\\pichgoal{image.Height * 15} {hex}}}}}";
            _txtChatLog.SelectionStart = _txtChatLog.TextLength;
            _txtChatLog.SelectionLength = 0;
            _txtChatLog.SelectedRtf = rtf;
            _txtChatLog.AppendText(Environment.NewLine);
        }
        catch { /* Thumbnail is optional; a broken preview must not break chat. */ }
    }

    private void StartDownload(FileDownloadStart? start)
    {
        if (start == null || start.FileSize <= 0 || start.FileSize > ChatLimits.MaxFileBytes) return;
        using var dialog = new SaveFileDialog { FileName = Path.GetFileName(start.FileName), Title = "Lưu tệp" };
        var canceled = dialog.ShowDialog(this) != DialogResult.OK;
        _downloads[start.TransferId] = new PendingDownload(canceled ? null : dialog.FileName, start.FileSize);
        if (!canceled) SetTransferProgress($"Đang tải {Path.GetFileName(start.FileName)}", 0);
    }

    private void ReceiveDownloadChunk(FileDownloadChunk? chunk)
    {
        if (chunk == null || !_downloads.TryGetValue(chunk.TransferId, out var download)) return;
        try
        {
            if (chunk.Index != download.NextChunkIndex) throw new InvalidDataException();
            var bytes = Convert.FromBase64String(chunk.Base64Data);
            if (bytes.Length == 0 || bytes.Length > ChatLimits.FileChunkBytes || download.Content.Length + bytes.Length > download.ExpectedSize)
                throw new InvalidDataException();
            download.Content.Write(bytes);
            download.NextChunkIndex++;
            if (download.SavePath != null)
                SetTransferProgress($"Đang tải {Path.GetFileName(download.SavePath)}", (int)(download.Content.Length * 100 / download.ExpectedSize));
        }
        catch
        {
            download.Failed = true;
        }
    }

    private async void CompleteDownload(FileDownloadComplete? complete)
    {
        if (complete == null || !_downloads.Remove(complete.TransferId, out var download)) return;
        using (download)
        {
            if (download.SavePath == null) { HideTransferProgress(); return; }
            if (download.Failed || download.Content.Length != download.ExpectedSize)
            {
                MessageBox.Show("Dữ liệu tải về không đầy đủ.", "Lỗi tải tệp", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                HideTransferProgress();
                return;
            }
            await File.WriteAllBytesAsync(download.SavePath, download.Content.ToArray());
            AppendLine($"Đã tải tệp: {Path.GetFileName(download.SavePath)}", UiTheme.Mint);
            HideTransferProgress();
        }
    }

    private void OnDisconnected(string reason)
    {
        if (InvokeRequired)
        {
            Invoke(() => OnDisconnected(reason));
            return;
        }
        MessageBox.Show(reason, "Mat ket noi", MessageBoxButtons.OK, MessageBoxIcon.Error);
    }

    private sealed record UserListItem(string Username)
    {
        public override string ToString() => Username;
    }

    private sealed record FileAttachment(string FileName, string StoredFile, long FileSize)
    {
        public override string ToString() => $"📎 {FileName} ({FormatSize(FileSize)})";
    }

    private sealed record MessageView(
        string MessageId,
        FlowLayoutPanel ReactionBadges,
        Label Bubble,
        string DisplayName,
        DateTimeOffset Time);

    private sealed record FileLinkRange(int Start, int End, FileAttachment Attachment);

    private sealed class PendingDownload : IDisposable
    {
        public string? SavePath { get; }
        public long ExpectedSize { get; }
        public int NextChunkIndex { get; set; }
        public bool Failed { get; set; }
        public MemoryStream Content { get; } = new();

        public PendingDownload(string? savePath, long expectedSize) { SavePath = savePath; ExpectedSize = expectedSize; }
        public void Dispose() => Content.Dispose();
    }

    private static string FormatSize(long bytes) => bytes < 1024 * 1024
        ? $"{Math.Max(1, bytes / 1024)} KB"
        : $"{bytes / 1024d / 1024d:0.##} MB";

    private static string FormatMessageTime(DateTimeOffset timestamp)
    {
        var localTime = timestamp.LocalDateTime;
        return localTime.Date == DateTime.Today
            ? localTime.ToString("hh:mm tt", CultureInfo.InvariantCulture)
            : localTime.ToString("dd/MM/yyyy hh:mm tt", CultureInfo.InvariantCulture);
    }

    private void SetTransferProgress(string text, int percentage)
    {
        _transferPanel.Visible = true;
        _transferLabel.Text = text;
        _transferProgress.Value = Math.Clamp(percentage, 0, 100);
    }

    private void HideTransferProgress()
    {
        _transferPanel.Visible = false;
        _transferProgress.Value = 0;
    }

    private void AppendLine(string text, Color? color = null)
    {
        var row = new Panel { AutoSize = true, Dock = DockStyle.Top, Margin = new Padding(0, 0, 0, 6), BackColor = Color.Transparent };
        var label = new Label
        {
            AutoSize = true,
            MaximumSize = new Size(700, 0),
            ForeColor = color ?? UiTheme.Text,
            Font = new Font("Times New Roman", 10F),
            Text = text,
            Margin = new Padding(0)
        };
        row.Controls.Add(label);
        _messagesPanel.Controls.Add(row);
        _messagesPanel.ScrollControlIntoView(row);
    }
}
