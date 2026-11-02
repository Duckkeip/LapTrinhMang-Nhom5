using ChatProtocol;

namespace ChatClient;

public class LoginForm : Form
{
    private readonly TextBox _txtHost = new() { Text = "multi-hmz5.onrender.com" };
    private readonly TextBox _txtPort = new() { Text = "443" };
    private readonly TextBox _txtUsername = new();
    private readonly TextBox _txtPassword = new() { UseSystemPasswordChar = true };
    private readonly Button _btnConnect = new() { Text = "Vào trò chuyện" };
    private readonly Button _btnRegister = new() { Text = "Tạo tài khoản" };
    private readonly Button _btnForgotPassword = new() { Text = "Quên mật khẩu?" };
    private readonly Button _btnRevealPassword = new() { Text = "Hiện", Dock = DockStyle.Right, Width = 58 };
    
    // Đã sửa: Cho phép Label tự động rớt dòng và gán kích thước tối đa
    private readonly Label _lblStatus = new() 
    { 
        AutoSize = false, 
        MaximumSize = new Size(360, 45), 
        Height = 40,
        AutoEllipsis = true 
    };

    private readonly Panel _loginView = new() { Dock = DockStyle.Fill };
    private readonly Panel _registerView = new() { Dock = DockStyle.Fill, Visible = false };
    private readonly TextBox _txtRegisterUsername = new();
    private readonly TextBox _txtRegisterPassword = new() { UseSystemPasswordChar = true };
    private readonly TextBox _txtRegisterEmail = new();
    private readonly TextBox _txtRegisterOtp = new() { MaxLength = 6, Enabled = false };
    private readonly Button _btnRegisterSubmit = new() { Text = "Gửi mã OTP" };
    private readonly Button _btnRegisterResend = new() { Text = "Gửi lại mã OTP", Enabled = false };
    private readonly Button _btnBackToLogin = new() { Text = "Quay lại đăng nhập" };
    private readonly Label _lblRegisterStatus = new() { AutoSize = true, MaximumSize = new Size(360, 0) };
    private readonly ToolTip _statusToolTip = new();
    private NetworkClient? _registrationNetwork;

    public LoginForm()
    {
        Text = "RE:CHAT - Chat ở Một Thế Giới Khác";

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
            Console.WriteLine($"Không thể tải icon: {ex.Message}");
        }

        ClientSize = new Size(1100, 570);
        MinimumSize = new Size(780, 520);

        Paint += LoginForm_Paint;
        DoubleBuffered = true;
        Font = new Font("Segoe UI", 10F);
        StartPosition = FormStartPosition.CenterScreen;
        BuildLayout();
        _btnConnect.Click += BtnConnect_Click;
        _btnRegister.Click += BtnRegister_Click;
        _btnForgotPassword.Click += (_, _) => ShowForgotPasswordDialog();
        _btnRegisterSubmit.Click += BtnRegisterSubmit_Click;
        _btnRegisterResend.Click += async (_, _) => await RequestRegistrationOtpAsync();
        _btnBackToLogin.Click += (_, _) => ShowLoginView();
        AcceptButton = _btnConnect;
        _btnRevealPassword.Click += (_, _) =>
        {
            _txtPassword.UseSystemPasswordChar = !_txtPassword.UseSystemPasswordChar;
            _btnRevealPassword.Text = _txtPassword.UseSystemPasswordChar ? "Hiện" : "Ẩn";
            _txtPassword.Focus();
            _txtPassword.SelectionStart = _txtPassword.TextLength;
        };
    }

    private void LoginForm_Paint(object? sender, PaintEventArgs e)
    {
        using var brush = new System.Drawing.Drawing2D.LinearGradientBrush(
            ClientRectangle,
            Color.Black, Color.Black,
            180F
        );

        var blend = new System.Drawing.Drawing2D.ColorBlend
        {
            Colors = new[] 
            { 
                Color.FromArgb(20, 24, 38), 
                Color.FromArgb(30, 35, 47),
                Color.FromArgb(60, 70, 95),  
                Color.FromArgb(30, 35, 47),
                Color.FromArgb(20, 24, 38)
            },
            Positions = new[] { 0.0F, 0.25F, 0.55F, 0.75F, 1.0F }
        };

        brush.InterpolationColors = blend;
        e.Graphics.FillRectangle(brush, ClientRectangle);
    }

    private void BuildLayout()
    {
        var shell = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 2, BackColor = Color.Transparent, Padding = new Padding(34) };
        shell.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 54));
        shell.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 46));

        var hero = new Panel { Dock = DockStyle.Fill, Padding = new Padding(20, 50, 38, 30), BackColor = Color.Transparent };

        var lblRe = new Label { Text = "Re:", AutoSize = true, Left = 20, Top = 15, ForeColor = Color.FromArgb(170, 160, 210), Font = new Font("Times New Roman", 60F, FontStyle.Italic) };
        var lblZero = new Label { Text = "Chat", AutoSize = true, Left = 150, Top = 10, ForeColor = Color.FromArgb(240, 243, 248), Font = new Font("Times New Roman", 60F, FontStyle.Bold | FontStyle.Italic) };
        var lblSub = new Label { Text = "-Chat ở Một Thế Giới Khác-", AutoSize = true, Left = 25, Top = 55, ForeColor = Color.FromArgb(200, 205, 215), Font = new Font("Times New Roman", 10F, FontStyle.Italic) };

        var badge = new Label { Text = "●  CHATNET  ●  KẾT NỐI CÙNG NHAU", AutoSize = true, Top = 105, ForeColor = UiTheme.Mint, Font = new Font("Times New Roman", 9F) };
        var title = new Label { Text = "Cuộc trò chuyện\nđẹp hơn, gần hơn.", AutoSize = true, Top = 135, ForeColor = UiTheme.Text, Font = new Font("Times New Roman", 20F), MaximumSize = new Size(430, 0) };
        var description = new Label { Text = "Tham gia phòng chat của bạn để chia sẻ ý tưởng, cập nhật và những điều thú vị mỗi ngày.", AutoSize = true, Top = 295, ForeColor = UiTheme.Muted, Font = new Font("Segoe UI", 11F), MaximumSize = new Size(400, 0) };
        var feature = new Label { Text = "✦  Phòng chat riêng tư\n✦  Trợ lý AI ngay trong hội thoại\n✦  Kết nối theo thời gian thực", AutoSize = true, Top = 395, ForeColor = Color.FromArgb(205, 210, 230), Font = new Font("Segoe UI", 10F), MaximumSize = new Size(380, 0) };

        hero.Controls.AddRange([lblRe, lblZero, lblSub, badge, title, description, feature]);

        var card = new CardPanel { Dock = DockStyle.Fill, Padding = new Padding(34, 20, 34, 20), Margin = new Padding(18, 12, 0, 12), CornerRadius = 18 };
        var heading = new Label { Text = "Chào mừng trở lại", AutoSize = true, TextAlign = ContentAlignment.MiddleCenter, ForeColor = UiTheme.Text, Font = new Font("Times New Roman", 18F, FontStyle.Bold) };
        var subheading = UiTheme.Label("Đăng nhập để tiếp tục cuộc trò chuyện.", 10F);
        subheading.TextAlign = ContentAlignment.MiddleCenter;
        subheading.Top = 50;

        var form = new TableLayoutPanel { Dock = DockStyle.Bottom, Height = 195, ColumnCount = 2, RowCount = 6, BackColor = UiTheme.Surface };
        form.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 50));
        form.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 50));
        for (var i = 0; i < 6; i++) form.RowStyles.Add(new RowStyle(SizeType.AutoSize));

        UiTheme.StyleInput(_txtHost); UiTheme.StyleInput(_txtPort); UiTheme.StyleInput(_txtUsername); UiTheme.StyleInput(_txtPassword);
        AddLabel(form, "MÁY CHỦ", 0, 0); AddLabel(form, "CỔNG", 1, 0);
        form.Controls.Add(_txtHost, 0, 1); form.Controls.Add(_txtPort, 1, 1);
        AddLabel(form, "TÊN HIỂN THỊ", 0, 2, 2); form.Controls.Add(_txtUsername, 0, 3); form.SetColumnSpan(_txtUsername, 2);
        AddLabel(form, "MẬT KHẨU", 0, 4, 2);
        var passwordPanel = new Panel { Dock = DockStyle.Fill, Height = 38, Margin = new Padding(0, 4, 0, 12) };
        _txtPassword.Dock = DockStyle.Fill;
        _txtPassword.Margin = Padding.Empty;
        UiTheme.StyleButton(_btnRevealPassword, UiTheme.SurfaceRaised, UiTheme.Muted);
        _btnRevealPassword.Height = 34;
        passwordPanel.Controls.Add(_txtPassword);
        passwordPanel.Controls.Add(_btnRevealPassword);
        form.Controls.Add(passwordPanel, 0, 5); form.SetColumnSpan(passwordPanel, 2);

        UiTheme.StyleButton(_btnConnect, UiTheme.Primary, Color.White); UiTheme.StyleButton(_btnRegister, UiTheme.SurfaceRaised, UiTheme.Text);
        _btnConnect.Dock = DockStyle.Bottom; _btnRegister.Dock = DockStyle.Bottom;
        _btnConnect.Margin = new Padding(0, 4, 7, 0); _btnRegister.Margin = new Padding(7, 4, 0, 0);
        
        _lblStatus.ForeColor = Color.FromArgb(251, 146, 160); 
        _lblStatus.Dock = DockStyle.Bottom; 
        _lblStatus.Padding = new Padding(0, 2, 0, 2);
        _lblStatus.Font = new Font("Segoe UI", 9F);

        var buttons = new TableLayoutPanel { Dock = DockStyle.Bottom, Height = 76, ColumnCount = 2, RowCount = 2, BackColor = UiTheme.Surface };
        buttons.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 56)); buttons.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 44));
        buttons.RowStyles.Add(new RowStyle(SizeType.Absolute, 44)); buttons.RowStyles.Add(new RowStyle(SizeType.Absolute, 28));
        buttons.Controls.Add(_btnConnect, 0, 0); buttons.Controls.Add(_btnRegister, 1, 0);
        UiTheme.StyleButton(_btnForgotPassword, Color.Transparent, UiTheme.Mint);
        _btnForgotPassword.Dock = DockStyle.Fill;
        _btnForgotPassword.FlatAppearance.MouseOverBackColor = Color.Transparent;
        buttons.Controls.Add(_btnForgotPassword, 0, 1);
        buttons.SetColumnSpan(_btnForgotPassword, 2);

        _loginView.Controls.AddRange([heading, subheading, form, _lblStatus, buttons]);
        BuildRegisterView();
        card.Controls.AddRange([_loginView, _registerView]);
        shell.Controls.Add(hero, 0, 0); shell.Controls.Add(card, 1, 0); Controls.Add(shell);
    }

    private static void AddLabel(TableLayoutPanel form, string text, int column, int row, int span = 1)
    {
        var label = UiTheme.Label(text, 8.5F, UiTheme.Muted, FontStyle.Bold);
        form.Controls.Add(label, column, row);
        if (span > 1) form.SetColumnSpan(label, span);
    }

    private async void BtnConnect_Click(object? sender, EventArgs e) => await ConnectAndAuthenticateAsync();

    private void BtnRegister_Click(object? sender, EventArgs e)
    {
        if (!int.TryParse(_txtPort.Text.Trim(), out var port))
        {
            SetStatus("Cổng kết nối không hợp lệ.", isError: true);
            return;
        }

        _loginView.Visible = false;
        _registerView.Visible = true;
        _txtRegisterUsername.Focus();
    }

    private void ShowForgotPasswordDialog()
    {
        if (!int.TryParse(_txtPort.Text.Trim(), out var port))
        {
            SetStatus("Cổng kết nối không hợp lệ.", isError: true);
            return;
        }

        using var dialog = new ForgotPasswordDialog(_txtHost.Text.Trim(), port);
        dialog.ShowDialog(this);
    }

    private void BuildRegisterView()
    {
        var layout = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            ColumnCount = 1,
            RowCount = 10,
            BackColor = UiTheme.Surface
        };
        layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 40));
        layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 32));
        for (var i = 0; i < 4; i++) layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 54));
        layout.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 40));
        layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 40));
        layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 40));

        var heading = new Label { Text = "Tạo tài khoản", AutoSize = true, ForeColor = UiTheme.Text, Font = new Font("Times New Roman", 20F, FontStyle.Bold) };
        layout.Controls.Add(heading, 0, 0);
        layout.Controls.Add(UiTheme.Label("Nhập email để nhận mã xác nhận.", 10F), 0, 1);
        AddRegisterField(layout, "TÀI KHOẢN", _txtRegisterUsername, 2);
        AddRegisterField(layout, "MẬT KHẨU", _txtRegisterPassword, 3);
        AddRegisterField(layout, "EMAIL", _txtRegisterEmail, 4);
        AddRegisterField(layout, "MÃ OTP", _txtRegisterOtp, 5);

        _lblRegisterStatus.ForeColor = UiTheme.Muted;
        
        layout.Controls.Add(_lblRegisterStatus, 0, 6);
        UiTheme.StyleButton(_btnRegisterSubmit, UiTheme.Primary, Color.White);
        _btnRegisterSubmit.Dock = DockStyle.Fill;
        //nút gửi mã OTP hoặc xác nhận OTP
        layout.Controls.Add(_btnRegisterSubmit, 0, 7);
        UiTheme.StyleButton(_btnRegisterResend, UiTheme.SurfaceRaised, UiTheme.Text);
        _btnRegisterResend.Dock = DockStyle.Fill;
        //nút gửi lại mã OTP
    
        layout.Controls.Add(_btnRegisterResend, 0, 8);
        UiTheme.StyleButton(_btnBackToLogin, UiTheme.SurfaceRaised, UiTheme.Text);
        _btnBackToLogin.Dock = DockStyle.Fill;
        layout.Controls.Add(_btnBackToLogin, 0, 9);
        //nút quay lại đăng nhập
        
        _registerView.Controls.Add(layout);
    }

    private static void AddRegisterField(TableLayoutPanel layout, string labelText, TextBox input, int row)
    {
        var panel = new Panel { Dock = DockStyle.Fill, BackColor = UiTheme.Surface };
        var label = UiTheme.Label(labelText, 8.5F, UiTheme.Muted, FontStyle.Bold);
        label.Dock = DockStyle.Top;
        input.Dock = DockStyle.Bottom;
        UiTheme.StyleInput(input);
        panel.Controls.Add(input);
        panel.Controls.Add(label);
        layout.Controls.Add(panel, 0, row);
    }

    private void ShowLoginView()
    {
        _registrationNetwork?.Close();
        _registrationNetwork = null;
        _txtRegisterOtp.Enabled = false;
        _txtRegisterOtp.Clear();
        _btnRegisterSubmit.Text = "Gửi mã OTP";
        _btnRegisterSubmit.Enabled = true;
        _btnRegisterResend.Enabled = false;
        _loginView.Visible = true;
        _registerView.Visible = false;
    }

    private async void BtnRegisterSubmit_Click(object? sender, EventArgs e)
    {
        if (_txtRegisterOtp.Enabled) await VerifyRegistrationOtpAsync();
        else await RequestRegistrationOtpAsync();
    }

    private async Task RequestRegistrationOtpAsync()
    {
        var username = _txtRegisterUsername.Text.Trim();
        var password = _txtRegisterPassword.Text;
        var email = _txtRegisterEmail.Text.Trim();
        if (username.Length < 3 || password.Length < 6 || !email.Contains('@'))
        {
            SetRegisterError("Hãy nhập tài khoản >= 3 ký tự, mật khẩu >= 6 ký tự và email hợp lệ.");
            return;
        }

        SetRegisterBusy(true, _txtRegisterOtp.Enabled ? "Đang gửi lại mã OTP..." : "Đang gửi mã OTP...");
        var network = _registrationNetwork ??= new NetworkClient();
        var response = new TaskCompletionSource<Envelope>(TaskCreationOptions.RunContinuationsAsynchronously);
        void OnMessage(Envelope envelope) { if (envelope.Type is "register-otp-sent" or "error") response.TrySetResult(envelope); }
        void OnDisconnected(string message) => response.TrySetException(new IOException(message));
        network.MessageReceived += OnMessage;
        network.Disconnected += OnDisconnected;
        try
        {
            if (!network.IsConnected)
                await network.ConnectAsync(_txtHost.Text.Trim(), int.Parse(_txtPort.Text.Trim()));
            await network.SendAsync("register", new RegisterRequest(username, password, email));
            var completed = await Task.WhenAny(response.Task, Task.Delay(TimeSpan.FromSeconds(60)));
            if (completed != response.Task) throw new TimeoutException("Máy chủ chưa phản hồi.");
            var result = await response.Task;
            if (result.Type == "error")
            {
                SetRegisterError(result.As<ErrorResponse>()?.Message ?? "Không thể gửi OTP.");
                network.Close();
                _registrationNetwork = null;
                return;
            }
            _txtRegisterOtp.Enabled = true;
            _btnRegisterSubmit.Text = "Xác nhận OTP";
            _btnRegisterResend.Enabled = true;
            _txtRegisterOtp.Focus();
            SetRegisterBusy(false, result.As<OtpSentResponse>()?.Message ?? "Đã gửi mã OTP.");
        }
        catch (Exception ex)
        {
            SetRegisterError($"Không thể đăng ký: {ex.Message}");
            network.Close();
            _registrationNetwork = null;
        }
        finally
        {
            network.MessageReceived -= OnMessage;
            network.Disconnected -= OnDisconnected;
            if (!_txtRegisterOtp.Enabled) SetRegisterBusy(false, _lblRegisterStatus.Text);
            else if (_registrationNetwork is not null) _btnRegisterResend.Enabled = true;
        }
    }

    private async Task VerifyRegistrationOtpAsync()
    {
        if (_txtRegisterOtp.Text.Trim().Length != 6) { SetRegisterError("Mã OTP phải có 6 chữ số."); return; }
        var network = _registrationNetwork;
        if (network is null) { SetRegisterError("Phiên đăng ký đã hết. Hãy gửi lại mã OTP."); return; }
        SetRegisterBusy(true, "Đang xác nhận...");
        var response = new TaskCompletionSource<Envelope>(TaskCreationOptions.RunContinuationsAsynchronously);
        void OnMessage(Envelope envelope) { if (envelope.Type is "auth" or "error") response.TrySetResult(envelope); }
        void OnDisconnected(string message) => response.TrySetException(new IOException(message));
        network.MessageReceived += OnMessage;
        network.Disconnected += OnDisconnected;
        try
        {
            await network.SendAsync("register-verify", new VerifyRegistrationRequest(_txtRegisterOtp.Text.Trim()));
            var completed = await Task.WhenAny(response.Task, Task.Delay(TimeSpan.FromSeconds(30)));
            if (completed != response.Task) throw new TimeoutException("Máy chủ chưa phản hồi.");
            var result = await response.Task;
            if (result.Type == "error") { SetRegisterError(result.As<ErrorResponse>()?.Message ?? "Mã OTP không đúng."); return; }
            _registrationNetwork = null;
            OpenChat(network, _txtRegisterUsername.Text.Trim());
        }
        catch (Exception ex) { SetRegisterError($"Không thể xác nhận: {ex.Message}"); }
        finally
        {
            network.MessageReceived -= OnMessage;
            network.Disconnected -= OnDisconnected;
            if (_registrationNetwork is not null) SetRegisterBusy(false, _lblRegisterStatus.Text);
        }
    }

    private void SetRegisterBusy(bool busy, string message)
    {
        _btnRegisterSubmit.Enabled = !busy;
        _btnRegisterResend.Enabled = !busy && _txtRegisterOtp.Enabled;
        _btnBackToLogin.Enabled = !busy;
        _txtRegisterUsername.Enabled = !busy && !_txtRegisterOtp.Enabled;
        _txtRegisterPassword.Enabled = !busy && !_txtRegisterOtp.Enabled;
        _txtRegisterEmail.Enabled = !busy && !_txtRegisterOtp.Enabled;
        _lblRegisterStatus.ForeColor = busy ? UiTheme.Mint : UiTheme.Muted;
        _lblRegisterStatus.Text = message;
    }

    private void SetRegisterError(string message)
    {
        _lblRegisterStatus.ForeColor = Color.FromArgb(251, 146, 160);
        _lblRegisterStatus.Text = message;
        _btnRegisterSubmit.Enabled = true;
        _btnRegisterResend.Enabled = _txtRegisterOtp.Enabled;
        _btnBackToLogin.Enabled = true;
    }

    private async Task ConnectAndAuthenticateAsync()
    {
        var username = _txtUsername.Text.Trim();
        var password = _txtPassword.Text;

        if (string.IsNullOrEmpty(username) || string.IsNullOrEmpty(password))
        {
            SetStatus("Hãy nhập tên hiển thị và mật khẩu.", isError: true);
            return;
        }

        if (!int.TryParse(_txtPort.Text.Trim(), out var port))
        {
            SetStatus("Cổng kết nối không hợp lệ.", isError: true);
            return;
        }

        SetUiState(isConnecting: true);
        SetStatus("Đang kết nối...", isError: false);

        var network = new NetworkClient();
        var authenticated = false;
        var authResult = new TaskCompletionSource<Envelope>(TaskCreationOptions.RunContinuationsAsynchronously);

        void OnAuthMessage(Envelope envelope)
        {
            if (envelope.Type is "auth" or "error")
                authResult.TrySetResult(envelope);
        }

        network.MessageReceived += OnAuthMessage;

        try
        {
            await network.ConnectAsync(_txtHost.Text.Trim(), port);
            await network.SendAsync("login", new AuthRequest(username, password));

            var completed = await Task.WhenAny(authResult.Task, Task.Delay(TimeSpan.FromSeconds(10)));
            if (completed != authResult.Task)
                throw new TimeoutException("Máy chủ chưa phản hồi yêu cầu xác thực.");

            var result = await authResult.Task;
            if (result.Type == "error")
            {
                var errMsg = result.As<ErrorResponse>()?.Message ?? "Có lỗi không xác định từ máy chủ.";
                SetStatus($"Lỗi xác thực: {errMsg}", isError: true);
                return;
            }

            authenticated = true;
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"[ConnectError] {ex}");
            SetStatus($"Không thể kết nối: {ex.Message}", isError: true);
            return;
        }
        finally
        {
            network.MessageReceived -= OnAuthMessage;
            
            if (!authenticated)
                network.Close();

            SetUiState(isConnecting: false);
        }

        OpenChat(network, username);
    }

    // Đã nâng cấp hàm SetStatus để tự gán ToolTip giúp dễ đọc hơn
    private void SetStatus(string text, bool isError)
    {
        _lblStatus.ForeColor = isError ? Color.FromArgb(251, 146, 160) : UiTheme.Mint;
        _lblStatus.Text = text;
        _statusToolTip.SetToolTip(_lblStatus, text);
    }

    private void SetUiState(bool isConnecting)
    {
        _btnConnect.Enabled = _btnRegister.Enabled = !isConnecting;
    }

    private void OpenChat(NetworkClient network, string username)
    {
        var mainForm = new UserMainForm(network, username);
        mainForm.FormClosed += (_, _) => Close();
        Hide();
        mainForm.Show();
    }
}