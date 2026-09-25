using ChatProtocol;

namespace ChatClient;

public class ChatForm : Form
{
    private static readonly Color BackgroundColor = UiTheme.Canvas;
    private static readonly Color PanelColor = UiTheme.Surface;
    private static readonly Color InputColor = UiTheme.SurfaceRaised;
    private static readonly Color AccentColor = UiTheme.Primary;

    private readonly NetworkClient _network;
    private readonly string _username;
    private string _currentRoom;

    private readonly ListBox _lstRooms = new();
    private readonly ListBox _lstUsers = new();
    private readonly RichTextBox _txtChatLog = new() { ReadOnly = true, Dock = DockStyle.Fill };
    private readonly TextBox _txtInput = new() { Dock = DockStyle.Fill };
    private readonly Button _btnSend = new() { Text = "Gui", Dock = DockStyle.Right, Width = 80 };
    private readonly Button _btnAi = new() { Text = "AI", Dock = DockStyle.Right, Width = 60 };
    private readonly Button _btnAttach = new() { Text = "📎", Dock = DockStyle.Right, Width = 48 };
    private readonly Button _btnCreateRoom = new() { Text = "+ Tao phong", Dock = DockStyle.Bottom };
    private readonly Button _btnNewDirect = new() { Text = "✉ Nhắn riêng", Dock = DockStyle.Bottom };
    private readonly Button _btnProfile = new() { Dock = DockStyle.Right, Width = 120 };
    private readonly ContextMenuStrip _userMenu = new();
    private readonly ListBox _lstFiles = new();
    private readonly Button _btnDownloadFile = new() { Text = "↓ Tải tệp đã chọn", Dock = DockStyle.Bottom, Height = 34 };
    private readonly Dictionary<string, PendingDownload> _downloads = new();
    private readonly List<FileLinkRange> _fileLinks = new();
    private readonly ProgressBar _transferProgress = new() { Dock = DockStyle.Fill, Style = ProgressBarStyle.Continuous, Maximum = 100 };
    private readonly Label _transferLabel = new() { Dock = DockStyle.Left, Width = 175, TextAlign = ContentAlignment.MiddleLeft, ForeColor = UiTheme.Muted, Font = new Font("Segoe UI", 8.5F) };
    private readonly Panel _transferPanel = new() { Dock = DockStyle.Bottom, Height = 26, Visible = false, Padding = new Padding(0, 2, 0, 2) };
    private readonly Label _lblTyping = new() { Dock = DockStyle.Bottom, Height = 20, ForeColor = Color.Gray };
    private readonly System.Windows.Forms.Timer _typingStopTimer = new() { Interval = 1500 };

    public ChatForm(NetworkClient network, string username, string room)
    {
        _network = network;
        _username = username;
        _currentRoom = room;

        Text = $"ChatNet · {room}";
        Width = 1080;
        Height = 700;
        MinimumSize = new Size(860, 560);
        BackColor = BackgroundColor;
        ForeColor = Color.White;
        Font = new Font("Segoe UI", 10F);
        StartPosition = FormStartPosition.CenterScreen;

        BuildLayout();

        _btnProfile.Text = $"◉ {_username}";
        _btnProfile.Click += (_, _) => ShowProfile(_username);
        _btnAttach.Click += BtnAttach_Click;
        _btnDownloadFile.Click += (_, _) => DownloadSelectedFile();
        _btnNewDirect.Click += (_, _) => StartNewDirectMessage();
        _userMenu.Items.Add("Xem hồ sơ", null, (_, _) => ShowSelectedUserProfile());
        _userMenu.Items.Add("Nhắn tin riêng", null, (_, _) => StartDirectMessageToSelectedUser());
        _lstUsers.MouseDown += LstUsers_MouseDown;
        _lstUsers.DoubleClick += (_, _) => ShowSelectedUserProfile();
        _txtChatLog.MouseClick += TxtChatLog_MouseClick;
        _transferPanel.Controls.Add(_transferProgress);
        _transferPanel.Controls.Add(_transferLabel);

        _network.MessageReceived += OnMessageReceived;
        _network.Disconnected += OnDisconnected;
        _network.Send("get-unread-direct-messages");

        _typingStopTimer.Tick += (_, _) =>
        {
            _typingStopTimer.Stop();
            _network.Send("typing", new TypingRequest(false));
        };

        FormClosed += (_, _) => _network.Close();
    }

    private void BuildLayout()
    {
        var leftPanel = new Panel { Dock = DockStyle.Left, Width = 220, BackColor = PanelColor, Padding = new Padding(14, 10, 14, 14) };
        leftPanel.Controls.Add(_lstRooms);
        leftPanel.Controls.Add(new Label { Text = "CHATNET", Dock = DockStyle.Top, Height = 42, ForeColor = UiTheme.Text, Font = new Font("Segoe UI Semibold", 16F), TextAlign = ContentAlignment.MiddleLeft });
        leftPanel.Controls.Add(new Label { Text = "PHÒNG CỦA BẠN", Dock = DockStyle.Top, Height = 26, ForeColor = UiTheme.Muted, Font = new Font("Segoe UI Semibold", 8.5F), TextAlign = ContentAlignment.MiddleLeft });
        _lstRooms.Dock = DockStyle.Fill;
        StyleList(_lstRooms);
        _lstRooms.DoubleClick += LstRooms_DoubleClick;

        var rightPanel = new Panel { Dock = DockStyle.Right, Width = 200, BackColor = PanelColor, Padding = new Padding(14, 10, 14, 14) };
        rightPanel.Controls.Add(_lstUsers);
        rightPanel.Controls.Add(new Label { Text = "●  ĐANG TRỰC TUYẾN", Dock = DockStyle.Top, Height = 42, ForeColor = UiTheme.Mint, Font = new Font("Segoe UI Semibold", 9F), TextAlign = ContentAlignment.MiddleLeft });
        _lstUsers.Dock = DockStyle.Fill;
        StyleList(_lstUsers);

        var filesPanel = new Panel { Dock = DockStyle.Bottom, Height = 170, BackColor = PanelColor, Padding = new Padding(0, 8, 0, 0) };
        filesPanel.Controls.Add(_lstFiles);
        filesPanel.Controls.Add(_btnDownloadFile);
        filesPanel.Controls.Add(new Label { Text = "TỆP TRONG PHÒNG", Dock = DockStyle.Top, Height = 26, ForeColor = UiTheme.Muted, Font = new Font("Segoe UI Semibold", 8.5F), TextAlign = ContentAlignment.MiddleLeft });
        _lstFiles.Dock = DockStyle.Fill;
        StyleList(_lstFiles);
        _lstFiles.ItemHeight = 27;
        UiTheme.StyleButton(_btnDownloadFile, UiTheme.SurfaceRaised, UiTheme.Text);
        _btnDownloadFile.Height = 34;
        rightPanel.Controls.Add(filesPanel);

        var inputPanel = new CardPanel { Dock = DockStyle.Bottom, Height = 58, BackColor = PanelColor, Padding = new Padding(12, 9, 9, 9), CornerRadius = 12 };
        inputPanel.Controls.Add(_txtInput);
        inputPanel.Controls.Add(_btnSend);
        inputPanel.Controls.Add(_btnAi);
        inputPanel.Controls.Add(_btnAttach);
        _txtInput.BackColor = InputColor;
        _txtInput.ForeColor = Color.White;
        _txtInput.BorderStyle = BorderStyle.None;
        _txtInput.Font = new Font("Segoe UI", 10F);
        _btnSend.BackColor = AccentColor;
        _btnSend.ForeColor = Color.White;
        UiTheme.StyleButton(_btnSend, AccentColor, Color.White);
        _btnAi.BackColor = Color.FromArgb(73, 67, 130);
        UiTheme.StyleButton(_btnAi, Color.FromArgb(73, 67, 130), Color.White);
        UiTheme.StyleButton(_btnAttach, UiTheme.SurfaceRaised, UiTheme.Text);
        _txtInput.PlaceholderText = "Nhập tin nhắn… dùng @AI hoặc /AI để hỏi trợ lý";
        _txtInput.TextChanged += TxtInput_TextChanged;
        _txtInput.KeyDown += (_, e) =>
        {
            if (e.KeyCode == Keys.Enter) { e.SuppressKeyPress = true; SendMessage(); }
        };
        _btnSend.Click += (_, _) => SendMessage();
        _btnAi.Click += BtnAi_Click;

        _btnCreateRoom.Click += BtnCreateRoom_Click;

        var centerPanel = new Panel { Dock = DockStyle.Fill, BackColor = BackgroundColor, Padding = new Padding(18, 0, 18, 16) };
        centerPanel.Controls.Add(_txtChatLog);
        centerPanel.Controls.Add(inputPanel);
        centerPanel.Controls.Add(_transferPanel);
        centerPanel.Controls.Add(_lblTyping);
        var header = new Panel { Dock = DockStyle.Top, Height = 72, BackColor = BackgroundColor, Padding = new Padding(0, 12, 0, 8) };
        header.Controls.Add(new Label { Text = "●  Kết nối an toàn", Dock = DockStyle.Right, Width = 145, ForeColor = UiTheme.Mint, Font = new Font("Segoe UI Semibold", 8.5F), TextAlign = ContentAlignment.MiddleRight });
        UiTheme.StyleButton(_btnProfile, UiTheme.SurfaceRaised, UiTheme.Text);
        _btnProfile.Height = 34;
        header.Controls.Add(_btnProfile);
        header.Controls.Add(new Label { Name = "RoomTitle", Text = $"# {_currentRoom}", Dock = DockStyle.Top, Height = 32, ForeColor = UiTheme.Text, Font = new Font("Segoe UI Semibold", 17F) });
        header.Controls.Add(new Label { Text = "Nơi mọi người cùng trao đổi và chia sẻ.", Dock = DockStyle.Bottom, Height = 20, ForeColor = UiTheme.Muted, Font = new Font("Segoe UI", 9F) });
        centerPanel.Controls.Add(header);

        _txtChatLog.BackColor = BackgroundColor;
        _txtChatLog.ForeColor = Color.FromArgb(225, 229, 242);
        _txtChatLog.BorderStyle = BorderStyle.None;
        _txtChatLog.Font = new Font("Segoe UI", 10F);
        _lblTyping.ForeColor = Color.Silver;
        UiTheme.StyleButton(_btnCreateRoom, UiTheme.SurfaceRaised, UiTheme.Text);
        _btnCreateRoom.Height = 40;
        UiTheme.StyleButton(_btnNewDirect, Color.FromArgb(73, 67, 130), Color.White);
        _btnNewDirect.Height = 40;

        leftPanel.Controls.Add(_btnCreateRoom);
        leftPanel.Controls.Add(_btnNewDirect);

        Controls.Add(centerPanel);
        Controls.Add(rightPanel);
        Controls.Add(leftPanel);
    }

    private static void StyleList(ListBox list)
    {
        list.BackColor = PanelColor;
        list.ForeColor = Color.FromArgb(220, 224, 240);
        list.BorderStyle = BorderStyle.None;
        list.ItemHeight = 34;
        list.Font = new Font("Segoe UI", 9.5F);
    }

    private void SendMessage()
    {
        var text = _txtInput.Text.Trim();
        if (text.Length == 0) return;
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

    private void BtnAi_Click(object? sender, EventArgs e)
    {
        var prompt = PromptInputBox.Show("Hỏi trợ lý AI", "Nhập câu hỏi cho AI:");
        if (string.IsNullOrWhiteSpace(prompt)) return;

        _network.Send("chat", new ChatRequest($"/AI {prompt.Trim()}"));
        AppendLine("Đang chờ AI phản hồi…", UiTheme.Muted);
    }

    private void TxtInput_TextChanged(object? sender, EventArgs e)
    {
        _network.Send("typing", new TypingRequest(true));
        _typingStopTimer.Stop();
        _typingStopTimer.Start();
    }

    private void LstRooms_DoubleClick(object? sender, EventArgs e)
    {
        if (_lstRooms.SelectedItem is not RoomInfo room) return;
        if (room.Name == _currentRoom) return;

        _currentRoom = room.Name;
        Text = $"ChatNet · {_currentRoom}";
        if (Controls.Find("RoomTitle", true).FirstOrDefault() is Label roomTitle) roomTitle.Text = $"# {_currentRoom}";
        _txtChatLog.Clear();
        _fileLinks.Clear();
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
        if (_lstUsers.SelectedItem is string username) ShowProfile(username);
    }

    private void StartDirectMessageToSelectedUser()
    {
        if (_lstUsers.SelectedItem is string username) StartDirectMessage(username);
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
        _network.Send("create-room", new CreateRoomRequest(name.Trim()));
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
                    AppendLine($"{chat.From}  ·  {chat.Time:HH:mm}\n{chat.Text}", chat.From == _username ? UiTheme.Mint : null);
                    if (!string.IsNullOrWhiteSpace(chat.FileName) && !string.IsNullOrWhiteSpace(chat.StoredFile))
                        AddAttachment(chat.FileName, chat.StoredFile, chat.FileSize, chat.ThumbnailBase64);
                }
                break;

            case "system":
                var sys = envelope.As<SystemNotice>();
                if (sys != null) AppendLine($"[{sys.Time:HH:mm:ss}] * {sys.Text}", Color.Gray);
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
                        AppendLine($"[{m.Time:HH:mm:ss}] {m.From}: {m.Text}");
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
                    foreach (var u in ou.Users) _lstUsers.Items.Add(u);
                }
                break;

            case "room-list":
                var rl = envelope.As<RoomListResponse>();
                if (rl != null)
                {
                    _lstRooms.Items.Clear();
                    foreach (var r in rl.Rooms) _lstRooms.Items.Add(r);
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
                    AppendLine($"{title}  ·  {direct.Time:HH:mm}\n{direct.Text}", sentByMe ? UiTheme.Mint : Color.FromArgb(196, 181, 253));
                }
                break;

            case "unread-direct-messages":
                var unread = envelope.As<UnreadDirectMessagesResponse>();
                if (unread?.Messages.Count > 0)
                {
                    AppendLine($"— Bạn có {unread.Messages.Count} tin nhắn riêng khi offline —", UiTheme.Muted);
                    foreach (var message in unread.Messages)
                        AppendLine($"✉ Tin nhắn riêng từ {message.From}  ·  {message.Time:HH:mm}\n{message.Text}", Color.FromArgb(196, 181, 253));
                }
                break;

            case "profile":
                var profile = envelope.As<ProfileResponse>()?.Profile;
                if (profile == null)
                {
                    MessageBox.Show("Không tìm thấy hồ sơ người dùng.", "Hồ sơ", MessageBoxButtons.OK, MessageBoxIcon.Information);
                }
                else
                {
                    ProfileDialog.Show(profile, string.Equals(profile.Username, _username, StringComparison.OrdinalIgnoreCase), StartDirectMessage);
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
        }
    }

    private void AppendLine(string text, Color? color = null)
    {
        _txtChatLog.SelectionStart = _txtChatLog.TextLength;
        _txtChatLog.SelectionLength = 0;
        _txtChatLog.SelectionColor = color ?? _txtChatLog.ForeColor;
        _txtChatLog.AppendText(text + Environment.NewLine);
        _txtChatLog.ScrollToCaret();
    }

    private void AddAttachment(string fileName, string storedFile, long fileSize, string? thumbnailBase64 = null)
    {
        if (!_lstFiles.Items.OfType<FileAttachment>().Any(file => file.StoredFile == storedFile))
            _lstFiles.Items.Add(new FileAttachment(fileName, storedFile, fileSize));

        AppendThumbnail(thumbnailBase64);
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

    private sealed record FileAttachment(string FileName, string StoredFile, long FileSize)
    {
        public override string ToString() => $"📎 {FileName} ({FormatSize(FileSize)})";
    }

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
}
