using ChatProtocol;

namespace ChatClient;

public sealed class RegisterForm : Form
{
    private readonly string _host;
    private readonly int _port;
    private readonly TextBox _txtUsername = new();
    private readonly TextBox _txtPassword = new() { UseSystemPasswordChar = true };
    private readonly TextBox _txtEmail = new();
    private readonly TextBox _txtOtp = new() { MaxLength = 6 };
    private readonly Button _btnSubmit = new() { Text = "Gửi mã OTP" };
    private readonly Button _btnResend = new() { Text = "Gửi lại mã OTP", Enabled = false };
    private readonly Label _lblStatus = new() { AutoSize = true };
    private NetworkClient? _network;
    private bool _completed;

    public NetworkClient AuthenticatedNetwork => _network!;
    public string Username => _txtUsername.Text.Trim();

    public RegisterForm(string host, int port)
    {
        _host = host;
        _port = port;
        Text = "Tạo tài khoản - RE:CHAT";
        ClientSize = new Size(440, 460);
        MinimumSize = new Size(440, 460);
        StartPosition = FormStartPosition.CenterParent;
        BackColor = UiTheme.Canvas;
        Font = new Font("Segoe UI", 10F);
        FormClosed += (_, _) =>
        {
            if (!_completed) _network?.Close();
        };
        BuildLayout();
        _btnSubmit.Click += BtnSubmit_Click;
        _btnResend.Click += async (_, _) => await RequestOtpAsync();
        AcceptButton = _btnSubmit;
    }

    private void BuildLayout()
    {
        var card = new CardPanel
        {
            Dock = DockStyle.Fill,
            Padding = new Padding(34, 28, 34, 28),
            CornerRadius = 18
        };
        var layout = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            ColumnCount = 1,
            RowCount = 10,
            BackColor = UiTheme.Surface
        };
        layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 38));
        layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 30));
        for (var i = 0; i < 6; i++) layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 48));
        layout.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 40));

        var heading = new Label
        {
            Text = "Tạo tài khoản",
            AutoSize = true,
            ForeColor = UiTheme.Text,
            Font = new Font("Times New Roman", 20F, FontStyle.Bold)
        };
        var subheading = UiTheme.Label("Nhập email để nhận mã xác nhận.", 10F);
        layout.Controls.Add(heading, 0, 0);
        layout.Controls.Add(subheading, 0, 1);
        AddField(layout, "TÀI KHOẢN", _txtUsername, 2);
        AddField(layout, "MẬT KHẨU", _txtPassword, 3);
        AddField(layout, "EMAIL", _txtEmail, 4);
        AddField(layout, "MÃ OTP", _txtOtp, 5);
        _txtOtp.Enabled = false;

        UiTheme.StyleButton(_btnSubmit, UiTheme.Primary, Color.White);
        _btnSubmit.Dock = DockStyle.Fill;
        layout.Controls.Add(_btnSubmit, 0, 7);
        UiTheme.StyleButton(_btnResend, UiTheme.SurfaceRaised, UiTheme.Text);
        _btnResend.Dock = DockStyle.Fill;
        layout.Controls.Add(_btnResend, 0, 6);
        _lblStatus.ForeColor = UiTheme.Muted;
        _lblStatus.MaximumSize = new Size(360, 0);
        layout.Controls.Add(_lblStatus, 0, 8);
        card.Controls.Add(layout);
        Controls.Add(card);
    }

    private static void AddField(TableLayoutPanel layout, string labelText, TextBox input, int row)
    {
        var panel = new Panel { Dock = DockStyle.Fill, BackColor = UiTheme.Surface };
        var label = UiTheme.Label(labelText, 8.5F, UiTheme.Muted, FontStyle.Bold);
        label.Dock = DockStyle.Top;
        input.Dock = DockStyle.Bottom;
        UiTheme.StyleInput(input);
        input.Margin = new Padding(0, 3, 0, 0);
        panel.Controls.Add(input);
        panel.Controls.Add(label);
        layout.Controls.Add(panel, 0, row);
    }

    private async void BtnSubmit_Click(object? sender, EventArgs e)
    {
        if (!_txtOtp.Enabled)
            await RequestOtpAsync();
        else
            await VerifyOtpAsync();
    }

    private async Task RequestOtpAsync()
    {
        if (Username.Length < 3 || _txtPassword.Text.Length < 6 || !_txtEmail.Text.Contains('@'))
        {
            SetError("Hãy nhập tài khoản >= 3 ký tự, mật khẩu >= 6 ký tự và email hợp lệ.");
            return;
        }

        SetBusy(true, _txtOtp.Enabled ? "Đang gửi lại mã OTP..." : "Đang gửi mã OTP...");
        _network ??= new NetworkClient();
        var response = new TaskCompletionSource<Envelope>(TaskCreationOptions.RunContinuationsAsynchronously);
        void OnMessage(Envelope envelope)
        {
            if (envelope.Type is "register-otp-sent" or "error") response.TrySetResult(envelope);
        }
        _network.MessageReceived += OnMessage;
        try
        {
            if (!_network.IsConnected)
                await _network.ConnectAsync(_host, _port);
            await _network.SendAsync("register", new RegisterRequest(Username, _txtPassword.Text, _txtEmail.Text.Trim()));
            var completed = await Task.WhenAny(response.Task, Task.Delay(TimeSpan.FromSeconds(60)));
            if (completed != response.Task) throw new TimeoutException("Máy chủ chưa phản hồi.");
            var result = await response.Task;
            if (result.Type == "error")
            {
                SetError(result.As<ErrorResponse>()?.Message ?? "Không thể gửi OTP.");
                _network.Close();
                return;
            }

            _txtOtp.Enabled = true;
            _txtOtp.Focus();
            _btnSubmit.Text = "Xác nhận OTP";
            _btnResend.Enabled = true;
            SetBusy(false, result.As<OtpSentResponse>()?.Message ?? "Đã gửi mã OTP.");
        }
        catch (Exception ex)
        {
            SetError($"Không thể đăng ký: {ex.Message}");
            _network.Close();
        }
        finally
        {
            _network.MessageReceived -= OnMessage;
            if (!_txtOtp.Enabled) SetBusy(false, _lblStatus.Text);
            else if (!_completed) _btnResend.Enabled = true;
        }
    }

    private async Task VerifyOtpAsync()
    {
        if (_txtOtp.Text.Trim().Length != 6)
        {
            SetError("Mã OTP phải có 6 chữ số.");
            return;
        }

        SetBusy(true, "Đang xác nhận...");
        var response = new TaskCompletionSource<Envelope>(TaskCreationOptions.RunContinuationsAsynchronously);
        void OnMessage(Envelope envelope)
        {
            if (envelope.Type is "auth" or "error") response.TrySetResult(envelope);
        }
        _network!.MessageReceived += OnMessage;
        try
        {
            await _network.SendAsync("register-verify", new VerifyRegistrationRequest(_txtOtp.Text.Trim()));
            var completed = await Task.WhenAny(response.Task, Task.Delay(TimeSpan.FromSeconds(30)));
            if (completed != response.Task) throw new TimeoutException("Máy chủ chưa phản hồi.");
            var result = await response.Task;
            if (result.Type == "error")
            {
                SetError(result.As<ErrorResponse>()?.Message ?? "Mã OTP không đúng.");
                return;
            }

            _completed = true;
            DialogResult = DialogResult.OK;
            Close();
        }
        catch (Exception ex)
        {
            SetError($"Không thể xác nhận: {ex.Message}");
        }
        finally
        {
            _network.MessageReceived -= OnMessage;
            if (!_completed) SetBusy(false, _lblStatus.Text);
        }
    }

    private void SetBusy(bool busy, string message)
    {
        _btnSubmit.Enabled = !busy;
        _btnResend.Enabled = !busy && _txtOtp.Enabled;
        _txtUsername.Enabled = !busy && !_txtOtp.Enabled;
        _txtPassword.Enabled = !busy && !_txtOtp.Enabled;
        _txtEmail.Enabled = !busy && !_txtOtp.Enabled;
        _lblStatus.ForeColor = busy ? UiTheme.Mint : UiTheme.Muted;
        _lblStatus.Text = message;
    }

    private void SetError(string message)
    {
        _lblStatus.ForeColor = Color.FromArgb(251, 146, 160);
        _lblStatus.Text = message;
        _btnSubmit.Enabled = true;
        _btnResend.Enabled = _txtOtp.Enabled;
    }
}
