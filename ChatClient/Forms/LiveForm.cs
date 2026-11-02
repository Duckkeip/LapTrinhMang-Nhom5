using System.Text.Json;
using ChatProtocol;
using Microsoft.Web.WebView2.Core;
using Microsoft.Web.WebView2.WinForms;

namespace ChatClient;

/// <summary>One-to-one LAN WebRTC call view. The .NET server relays SDP/ICE only.</summary>
public sealed class LiveForm : UserControl
{
    private readonly NetworkClient _network;
    private readonly string _username;
    private readonly string _channelId;
    private readonly Action? _leaveChannel;
    private readonly WebView2 _mediaView = new() { Dock = DockStyle.Fill, DefaultBackgroundColor = Color.FromArgb(24, 28, 40) };
    private readonly Label _deviceStatus = new() { Dock = DockStyle.Top, Height = 34, TextAlign = ContentAlignment.MiddleCenter };
    private readonly Button _microphoneButton = new() { Width = 150, Height = 42, Text = "Bật mic" };
    private readonly Button _testMicrophoneButton = new() { Width = 150, Height = 42, Text = "Test mic" };
    private readonly Button _cameraButton = new() { Width = 150, Height = 42, Text = "Bật camera" };
    private readonly Button _leaveButton = new() { Width = 150, Height = 42, Text = "Rời kênh" };
    private readonly ProgressBar _microphoneLevel = new() { Width = 150, Height = 12, Maximum = 100, Style = ProgressBarStyle.Continuous };
    private readonly ListBox _participants = new() { Dock = DockStyle.Fill };
    private bool _microphoneEnabled;
    private bool _cameraEnabled;
    private bool _joined;
    private bool _webReady;
    private LiveKitCredentials? _pendingCredentials;

    public LiveForm(NetworkClient network, string username, string channelId, Func<bool>? ignoreProfileResponses = null, Action? leaveChannel = null)
    {
        _network = network;
        _username = username;
        _channelId = channelId;
        _leaveChannel = leaveChannel;
        BackColor = UiTheme.Canvas;
        ForeColor = UiTheme.Text;
        Dock = DockStyle.Fill;
        BuildLayout();
        UpdateControls();
        _ = InitializeMediaAsync();
        _network.Send("voice-join", new JoinVoiceChannelRequest(_channelId));
        _joined = true;
    }

    private void BuildLayout()
    {
        var header = new Panel { Dock = DockStyle.Top, Height = 76, Padding = new Padding(0, 8, 0, 0) };
        header.Controls.Add(new Label { Text = "Kênh đàm thoại", Dock = DockStyle.Top, Height = 34, ForeColor = UiTheme.Text, Font = new Font("Segoe UI Semibold", 18F), TextAlign = ContentAlignment.MiddleLeft });
        header.Controls.Add(_deviceStatus);

        var participantsPanel = new Panel { Dock = DockStyle.Right, Width = 200, Padding = new Padding(12, 8, 0, 0) };
        participantsPanel.Controls.Add(_participants);
        participantsPanel.Controls.Add(new Label { Text = "NGƯỜI THAM GIA", Dock = DockStyle.Top, Height = 30, ForeColor = UiTheme.Muted, Font = new Font("Segoe UI Semibold", 8.5F) });
        _participants.BackColor = UiTheme.Surface;
        _participants.ForeColor = UiTheme.Text;
        _participants.BorderStyle = BorderStyle.None;

        var controls = new FlowLayoutPanel { Dock = DockStyle.Bottom, Height = 70, FlowDirection = FlowDirection.LeftToRight, WrapContents = false, Padding = new Padding(0, 12, 0, 8) };
        _microphoneButton.Click += async (_, _) => await ExecuteMediaCommandAsync("toggleMicrophone()");
        _testMicrophoneButton.Click += async (_, _) => await ExecuteMediaCommandAsync("testMicrophone()");
        _cameraButton.Click += async (_, _) => await ExecuteMediaCommandAsync("toggleCamera()");
        _leaveButton.Click += (_, _) => LeaveChannel();
        controls.Controls.AddRange([_microphoneButton, _testMicrophoneButton, _microphoneLevel, _cameraButton, _leaveButton]);

        var main = new Panel { Dock = DockStyle.Fill };
        main.Controls.Add(_mediaView);
        main.Controls.Add(participantsPanel);
        main.Controls.Add(controls);
        Controls.Add(main);
        Controls.Add(header);
    }

    private async Task InitializeMediaAsync()
    {
        try
        {
            await _mediaView.EnsureCoreWebView2Async();
            _mediaView.CoreWebView2.Settings.IsWebMessageEnabled = true;
            _mediaView.CoreWebView2.WebMessageReceived += OnWebMessageReceived;
            var mediaRoot = Path.Combine(AppContext.BaseDirectory, "Assets", "LiveMedia");
            _mediaView.CoreWebView2.SetVirtualHostNameToFolderMapping("rechat.local", mediaRoot,
                CoreWebView2HostResourceAccessKind.DenyCors);
            _mediaView.CoreWebView2.NavigationCompleted += OnMediaNavigationCompleted;
            _mediaView.CoreWebView2.Navigate("https://rechat.local/index.html");
        }
        catch (Exception ex)
        {
            _deviceStatus.Text = "Không mở được WebRTC: hãy cài Microsoft Edge WebView2 Runtime.";
            ClientLog.Error($"WebRTC initialization failed: {ex.Message}");
        }
    }

    public void UpdateVoiceState(VoiceChannelState state)
    {
        if (state.ChannelId != _channelId || IsDisposed) return;
        _participants.BeginUpdate();
        _participants.Items.Clear();
        foreach (var participant in state.Participants)
        {
            var self = string.Equals(participant.Username, _username, StringComparison.OrdinalIgnoreCase);
            _participants.Items.Add($"{(participant.MicrophoneEnabled ? "🎙" : "🔇")} {(participant.CameraEnabled ? "📹" : "")} {participant.Username}{(self ? " (bạn)" : "")}".Trim());
        }
        _participants.EndUpdate();

    }

    public void ConfigureLiveKit(LiveKitCredentials credentials)
    {
        if (IsDisposed) return;
        _pendingCredentials = credentials;
        if (_webReady)
            _ = StartLiveKitAsync(credentials);
    }

    private void OnMediaNavigationCompleted(object? sender, CoreWebView2NavigationCompletedEventArgs e)
    {
        _webReady = e.IsSuccess;
        UpdateControls();
        if (_webReady && _pendingCredentials != null)
            _ = StartLiveKitAsync(_pendingCredentials);
    }

    private async Task StartLiveKitAsync(LiveKitCredentials credentials)
    {
        if (!_webReady) return;
        _pendingCredentials = null;
        await ExecuteMediaCommandAsync($"connectLiveKit({JsonSerializer.Serialize(credentials.Url)}, {JsonSerializer.Serialize(credentials.Token)})");
    }

    private async Task ExecuteMediaCommandAsync(string command)
    {
        if (!_webReady || _mediaView.CoreWebView2 == null) return;
        try { await _mediaView.CoreWebView2.ExecuteScriptAsync($"window.live && window.live.{command};"); }
        catch (Exception ex) { ClientLog.Error($"LiveKit command failed: {ex.Message}"); }
    }

    private void OnWebMessageReceived(object? sender, CoreWebView2WebMessageReceivedEventArgs args)
    {
        try
        {
            using var document = JsonDocument.Parse(args.WebMessageAsJson);
            var root = document.RootElement;
            var type = root.GetProperty("type").GetString();
            if (type == "device")
            {
                _microphoneEnabled = root.GetProperty("microphoneEnabled").GetBoolean();
                _cameraEnabled = root.GetProperty("cameraEnabled").GetBoolean();
                if (_joined) _network.Send("voice-device-state", new UpdateVoiceDeviceStateRequest(_microphoneEnabled, _cameraEnabled));
                UpdateControls();
            }
            else if (type == "mic-test")
            {
                var level = Math.Clamp(root.GetProperty("level").GetInt32(), 0, 100);
                _microphoneLevel.Value = level;
                _testMicrophoneButton.Text = root.GetProperty("running").GetBoolean() ? "Dừng test mic" : "Test mic";
                if (!root.GetProperty("running").GetBoolean()) _microphoneLevel.Value = 0;
            }
            else if (type == "livekit")
            {
                _deviceStatus.Text = root.GetProperty("status").GetString() == "connected"
                    ? "LiveKit đã kết nối"
                    : "LiveKit chưa kết nối";
            }
            else if (type == "error") _deviceStatus.Text = root.GetProperty("message").GetString() ?? "Không truy cập được thiết bị media.";
        }
        catch (Exception ex) { ClientLog.Error($"Invalid WebRTC browser message: {ex.Message}"); }
    }

    private void UpdateControls()
    {
        _microphoneButton.Text = _microphoneEnabled ? "Tắt mic" : "Bật mic";
        _cameraButton.Text = _cameraEnabled ? "Tắt camera" : "Bật camera";
        if (_webReady) _deviceStatus.Text = $"Mic: {(_microphoneEnabled ? "Bật" : "Tắt")}    Camera: {(_cameraEnabled ? "Bật" : "Tắt")}";
        UiTheme.StyleButton(_microphoneButton, _microphoneEnabled ? UiTheme.Primary : UiTheme.SurfaceRaised, UiTheme.Text);
        UiTheme.StyleButton(_testMicrophoneButton, UiTheme.SurfaceRaised, UiTheme.Text);
        UiTheme.StyleButton(_cameraButton, _cameraEnabled ? UiTheme.Primary : UiTheme.SurfaceRaised, UiTheme.Text);
        UiTheme.StyleButton(_leaveButton, Color.FromArgb(155, 55, 70), Color.White);
    }

    private void LeaveChannel()
    {
        LeaveVoiceSession();
        _leaveChannel?.Invoke();
    }

    private void LeaveVoiceSession()
    {
        if (!_joined) return;
        _joined = false;
        _network.Send("voice-leave", new LeaveVoiceChannelRequest());
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing)
        {
            LeaveVoiceSession();
            if (_webReady) _ = ExecuteMediaCommandAsync("stopMicrophoneTest()");
            if (_mediaView.CoreWebView2 != null)
            {
                _mediaView.CoreWebView2.WebMessageReceived -= OnWebMessageReceived;
                _mediaView.CoreWebView2.NavigationCompleted -= OnMediaNavigationCompleted;
            }
        }
        base.Dispose(disposing);
    }

}
