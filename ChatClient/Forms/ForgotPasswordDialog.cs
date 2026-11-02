using ChatProtocol;

namespace ChatClient;

internal sealed class ForgotPasswordDialog : Form
{
    private readonly string _host;
    private readonly int _port;
    private readonly TextBox _email = new();
    private readonly TextBox _otp = new() { MaxLength = 6, Enabled = false };
    private readonly TextBox _newPassword = new() { UseSystemPasswordChar = true, Enabled = false };
    private readonly TextBox _confirmPassword = new() { UseSystemPasswordChar = true, Enabled = false };
    private readonly Button _sendOtp = new() { Text = "Gửi mã OTP" };
    private readonly Button _reset = new() { Text = "Đặt lại mật khẩu", Enabled = false };
    private readonly Label _status = new() { AutoSize = true, MaximumSize = new Size(390, 0) };
    private NetworkClient? _network;

    public ForgotPasswordDialog(string host, int port)
    {
        _host = host;
        _port = port;
        Text = "Quên mật khẩu";
        ClientSize = new Size(480, 390);
        StartPosition = FormStartPosition.CenterParent;
        FormBorderStyle = FormBorderStyle.FixedDialog;
        MaximizeBox = false;
        MinimizeBox = false;
        BackColor = UiTheme.Canvas;
        ForeColor = UiTheme.Text;
        Font = new Font("Segoe UI", 10F);
        BuildLayout();
        _sendOtp.Click += async (_, _) => await RequestOtpAsync();
        _reset.Click += async (_, _) => await ResetPasswordAsync();
        FormClosed += (_, _) => _network?.Close();
    }

    private void BuildLayout()
    {
        var card = new CardPanel { Dock = DockStyle.Fill, Padding = new Padding(28), BackColor = UiTheme.Surface };
        var layout = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 2, RowCount = 8 };
        layout.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 145));
        layout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 42));
        for (var i = 1; i < 7; i++) layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 43));
        layout.RowStyles.Add(new RowStyle(SizeType.Percent, 100));

        AddField(layout, 0, "Email đã đăng ký", _email);
        AddField(layout, 1, "Mã OTP", _otp);
        AddField(layout, 2, "Mật khẩu mới", _newPassword);
        AddField(layout, 3, "Nhập lại mật khẩu mới", _confirmPassword);

        UiTheme.StyleButton(_sendOtp, UiTheme.Primary, Color.White);
        UiTheme.StyleButton(_reset, UiTheme.Mint, UiTheme.Canvas);
        _sendOtp.Dock = DockStyle.Fill;
        _reset.Dock = DockStyle.Fill;
        layout.Controls.Add(_sendOtp, 0, 4);
        layout.SetColumnSpan(_sendOtp, 2);
        layout.Controls.Add(_reset, 0, 5);
        layout.SetColumnSpan(_reset, 2);
        _status.ForeColor = UiTheme.Muted;
        layout.Controls.Add(_status, 0, 6);
        layout.SetColumnSpan(_status, 2);

        card.Controls.Add(layout);
        Controls.Add(card);
    }

    private static void AddField(TableLayoutPanel layout, int row, string label, TextBox input)
    {
        layout.Controls.Add(new Label { Text = label, Dock = DockStyle.Fill, ForeColor = UiTheme.Text, TextAlign = ContentAlignment.MiddleLeft }, 0, row);
        UiTheme.StyleInput(input);
        input.Dock = DockStyle.Fill;
        layout.Controls.Add(input, 1, row);
    }

    private async Task RequestOtpAsync()
    {
        var email = _email.Text.Trim();
        if (!email.Contains('@') || !email.Contains('.'))
        {
            SetStatus("Hãy nhập email hợp lệ.", true);
            return;
        }

        SetBusy(true, "Đang gửi mã OTP...");
        var network = new NetworkClient();
        _network = network;
        var response = new TaskCompletionSource<Envelope>(TaskCreationOptions.RunContinuationsAsynchronously);
        void OnMessage(Envelope envelope)
        {
            if (envelope.Type is "password-reset-otp-sent" or "error") response.TrySetResult(envelope);
        }
        void OnDisconnected(string message) => response.TrySetException(new IOException(message));
        network.MessageReceived += OnMessage;
        network.Disconnected += OnDisconnected;
        try
        {
            await network.ConnectAsync(_host, _port);
            await network.SendAsync("request-password-reset-otp", new RequestPasswordResetOtp(email));
            var completed = await Task.WhenAny(response.Task, Task.Delay(TimeSpan.FromSeconds(60)));
            if (completed != response.Task) throw new TimeoutException("Máy chủ chưa phản hồi.");
            var result = await response.Task;
            if (result.Type == "error")
            {
                SetStatus(result.As<ErrorResponse>()?.Message ?? "Không thể gửi OTP.", true);
                return;
            }

            _otp.Enabled = true;
            _newPassword.Enabled = true;
            _confirmPassword.Enabled = true;
            _sendOtp.Enabled = false;
            _reset.Enabled = true;
            SetStatus(result.As<PasswordResetOtpSentResponse>()?.Message ?? "Đã gửi OTP.", false);
            _otp.Focus();
        }
        catch (Exception ex)
        {
            SetStatus($"Không thể gửi OTP: {ex.Message}", true);
            network.Close();
            _network = null;
        }
        finally
        {
            network.MessageReceived -= OnMessage;
            network.Disconnected -= OnDisconnected;
            if (_sendOtp.Enabled) SetBusy(false, _status.Text);
        }
    }

    private async Task ResetPasswordAsync()
    {
        if (_network == null) { SetStatus("Phiên OTP đã hết. Hãy gửi lại mã.", true); return; }
        if (_otp.Text.Trim().Length != 6) { SetStatus("Mã OTP phải có 6 chữ số.", true); return; }
        if (_newPassword.Text.Length < 6) { SetStatus("Mật khẩu mới phải có ít nhất 6 ký tự.", true); return; }
        if (_newPassword.Text != _confirmPassword.Text) { SetStatus("Mật khẩu nhập lại không khớp.", true); return; }

        SetBusy(true, "Đang cập nhật mật khẩu...");
        var response = new TaskCompletionSource<Envelope>(TaskCreationOptions.RunContinuationsAsynchronously);
        void OnMessage(Envelope envelope)
        {
            if (envelope.Type is "password-reset" or "error") response.TrySetResult(envelope);
        }
        _network.MessageReceived += OnMessage;
        try
        {
            await _network.SendAsync("verify-password-reset", new VerifyPasswordResetRequest(_otp.Text.Trim(), _newPassword.Text));
            var completed = await Task.WhenAny(response.Task, Task.Delay(TimeSpan.FromSeconds(30)));
            if (completed != response.Task) throw new TimeoutException("Máy chủ chưa phản hồi.");
            var result = await response.Task;
            if (result.Type == "error")
            {
                SetStatus(result.As<ErrorResponse>()?.Message ?? "OTP không đúng.", true);
                return;
            }

            MessageBox.Show(this, result.As<SettingsUpdatedResponse>()?.Message ?? "Đổi mật khẩu thành công.", "Quên mật khẩu", MessageBoxButtons.OK, MessageBoxIcon.Information);
            Close();
        }
        catch (Exception ex)
        {
            SetStatus($"Không thể cập nhật: {ex.Message}", true);
        }
        finally
        {
            _network.MessageReceived -= OnMessage;
            if (IsHandleCreated && !IsDisposed) SetBusy(false, _status.Text);
        }
    }

    private void SetBusy(bool busy, string message)
    {
        _sendOtp.Enabled = !busy && !_otp.Enabled;
        _reset.Enabled = !busy && _otp.Enabled;
        _email.Enabled = !busy && !_otp.Enabled;
        _otp.Enabled = _otp.Enabled || busy;
        _newPassword.Enabled = _newPassword.Enabled || busy;
        _confirmPassword.Enabled = _confirmPassword.Enabled || busy;
        _status.ForeColor = busy ? UiTheme.Mint : UiTheme.Muted;
        _status.Text = message;
    }

    private void SetStatus(string message, bool error)
    {
        _status.ForeColor = error ? Color.FromArgb(251, 146, 160) : UiTheme.Muted;
        _status.Text = message;
        _sendOtp.Enabled = !_otp.Enabled;
        _reset.Enabled = _otp.Enabled;
    }
}
