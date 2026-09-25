using ChatProtocol;

namespace ChatClient;

public class LoginForm : Form
{
    private readonly TextBox _txtHost = new() { Text = "127.0.0.1" };
    private readonly TextBox _txtPort = new() { Text = "5050" };
    private readonly TextBox _txtUsername = new();
    private readonly TextBox _txtPassword = new() { UseSystemPasswordChar = true };
    private readonly ComboBox _cmbRoom = new()
    {
        DropDownStyle = ComboBoxStyle.DropDownList,
        Items = { "General", "Random", "Tech" },
        SelectedIndex = 0
    };
    private readonly Button _btnConnect = new() { Text = "Vào trò chuyện" };
    private readonly Button _btnRegister = new() { Text = "Tạo tài khoản" };
    private readonly Label _lblStatus = new() { AutoSize = true };

    public LoginForm()
    {
        Text = "ChatNet";
        ClientSize = new Size(910, 570);
        MinimumSize = new Size(780, 520);
        BackColor = UiTheme.Canvas;
        ForeColor = UiTheme.Text;
        Font = new Font("Segoe UI", 10F);
        StartPosition = FormStartPosition.CenterScreen;
        BuildLayout();
        _btnConnect.Click += BtnConnect_Click;
        _btnRegister.Click += BtnRegister_Click;
        AcceptButton = _btnConnect;
    }

    private void BuildLayout()
    {
        var shell = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 2, BackColor = UiTheme.Canvas, Padding = new Padding(34) };
        shell.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 53));
        shell.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 47));

        var hero = new Panel { Dock = DockStyle.Fill, Padding = new Padding(20, 70, 38, 30), BackColor = UiTheme.Canvas };
        var badge = new Label { Text = "●  CHATNET  •  KẾT NỐI CÙNG NHAU", AutoSize = true, ForeColor = UiTheme.Mint, Font = new Font("Segoe UI Semibold", 9F) };
        var title = new Label { Text = "Cuộc trò chuyện\nđẹp hơn, gần hơn.", AutoSize = true, Top = 40, ForeColor = UiTheme.Text, Font = new Font("Segoe UI Semibold", 27F), MaximumSize = new Size(430, 0) };
        var description = new Label { Text = "Tham gia phòng chat của bạn để chia sẻ ý tưởng, cập nhật và những điều thú vị mỗi ngày.", AutoSize = true, Top = 190, ForeColor = UiTheme.Muted, Font = new Font("Segoe UI", 11F), MaximumSize = new Size(400, 0) };
        var feature = new Label { Text = "✦  Phòng chat riêng tư\n✦  Trợ lý AI ngay trong hội thoại\n✦  Kết nối theo thời gian thực", AutoSize = true, Top = 310, ForeColor = Color.FromArgb(205, 210, 230), Font = new Font("Segoe UI", 10F), MaximumSize = new Size(380, 0) };
        hero.Controls.AddRange([badge, title, description, feature]);

        var card = new CardPanel { Dock = DockStyle.Fill, Padding = new Padding(34, 27, 34, 25), Margin = new Padding(18, 12, 0, 12) };
        var heading = new Label { Text = "Chào mừng trở lại", AutoSize = true, ForeColor = UiTheme.Text, Font = UiTheme.TitleFont };
        var subheading = UiTheme.Label("Đăng nhập để tiếp tục cuộc trò chuyện.", 10F);
        subheading.Top = 43;
        var form = new TableLayoutPanel { Dock = DockStyle.Bottom, Height = 343, ColumnCount = 2, RowCount = 8, BackColor = UiTheme.Surface };
        form.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 50));
        form.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 50));
        for (var i = 0; i < 8; i++) form.RowStyles.Add(new RowStyle(SizeType.AutoSize));

        UiTheme.StyleInput(_txtHost); UiTheme.StyleInput(_txtPort); UiTheme.StyleInput(_txtUsername); UiTheme.StyleInput(_txtPassword);
        _cmbRoom.BackColor = UiTheme.SurfaceRaised; _cmbRoom.ForeColor = UiTheme.Text; _cmbRoom.FlatStyle = FlatStyle.Flat; _cmbRoom.Font = new Font("Segoe UI", 10F); _cmbRoom.Margin = new Padding(0, 4, 0, 12);
        AddLabel(form, "MÁY CHỦ", 0, 0); AddLabel(form, "CỔNG", 1, 0);
        form.Controls.Add(_txtHost, 0, 1); form.Controls.Add(_txtPort, 1, 1);
        AddLabel(form, "TÊN HIỂN THỊ", 0, 2, 2); form.Controls.Add(_txtUsername, 0, 3); form.SetColumnSpan(_txtUsername, 2);
        AddLabel(form, "MẬT KHẨU", 0, 4, 2); form.Controls.Add(_txtPassword, 0, 5); form.SetColumnSpan(_txtPassword, 2);
        AddLabel(form, "PHÒNG CHAT", 0, 6, 2); form.Controls.Add(_cmbRoom, 0, 7); form.SetColumnSpan(_cmbRoom, 2);

        UiTheme.StyleButton(_btnConnect, UiTheme.Primary, Color.White); UiTheme.StyleButton(_btnRegister, UiTheme.SurfaceRaised, UiTheme.Text);
        _btnConnect.Dock = DockStyle.Bottom; _btnRegister.Dock = DockStyle.Bottom;
        _btnConnect.Margin = new Padding(0, 8, 7, 0); _btnRegister.Margin = new Padding(7, 8, 0, 0);
        _lblStatus.ForeColor = Color.FromArgb(251, 146, 160); _lblStatus.Dock = DockStyle.Bottom; _lblStatus.Padding = new Padding(0, 6, 0, 0);
        var buttons = new TableLayoutPanel { Dock = DockStyle.Bottom, Height = 48, ColumnCount = 2, BackColor = UiTheme.Surface };
        buttons.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 56)); buttons.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 44));
        buttons.Controls.Add(_btnConnect, 0, 0); buttons.Controls.Add(_btnRegister, 1, 0);
        card.Controls.AddRange([heading, subheading, form, _lblStatus, buttons]);
        shell.Controls.Add(hero, 0, 0); shell.Controls.Add(card, 1, 0); Controls.Add(shell);
    }

    private static void AddLabel(TableLayoutPanel form, string text, int column, int row, int span = 1)
    {
        var label = UiTheme.Label(text, 8.5F, UiTheme.Muted, FontStyle.Bold);
        form.Controls.Add(label, column, row);
        if (span > 1) form.SetColumnSpan(label, span);
    }

    private async void BtnConnect_Click(object? sender, EventArgs e) => await ConnectAndAuthenticateAsync(false);
    private async void BtnRegister_Click(object? sender, EventArgs e) => await ConnectAndAuthenticateAsync(true);

    private async Task ConnectAndAuthenticateAsync(bool register)
    {
        var username = _txtUsername.Text.Trim(); var password = _txtPassword.Text; var room = _cmbRoom.Text.Trim();
        if (username.Length == 0 || password.Length == 0 || room.Length == 0) { _lblStatus.Text = "Hãy nhập tên hiển thị, mật khẩu và phòng chat."; return; }
        if (!int.TryParse(_txtPort.Text.Trim(), out var port)) { _lblStatus.Text = "Cổng kết nối không hợp lệ."; return; }
        _btnConnect.Enabled = _btnRegister.Enabled = false; _lblStatus.ForeColor = UiTheme.Mint; _lblStatus.Text = register ? "Đang tạo tài khoản..." : "Đang kết nối...";
        var network = new NetworkClient(); var authenticated = false;
        var authResult = new TaskCompletionSource<Envelope>(TaskCreationOptions.RunContinuationsAsynchronously);
        void OnAuthMessage(Envelope envelope) { if (envelope.Type is "auth" or "error") authResult.TrySetResult(envelope); }
        network.MessageReceived += OnAuthMessage;
        try
        {
            await network.ConnectAsync(_txtHost.Text.Trim(), port); await network.SendAsync(register ? "register" : "login", new AuthRequest(username, password));
            var completed = await Task.WhenAny(authResult.Task, Task.Delay(TimeSpan.FromSeconds(10)));
            if (completed != authResult.Task) throw new TimeoutException("Máy chủ chưa phản hồi yêu cầu xác thực.");
            var result = await authResult.Task;
            if (result.Type == "error") { _lblStatus.ForeColor = Color.FromArgb(251, 146, 160); _lblStatus.Text = result.As<ErrorResponse>()?.Message ?? "Có lỗi không xác định."; return; }
            authenticated = true;
        }
        catch (Exception ex) { _lblStatus.ForeColor = Color.FromArgb(251, 146, 160); _lblStatus.Text = $"Không thể kết nối: {ex.Message}"; return; }
        finally { network.MessageReceived -= OnAuthMessage; if (!authenticated) network.Close(); _btnConnect.Enabled = _btnRegister.Enabled = true; }
        var chatForm = new ChatForm(network, username, room); chatForm.FormClosed += (_, _) => Close(); Hide(); chatForm.Show(); network.Send("join", new JoinRequest(room));
    }
}
