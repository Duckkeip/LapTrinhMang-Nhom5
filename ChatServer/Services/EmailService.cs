using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;

namespace ChatServer;

public sealed class EmailService
{
    private static readonly HttpClient HttpClient = new();
    private readonly string _apiKey;
    private readonly string _sender;
    private readonly string _scriptUrl;
    private readonly string _scriptSecret;

    public EmailService(IReadOnlyDictionary<string, string> config)
    {
        _apiKey = config.GetValueOrDefault("RESEND_API_KEY", "").Trim();
        _sender = config.GetValueOrDefault("EMAIL_FROM", "").Trim();
        _scriptUrl = config.GetValueOrDefault("EMAIL_SCRIPT_URL", "").Trim();
        _scriptSecret = config.GetValueOrDefault("EMAIL_SCRIPT_SECRET", "").Trim();
    }

    public bool IsConfigured =>
        (_scriptUrl.Length > 0 && _scriptSecret.Length > 0) ||
        (_apiKey.Length > 0 && _sender.Length > 0);

    public async Task SendOtpAsync(string recipient, string otp)
    {
        var htmlBody = BuildEmailHtml(otp);
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(20));

        if (_scriptUrl.Length > 0 && _scriptSecret.Length > 0)
        {
            await SendWithAppsScriptAsync(recipient, htmlBody, timeout.Token);
            return;
        }

        if (_apiKey.Length > 0 && _sender.Length > 0)
        {
            await SendWithResendAsync(recipient, htmlBody, timeout.Token);
            return;
        }

        throw new InvalidOperationException(
            "Cau hinh EMAIL_SCRIPT_URL va EMAIL_SCRIPT_SECRET, hoac RESEND_API_KEY va EMAIL_FROM."
        );
    }

    private async Task SendWithAppsScriptAsync(string recipient, string htmlBody, CancellationToken cancellationToken)
    {
        using var request = new HttpRequestMessage(HttpMethod.Post, _scriptUrl);
        request.Content = new StringContent(
            JsonSerializer.Serialize(new
            {
                secret = _scriptSecret,
                to = recipient,
                subject = "RE:CHAT - Mã xác nhận bảo mật",
                html = htmlBody
            }),
            Encoding.UTF8,
            "application/json");

        using var response = await HttpClient.SendAsync(request, cancellationToken);
        var responseBody = await response.Content.ReadAsStringAsync(cancellationToken);
        if (!response.IsSuccessStatusCode)
            throw new HttpRequestException($"Apps Script returned HTTP {(int)response.StatusCode}: {responseBody}");

        using var document = JsonDocument.Parse(responseBody);
        var root = document.RootElement;
        if (!root.TryGetProperty("ok", out var ok) || !ok.GetBoolean())
        {
            var error = root.TryGetProperty("error", out var errorValue)
                ? errorValue.GetString()
                : "Unknown Apps Script error.";
            throw new HttpRequestException($"Apps Script failed to send email: {error}");
        }
    }

    private async Task SendWithResendAsync(string recipient, string htmlBody, CancellationToken cancellationToken)
    {
        using var request = new HttpRequestMessage(HttpMethod.Post, "https://api.resend.com/emails");
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", _apiKey);
        request.Content = new StringContent(
            JsonSerializer.Serialize(new
            {
                from = _sender,
                to = new[] { recipient },
                subject = "RE:CHAT - Mã xác nhận bảo mật",
                html = htmlBody
            }),
            Encoding.UTF8,
            "application/json");

        using var response = await HttpClient.SendAsync(request, cancellationToken);
        if (!response.IsSuccessStatusCode)
        {
            var responseBody = await response.Content.ReadAsStringAsync(cancellationToken);
            throw new HttpRequestException(
                $"Resend API returned HTTP {(int)response.StatusCode}: {responseBody}",
                null,
                response.StatusCode);
        }
    }

    private static string BuildEmailHtml(string otp)
    {
        return $@"
<!DOCTYPE html>
<html lang=""vi"">
<head>
    <meta charset=""UTF-8"">
    <meta name=""viewport"" content=""width=device-width, initial-scale=1.0"">
    <title>RE:CHAT - Xác nhận bảo mật</title>
</head>

<body style=""
    margin:0;
    padding:0;
    background-color:#f4f7fb;
    font-family:Arial,Helvetica,sans-serif;
"">

    <table width=""100%"" cellpadding=""0"" cellspacing=""0"" border=""0""
           style=""background-color:#f4f7fb;padding:40px 15px;"">
        <tr>
            <td align=""center"">

                <!-- Container -->
                <table width=""100%"" cellpadding=""0"" cellspacing=""0"" border=""0""
                       style=""
                           max-width:560px;
                           background:#ffffff;
                           border-radius:18px;
                           overflow:hidden;
                           box-shadow:0 8px 30px rgba(0,0,0,0.08);
                       "">

                    <!-- Header -->
                    <tr>
                        <td align=""center""
                            style=""
                                background:linear-gradient(135deg,#6366f1,#8b5cf6);
                                padding:35px 25px;
                            "">

                            <div style=""
                                font-size:32px;
                                font-weight:bold;
                                color:#ffffff;
                                letter-spacing:1px;
                            "">
                                RE:CHAT
                            </div>

                            <div style=""
                                margin-top:8px;
                                font-size:14px;
                                color:#e9e7ff;
                            "">
                                Multi-room Chat
                            </div>

                        </td>
                    </tr>

                    <!-- Content -->
                    <tr>
                        <td style=""padding:40px 35px 30px 35px;"">

                            <h2 style=""
                                margin:0 0 12px 0;
                                color:#1f2937;
                                font-size:24px;
                                text-align:center;
                            "">
                                Xác nhận bảo mật 🔐
                            </h2>

                            <p style=""
                                margin:0 0 25px 0;
                                color:#6b7280;
                                font-size:15px;
                                line-height:1.7;
                                text-align:center;
                            "">
                                Bạn vừa yêu cầu thực hiện thay đổi liên quan
                                đến tài khoản RE:CHAT.
                            </p>

                            <p style=""
                                margin:0 0 12px 0;
                                color:#374151;
                                font-size:15px;
                                text-align:center;
                            "">
                                Mã xác nhận của bạn là:
                            </p>

                            <!-- OTP -->
                            <div style=""
                                background:#f3f4ff;
                                border:2px dashed #6366f1;
                                border-radius:14px;
                                padding:20px;
                                text-align:center;
                                margin:0 auto 25px auto;
                            "">
                                <span style=""
                                    font-size:36px;
                                    font-weight:bold;
                                    letter-spacing:10px;
                                    color:#4f46e5;
                                "">
                                    {otp}
                                </span>
                            </div>

                            <!-- Expiry -->
                            <div style=""
                                background:#fff7ed;
                                border-radius:10px;
                                padding:14px 16px;
                                margin-bottom:25px;
                                text-align:center;
                            "">
                                <span style=""
                                    color:#c2410c;
                                    font-size:14px;
                                "">
                                    ⏱ Mã này có hiệu lực trong <strong>10 phút</strong>.
                                </span>
                            </div>

                            <!-- Security notice -->
                            <div style=""
                                border-top:1px solid #e5e7eb;
                                padding-top:22px;
                            "">
                                <p style=""
                                    margin:0;
                                    color:#6b7280;
                                    font-size:13px;
                                    line-height:1.7;
                                "">
                                    <strong style=""color:#374151;"">
                                        Lưu ý bảo mật:
                                    </strong>
                                    Không chia sẻ mã OTP này với bất kỳ ai.
                                    RE:CHAT sẽ không bao giờ yêu cầu bạn cung cấp
                                    mã xác nhận qua tin nhắn hoặc cuộc gọi.
                                </p>

                                <p style=""
                                    margin:15px 0 0 0;
                                    color:#9ca3af;
                                    font-size:13px;
                                    line-height:1.6;
                                "">
                                    Nếu bạn không thực hiện yêu cầu này,
                                    hãy bỏ qua email và kiểm tra lại bảo mật
                                    tài khoản của mình.
                                </p>
                            </div>

                        </td>
                    </tr>

                    <!-- Footer -->
                    <tr>
                        <td align=""center""
                            style=""
                                background:#f9fafb;
                                padding:22px;
                                border-top:1px solid #f0f0f0;
                            "">

                            <p style=""
                                margin:0;
                                color:#9ca3af;
                                font-size:12px;
                            "">
                                © 2026 RE:CHAT · Email tự động
                            </p>

                            <p style=""
                                margin:7px 0 0 0;
                                color:#c0c4cc;
                                font-size:11px;
                            "">
                                Vui lòng không trả lời email này.
                            </p>

                        </td>
                    </tr>

                </table>

            </td>
        </tr>
    </table>

</body>
</html>";
    }
}

