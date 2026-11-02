using System.Net;
using System.Net.Mail;
using System.Net.WebSockets;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using ChatProtocol;
using QRCoder;
using System.Collections.Generic; // nếu chưa có
using System.Linq; // nếu chưa có
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;

namespace ChatServer;

public static class Program
{
    // Danh sach phong mac dinh
    private static readonly string[] DefaultRooms = { "General", "Random", "Tech" };
    private static readonly RoomManager Rooms = new(DefaultRooms);
    private static readonly List<ClientSession> AllClients = new();
    private static readonly object AllClientsLock = new();
    private static MongoUserStore? UserStore;
    private static MongoMessageStore? MessageStore;
    private static MongoDirectMessageStore? DirectMessageStore;
    private static MongoServerStore? ServerStore;
    private static MongoVoiceSessionStore? VoiceSessionStore;
    private static MongoFileStore? FileStore;
    private static AiService Ai = new(null);
    private static EmailService Email = null!;
    private static OtpJwtService OtpTokens = null!;
    private static readonly SlidingWindowRateLimiter AuthRateLimiter = new();
    private static string LiveKitUrl = "";
    private static string LiveKitApiKey = "";
    private static string LiveKitApiSecret = "";

    public static async Task Main(string[] args)
    {
        var config = EnvironmentConfig.Load();
        string GetEnv(string key) =>
            !string.IsNullOrWhiteSpace(config.GetValueOrDefault(key, ""))
                ? config.GetValueOrDefault(key, "")
                : Environment.GetEnvironmentVariable(key) ?? "";

        LiveKitUrl = GetEnv("LIVEKIT_URL").Trim();
        LiveKitApiKey = GetEnv("LIVEKIT_API_KEY").Trim();
        LiveKitApiSecret = GetEnv("LIVEKIT_API_SECRET").Trim();
        Email = new EmailService(config);
        Console.WriteLine($"[ChatServer] Email OTP configured: {Email.IsConfigured}");
        OtpTokens = new OtpJwtService(GetEnv("JWT_SECRET"));

        var mongoUri = GetEnv("MONGODB_URI");
        if (!string.IsNullOrWhiteSpace(mongoUri))
        {
            try
            {
                var mongo = new MongoContext(mongoUri);
                UserStore = new MongoUserStore(mongo.Database);
                await UserStore.InitializeAsync();
                MessageStore = new MongoMessageStore(mongo.Database);
                await MessageStore.InitializeAsync();
                DirectMessageStore = new MongoDirectMessageStore(mongo.Database);
                await DirectMessageStore.InitializeAsync();
                FileStore = new MongoFileStore(mongo.Database);
                ServerStore = new MongoServerStore(mongo.Database);
                await ServerStore.InitializeAsync();
                VoiceSessionStore = new MongoVoiceSessionStore(mongo.Database);
                await VoiceSessionStore.InitializeAsync();
                Console.WriteLine("[ChatServer] Đã kết nối MongoDB");
                Console.WriteLine($"[ChatServer] Tên cơ sở dữ liệu: {mongo.Database.DatabaseNamespace.DatabaseName}");
            }
            catch (Exception ex)
            {
                Console.Error.WriteLine($"[ChatServer] Khong ket noi duoc MongoDB: {ex}");
            }
        }
        else
        {
            Console.Error.WriteLine("[ChatServer] Thieu MONGODB_URI trong .env.");
        }

        Ai = new AiService(GetEnv("AI_SERVICE_URL"));
        var configuredPort = Environment.GetEnvironmentVariable("PORT");
        if (string.IsNullOrWhiteSpace(configuredPort))
            configuredPort = config.GetValueOrDefault("PORT", "");
        var port = int.TryParse(configuredPort, out var envPort) ? envPort : 5050;
        if (args.Length > 0 && int.TryParse(args[0], out var argumentPort)) port = argumentPort;

        var builder = WebApplication.CreateBuilder(Array.Empty<string>());
        builder.WebHost.UseUrls($"http://0.0.0.0:{port}");
        var app = builder.Build();
        app.UseWebSockets(new WebSocketOptions { KeepAliveInterval = TimeSpan.FromSeconds(30) });
        app.MapMethods("/", new[] { HttpMethods.Get, HttpMethods.Head }, () => Results.Ok(new { status = "ok" }));
        app.MapMethods("/health", new[] { HttpMethods.Get, HttpMethods.Head }, () => Results.Ok(new { status = "ok" }));
        app.Map("/ws", async context =>
        {
            if (!context.WebSockets.IsWebSocketRequest)
            {
                context.Response.StatusCode = StatusCodes.Status400BadRequest;
                await context.Response.WriteAsync("WebSocket connection required.");
                return;
            }

            var socket = await context.WebSockets.AcceptWebSocketAsync();
            var forwardedFor = context.Request.Headers["X-Forwarded-For"].ToString();
            var forwardedIp = forwardedFor.Split(',', StringSplitOptions.RemoveEmptyEntries)
                .Select(value => value.Trim()).FirstOrDefault();
            var remoteIp = forwardedIp ?? context.Connection.RemoteIpAddress?.ToString() ?? "unknown";
            var session = new ClientSession(socket, remoteIp);
            lock (AllClientsLock) AllClients.Add(session);
            Console.WriteLine($"[WS] Accepted connection from {remoteIp}");
            await HandleClientAsync(session);
        });

        Console.WriteLine($"[ChatServer] WebSocket server listening on port {port}...");
        await app.RunAsync();
    }

    private static async Task HandleClientAsync(ClientSession session)
    {
        var remoteEp = session.IpAddress;
        Console.WriteLine($"[+] Ket noi moi tu {remoteEp}");

        try
        {
            while (true)
            {
                Envelope? envelope;
                try
                {
                    envelope = await WebSocketEnvelopeCodec.ReceiveAsync(session.Socket);
                }
                catch (InvalidDataException ex)
                {
                    Console.WriteLine($"[!] Frame khong hop le tu {remoteEp}: {ex.Message}");
                    break;
                }
                if (envelope == null) break; // client dong ket noi

                await DispatchAsync(session, envelope);
            }
        }
        catch (WebSocketException ex)
        {
            Console.WriteLine($"[WS] Connection closed for {remoteEp}: {ex.Message}");
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine($"[Client] Unhandled error for {remoteEp}: {ex}");
        }
        finally
        {
            await HandleDisconnectAsync(session);
        }
    }

    private static async Task DispatchAsync(ClientSession session, Envelope envelope)
    {
        switch (envelope.Type)
        {
            case "join":
                await HandleJoinAsync(session, envelope.As<JoinRequest>());
                break;
            case "register":
                await HandleRegisterRequestAsync(session, envelope.As<RegisterRequest>());
                break;

            case "register-verify":
                await HandleRegisterVerifyAsync(session, envelope.As<VerifyRegistrationRequest>());
                break;

            case "login":
                await HandleLoginAsync(session, envelope.As<AuthRequest>());
                break;

            case "2fa-verify":
                await HandleTwoFactorVerifyAsync(session, envelope.As<TwoFactorVerifyRequest>());
                break;

            case "request-password-reset-otp":
                await HandleRequestPasswordResetOtpAsync(session, envelope.As<RequestPasswordResetOtp>());
                break;

            case "verify-password-reset":
                await HandleVerifyPasswordResetAsync(session, envelope.As<VerifyPasswordResetRequest>());
                break;

            case "profile":
                await HandleProfileAsync(session, envelope.As<ProfileRequest>());
                break;

            case "request-password-change-otp":
                await HandleRequestPasswordChangeOtpAsync(session);
                break;

            case "verify-password-change":
                await HandleVerifyPasswordChangeAsync(session, envelope.As<VerifyPasswordChangeRequest>());
                break;

            case "update-avatar":
                await HandleUpdateAvatarAsync(session, envelope.As<UpdateAvatarRequest>());
                break;

            case "update-profile":
                await HandleUpdateProfileAsync(session, envelope.As<UpdateProfileRequest>());
                break;

            case "update-privacy":
                await HandleUpdatePrivacyAsync(session, envelope.As<UpdatePrivacyRequest>());
                break;

            case "block-user":
                await HandleBlockUserAsync(session, envelope.As<BlockUserRequest>());
                break;

            case "update-appearance":
                await HandleUpdateAppearanceAsync(session, envelope.As<UpdateAppearanceRequest>());
                break;

            case "create-room":
                await HandleCreateRoomAsync(session, envelope.As<CreateRoomRequest>());
                break;

            case "request-server-creation-otp":
                await HandleRequestServerCreationOtpAsync(session, envelope.As<CreateServerRequest>());
                break;

            case "verify-server-creation":
                await HandleVerifyServerCreationAsync(session, envelope.As<VerifyServerCreationRequest>());
                break;

            case "get-my-servers":
                await HandleGetMyServersAsync(session);
                break;

            case "get-server-channels":
                await HandleGetServerChannelsAsync(session, envelope.As<GetServerChannelsRequest>());
                break;

            case "create-server-channel":
                await HandleCreateServerChannelAsync(session, envelope.As<CreateServerChannelRequest>());
                break;

            case "create-server-invite":
                await HandleCreateServerInviteAsync(session, envelope.As<CreateServerInviteRequest>());
                break;

            case "join-server-invite":
                await HandleJoinServerInviteAsync(session, envelope.As<JoinServerInviteRequest>());
                break;

            case "voice-join":
                await HandleVoiceJoinAsync(session, envelope.As<JoinVoiceChannelRequest>());
                break;

            case "voice-leave":
                await LeaveVoiceChannelAsync(session);
                break;

            case "voice-device-state":
                await HandleVoiceDeviceStateAsync(session, envelope.As<UpdateVoiceDeviceStateRequest>());
                break;

            case "get-voice-participants":
                await HandleGetVoiceParticipantsAsync(session, envelope.As<GetVoiceParticipantsRequest>());
                break;

            case "webrtc-signal":
                await HandleWebRtcSignalAsync(session, envelope.As<WebRtcSignalRequest>());
                break;

            case "chat":
                await HandleChatAsync(session, envelope.As<ChatRequest>());
                break;

            case "file-upload-start":
                await HandleFileUploadStartAsync(session, envelope.As<FileUploadStartRequest>());
                break;

            case "file-upload-chunk":
                await HandleFileUploadChunkAsync(session, envelope.As<FileUploadChunkRequest>());
                break;

            case "file-upload-complete":
                await HandleFileUploadCompleteAsync(session, envelope.As<FileUploadCompleteRequest>());
                break;

            case "file-download":
                await HandleFileDownloadAsync(session, envelope.As<FileDownloadRequest>());
                break;

            case "typing":
                await HandleTypingAsync(session, envelope.As<TypingRequest>());
                break;

            case "direct-message":
                await HandleDirectMessageAsync(session, envelope.As<DirectMessageRequest>());
                break;

            case "get-unread-direct-messages":
                await HandleGetUnreadDirectMessagesAsync(session);
                break;

            // Session management
            case "get-sessions":
                await HandleGetSessionsAsync(session);
                break;

            case "revoke-session":
                await HandleRevokeSessionAsync(session, envelope.As<RevokeSessionRequest>());
                break;

            case "revoke-all-sessions":
                await HandleRevokeAllSessionsAsync(session);
                break;

            // Two-factor authentication
            case "2fa-setup":
                await HandleTwoFactorSetupAsync(session);
                break;

            case "2fa-enable":
                await HandleTwoFactorEnableAsync(session, envelope.As<TwoFactorEnableRequest>());
                break;

            case "2fa-disable":
                await HandleTwoFactorDisableAsync(session, envelope.As<TwoFactorDisableRequest>());
                break;

            case "2fa-status":
                await HandleTwoFactorStatusAsync(session);
                break;

            // Login history
            case "get-login-history":
                await HandleGetLoginHistoryAsync(session);
                break;
            // Reactions - THÊM 2 DÒNG NÀY
            case "add-reaction":
                await HandleAddReactionAsync(session, envelope.As<AddReactionRequest>());
                break;

            case "remove-reaction":
                await HandleRemoveReactionAsync(session, envelope.As<RemoveReactionRequest>());
                break;

            case "edit-message":
                await HandleEditMessageAsync(session, envelope.As<EditMessageRequest>());
                break;

            case "delete-message":
                await HandleDeleteMessageAsync(session, envelope.As<DeleteMessageRequest>());
                break;
            case "friend-request":
                await HandleFriendRequestAsync(session, envelope.As<SendFriendRequest>());
                break;
            case "friend-respond":
                await HandleFriendRespondAsync(session, envelope.As<RespondFriendRequest>());
                break;
            case "remove-friend":
                await HandleRemoveFriendAsync(session, envelope.As<RemoveFriendRequest>());
                break;
            case "get-friends":
                await HandleGetFriendsAsync(session);
                break;
            case "get-pending-requests":
                await HandleGetPendingRequestsAsync(session);
                break;

            default:
                await session.SendAsync("error", new ErrorResponse("Loai message khong ho tro."));
                break;
        }
    }

    private static async Task HandleProfileAsync(ClientSession session, ProfileRequest? req)
    {
        if (UserStore == null)
        {
            await session.SendAsync("error", new ErrorResponse("MongoDB chua san sang."));
            return;
        }

        var username = (req?.Username ?? session.Username).Trim();
        var profile = await UserStore.GetProfileAsync(username, IsUserOnline(username));
        await session.SendAsync("profile", new ProfileResponse(profile));
    }

    private static async Task HandleRequestPasswordResetOtpAsync(ClientSession session, RequestPasswordResetOtp? req)
    {
        if (UserStore == null)
        {
            await session.SendAsync("error", new ErrorResponse("MongoDB chua san sang."));
            return;
        }
        if (req == null || !req.Email.Contains('@'))
        {
            await session.SendAsync("error", new ErrorResponse("Email khong hop le."));
            return;
        }

        var email = req.Email.Trim();
        if (!await TryConsumeRateLimitAsync(session, "password-reset-otp", email, 3, TimeSpan.FromMinutes(15))) return;
        var username = await UserStore.GetUsernameByEmailAsync(email);
        if (username == null)
        {
            await session.SendAsync("error", new ErrorResponse("Email chua duoc dang ky trong he thong."));
            return;
        }
        if (!Email.IsConfigured)
        {
            await session.SendAsync("error", new ErrorResponse("Chua cau hinh EMAIL_SCRIPT_URL/EMAIL_SCRIPT_SECRET hoac Resend."));
            return;
        }

        var otp = RandomNumberGenerator.GetInt32(100000, 1000000).ToString();
        try
        {
            await Email.SendOtpAsync(email, otp);
            session.PendingPasswordResetToken = OtpTokens.Create(username, email, otp, DateTime.UtcNow.AddMinutes(10));
            await session.SendAsync("password-reset-otp-sent",
                new PasswordResetOtpSentResponse("Ma OTP da duoc gui toi email da dang ky."));
        }
        catch (HttpRequestException ex)
        {
            Console.Error.WriteLine($"[PasswordReset] Gui OTP that bai cho '{email}': {ex}");
            await session.SendAsync("error", new ErrorResponse("Dich vu email khong gui duoc OTP. Hay kiem tra cau hinh email gateway."));
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine($"[PasswordReset] Gui OTP that bai cho '{email}': {ex}");
            await session.SendAsync("error", new ErrorResponse("Khong the gui OTP luc nay. Hay thu lai sau."));
        }
    }

    private static async Task HandleVerifyPasswordResetAsync(ClientSession session, VerifyPasswordResetRequest? req)
    {
        if (UserStore == null || req == null) return;
        if (!await TryConsumeRateLimitAsync(session, "password-reset-otp-verify", session.IpAddress, 5, TimeSpan.FromMinutes(10))) return;
        var token = session.PendingPasswordResetToken;
        session.PendingPasswordResetToken = null;
        if (token == null || !OtpTokens.TryValidate(token, req.Otp.Trim(), out var claims) || claims == null)
        {
            await session.SendAsync("error", new ErrorResponse("Ma OTP khong dung hoac da het han."));
            return;
        }

        var result = await UserStore.ChangePasswordAfterOtpAsync(claims.Username, req.NewPassword);
        await session.SendAsync(result.Success ? "password-reset" : "error",
            result.Success ? new SettingsUpdatedResponse(result.Error) : new ErrorResponse(result.Error));
    }

    private static async Task HandleRequestPasswordChangeOtpAsync(ClientSession session)
    {
        if (!session.IsAuthenticated || UserStore == null) return;
        var email = await UserStore.GetEmailAsync(session.Username);
        if (string.IsNullOrWhiteSpace(email))
        {
            await session.SendAsync("error", new ErrorResponse("Tai khoan chua co email de xac nhan."));
            return;
        }
        if (!await TryConsumeRateLimitAsync(session, "password-change-otp", email, 3, TimeSpan.FromMinutes(15))) return;
        if (!Email.IsConfigured)
        {
            await session.SendAsync("error", new ErrorResponse("Chua cau hinh EMAIL_SCRIPT_URL/EMAIL_SCRIPT_SECRET hoac Resend."));
            return;
        }

        var otp = RandomNumberGenerator.GetInt32(100000, 1000000).ToString();
        try
        {
            await Email.SendOtpAsync(email, otp);
            session.PendingPasswordChangeToken = OtpTokens.Create(session.Username, email, otp, DateTime.UtcNow.AddMinutes(10));
            await session.SendAsync("password-otp-sent", new PasswordChangeOtpSentResponse("Ma OTP da duoc gui toi email da dang ky."));
        }
        catch (Exception ex)
        {
            await session.SendAsync("error", new ErrorResponse($"Khong gui duoc OTP: {ex.Message}"));
        }
    }

    private static async Task HandleVerifyPasswordChangeAsync(ClientSession session, VerifyPasswordChangeRequest? req)
    {
        if (!session.IsAuthenticated || UserStore == null || req == null) return;
        if (!await TryConsumeRateLimitAsync(session, "password-change-otp-verify", session.Username, 5, TimeSpan.FromMinutes(10))) return;
        var token = session.PendingPasswordChangeToken;
        session.PendingPasswordChangeToken = null;
        if (token == null || !OtpTokens.TryValidate(token, req.Otp.Trim(), out var claims) ||
            claims == null || !string.Equals(claims.Username, session.Username, StringComparison.OrdinalIgnoreCase))
        {
            await session.SendAsync("error", new ErrorResponse("Ma OTP khong dung hoac da het han."));
            return;
        }

        var result = await UserStore.ChangePasswordAfterOtpAsync(session.Username, req.NewPassword);
        await session.SendAsync(result.Success ? "settings-updated" : "error",
            result.Success ? new SettingsUpdatedResponse(result.Error) : new ErrorResponse(result.Error));
    }

    private static async Task HandleUpdateAvatarAsync(ClientSession session, UpdateAvatarRequest? req)
    {
        if (!session.IsAuthenticated || UserStore == null || req == null) return;
        if (req.AvatarBase64?.Length > 1_400_000)
        {
            await session.SendAsync("error", new ErrorResponse("Avatar qua lon. Hay chon anh nho hon 1 MB."));
            return;
        }

        var updated = await UserStore.UpdateAvatarAsync(session.Username, req.AvatarBase64);
        await session.SendAsync(updated ? "settings-updated" : "error",
            updated ? new SettingsUpdatedResponse("Cap nhat avatar thanh cong.") : new ErrorResponse("Khong cap nhat duoc avatar."));
    }

    private static async Task HandleUpdateProfileAsync(ClientSession session, UpdateProfileRequest? req)
    {
        if (!session.IsAuthenticated || UserStore == null || req == null) return;
        var result = await UserStore.UpdateProfileAsync(session.Username, req.DisplayName, req.Email, req.Bio, req.Status);
        await session.SendAsync(result.Success ? "settings-updated" : "error",
            result.Success ? new SettingsUpdatedResponse(result.Error) : new ErrorResponse(result.Error));
    }

    private static async Task HandleUpdatePrivacyAsync(ClientSession session, UpdatePrivacyRequest? req)
    {
        if (!session.IsAuthenticated || UserStore == null || req == null) return;
        var updated = await UserStore.UpdatePrivacyAsync(session.Username, req.AllowDirectMessages, req.ShowAvatar, req.ShowOnlineStatus);
        await session.SendAsync(updated ? "settings-updated" : "error",
            updated ? new SettingsUpdatedResponse("Đã cập nhật quyền riêng tư.") : new ErrorResponse("Không thể cập nhật quyền riêng tư."));
    }

    private static async Task HandleBlockUserAsync(ClientSession session, BlockUserRequest? req)
    {
        if (!session.IsAuthenticated || UserStore == null || req == null) return;
        var username = req.Username.Trim();
        if (username.Length == 0 || string.Equals(username, session.Username, StringComparison.OrdinalIgnoreCase))
        {
            await session.SendAsync("error", new ErrorResponse("Tên người dùng không hợp lệ."));
            return;
        }
        if (!await UserStore.ExistsAsync(username))
        {
            await session.SendAsync("error", new ErrorResponse("Người dùng không tồn tại."));
            return;
        }
        var updated = await UserStore.SetBlockedUserAsync(session.Username, username, req.Blocked);
        await session.SendAsync(updated ? "settings-updated" : "error",
            updated ? new SettingsUpdatedResponse(req.Blocked ? "Đã chặn người dùng." : "Đã bỏ chặn người dùng.") : new ErrorResponse("Không thể cập nhật danh sách chặn."));
    }

    private static async Task HandleUpdateAppearanceAsync(ClientSession session, UpdateAppearanceRequest? req)
    {
        if (!session.IsAuthenticated || UserStore == null || req == null) return;
        var updated = await UserStore.UpdateAppearanceAsync(session.Username, req);
        await session.SendAsync(updated ? "settings-updated" : "error",
            updated ? new SettingsUpdatedResponse("Đã lưu giao diện cá nhân.") : new ErrorResponse("Không thể lưu giao diện cá nhân."));
    }

    private static async Task HandleRequestServerCreationOtpAsync(ClientSession session, CreateServerRequest? req)
    {
        if (!session.IsAuthenticated || UserStore == null || ServerStore == null || req == null) return;
        if (string.IsNullOrWhiteSpace(req.Name) || req.Name.Trim().Length is < 2 or > 80)
        {
            await session.SendAsync("error", new ErrorResponse("Tên server phải có từ 2 đến 80 ký tự."));
            return;
        }

        var email = await UserStore.GetEmailAsync(session.Username);
        if (string.IsNullOrWhiteSpace(email) || !Email.IsConfigured)
        {
            await session.SendAsync("error", new ErrorResponse("Không thể xác thực email để tạo server."));
            return;
        }
        if (!await TryConsumeRateLimitAsync(session, "server-creation-otp", email, 3, TimeSpan.FromMinutes(15))) return;

        var otp = RandomNumberGenerator.GetInt32(100000, 1000000).ToString();
        try
        {
            await Email.SendOtpAsync(email, otp);
            session.PendingServerCreation = new PendingServerCreation(req.Name.Trim(), req.Description?.Trim(),
                OtpTokens.Create(session.Username, email, otp, DateTime.UtcNow.AddMinutes(10)));
            await session.SendAsync("server-creation-otp-sent", new ServerCreationOtpSentResponse("Đã gửi mã xác nhận đến email đăng ký của bạn."));
        }
        catch
        {
            await session.SendAsync("error", new ErrorResponse("Không thể gửi mã xác nhận lúc này."));
        }
    }

    private static async Task HandleVerifyServerCreationAsync(ClientSession session, VerifyServerCreationRequest? req)
    {
        if (!session.IsAuthenticated || ServerStore == null || req == null) return;
        if (!await TryConsumeRateLimitAsync(session, "server-creation-otp-verify", session.Username, 5, TimeSpan.FromMinutes(10))) return;

        var pending = session.PendingServerCreation;
        session.PendingServerCreation = null; // OTP may only be used once.
        if (pending == null || !OtpTokens.TryValidate(pending.VerificationToken, req.Otp.Trim(), out var claims) ||
            claims == null || !string.Equals(claims.Username, session.Username, StringComparison.OrdinalIgnoreCase))
        {
            await session.SendAsync("error", new ErrorResponse("Mã xác nhận không đúng hoặc đã hết hạn."));
            return;
        }

        var result = await ServerStore.CreateAsync(session.Username, pending.Name, pending.Description);
        if (!result.Success || result.Server == null || result.Channels == null)
        {
            await session.SendAsync("error", new ErrorResponse(result.Error));
            return;
        }
        await session.SendAsync("server-created", new ServerCreatedResponse(result.Server, result.Channels));
    }

    private static async Task HandleGetMyServersAsync(ClientSession session)
    {
        if (!session.IsAuthenticated || ServerStore == null) return;
        await session.SendAsync("my-servers", new MyServersResponse(await ServerStore.GetForUserAsync(session.Username)));
    }

    private static async Task HandleGetServerChannelsAsync(ClientSession session, GetServerChannelsRequest? req)
    {
        if (!session.IsAuthenticated || ServerStore == null || string.IsNullOrWhiteSpace(req?.ServerId)) return;
        var channels = await ServerStore.GetChannelsAsync(session.Username, req.ServerId);
        if (channels == null)
        {
            await session.SendAsync("error", new ErrorResponse("Bạn không phải thành viên của server này."));
            return;
        }
        await session.SendAsync("server-channels", new ServerChannelsResponse(req.ServerId, channels));
    }

    private static async Task HandleCreateServerChannelAsync(ClientSession session, CreateServerChannelRequest? req)
    {
        if (!session.IsAuthenticated || ServerStore == null || req == null) return;
        var result = await ServerStore.CreateChannelAsync(session.Username, req.ServerId, req.Name, req.Type);
        if (!result.Success)
        {
            await session.SendAsync("error", new ErrorResponse(result.Error));
            return;
        }
        var channels = await ServerStore.GetChannelsAsync(session.Username, req.ServerId) ?? [];
        await session.SendAsync("server-channels", new ServerChannelsResponse(req.ServerId, channels));
    }

    private static async Task HandleCreateServerInviteAsync(ClientSession session, CreateServerInviteRequest? req)
    {
        if (!session.IsAuthenticated || ServerStore == null || req == null) return;
        var result = await ServerStore.CreateInviteAsync(session.Username, req.ServerId, req.MaxUses, req.ExpiresInMinutes);
        await session.SendAsync(result.Success ? "server-invite" : "error",
            result.Success ? result.Invite : new ErrorResponse(result.Error));
    }

    private static async Task HandleJoinServerInviteAsync(ClientSession session, JoinServerInviteRequest? req)
    {
        if (!session.IsAuthenticated || ServerStore == null || req == null) return;
        var result = await ServerStore.AcceptInviteAsync(session.Username, req.Code);
        await session.SendAsync(result.Success ? "server-invite-accepted" : "error",
            result.Success && result.Server != null && result.Channels != null
                ? new ServerInviteAcceptedResponse(result.Server, result.Channels)
                : new ErrorResponse(result.Error));
    }

    private static async Task HandleVoiceJoinAsync(ClientSession session, JoinVoiceChannelRequest? req)
    {
        if (!session.IsAuthenticated || ServerStore == null || string.IsNullOrWhiteSpace(req?.ChannelId)) return;
        if (!await ServerStore.CanJoinVoiceChannelAsync(session.Username, req.ChannelId))
        {
            await session.SendAsync("error", new ErrorResponse("Bạn không có quyền vào kênh đàm thoại này."));
            return;
        }
        if (session.VoiceChannelId == req.ChannelId)
        {
            await SendLiveKitCredentialsAsync(session, req.ChannelId);
            await SendVoiceStateAsync(session, req.ChannelId);
            return;
        }

        await LeaveVoiceChannelAsync(session);
        session.VoiceChannelId = req.ChannelId;
        session.VoiceJoinedAt = DateTime.UtcNow;
        session.VoiceMicrophoneEnabled = false;
        session.VoiceCameraEnabled = false;
        if (VoiceSessionStore != null)
            await VoiceSessionStore.StartAsync(session.SessionId, req.ChannelId, session.Username, session.VoiceJoinedAt);
        await SendLiveKitCredentialsAsync(session, req.ChannelId);
        await BroadcastVoiceStateAsync(req.ChannelId);
    }

    private static async Task SendLiveKitCredentialsAsync(ClientSession session, string channelId)
    {
        if (string.IsNullOrWhiteSpace(LiveKitUrl) || string.IsNullOrWhiteSpace(LiveKitApiKey) || string.IsNullOrWhiteSpace(LiveKitApiSecret))
        {
            await session.SendAsync("error", new ErrorResponse("LiveKit chưa được cấu hình trên server."));
            return;
        }

        var now = DateTimeOffset.UtcNow.ToUnixTimeSeconds();
        var header = Base64Url(JsonSerializer.SerializeToUtf8Bytes(new { alg = "HS256", typ = "JWT" }));
        var payload = Base64Url(JsonSerializer.SerializeToUtf8Bytes(new
        {
            iss = LiveKitApiKey,
            sub = session.Username,
            nbf = now - 5,
            exp = now + 6 * 60 * 60,
            video = new
            {
                roomJoin = true,
                room = channelId,
                canPublish = true,
                canSubscribe = true,
                canPublishData = true
            }
        }));
        var unsignedToken = $"{header}.{payload}";
        using var hmac = new HMACSHA256(Encoding.UTF8.GetBytes(LiveKitApiSecret));
        var signature = Base64Url(hmac.ComputeHash(Encoding.UTF8.GetBytes(unsignedToken)));
        await session.SendAsync("livekit-credentials", new LiveKitCredentials(LiveKitUrl, $"{unsignedToken}.{signature}"));
    }

    private static string Base64Url(byte[] value) => Convert.ToBase64String(value)
        .TrimEnd('=').Replace('+', '-').Replace('/', '_');

    private static async Task HandleVoiceDeviceStateAsync(ClientSession session, UpdateVoiceDeviceStateRequest? req)
    {
        if (!session.IsAuthenticated || session.VoiceChannelId == null || req == null) return;
        session.VoiceMicrophoneEnabled = req.MicrophoneEnabled;
        session.VoiceCameraEnabled = req.CameraEnabled;
        if (VoiceSessionStore != null)
            await VoiceSessionStore.UpdateDeviceStateAsync(session.SessionId, req.MicrophoneEnabled, req.CameraEnabled);
        await BroadcastVoiceStateAsync(session.VoiceChannelId);
    }

    private static async Task HandleGetVoiceParticipantsAsync(ClientSession session, GetVoiceParticipantsRequest? req)
    {
        if (!session.IsAuthenticated || string.IsNullOrWhiteSpace(req?.ChannelId)) return;
        await SendVoiceStateAsync(session, req.ChannelId);
    }

    private static async Task LeaveVoiceChannelAsync(ClientSession session)
    {
        var channelId = session.VoiceChannelId;
        if (channelId == null) return;
        session.VoiceChannelId = null;
        session.VoiceMicrophoneEnabled = false;
        session.VoiceCameraEnabled = false;
        if (VoiceSessionStore != null) await VoiceSessionStore.EndAsync(session.SessionId, DateTime.UtcNow);
        await BroadcastVoiceStateAsync(channelId);
    }

    private static List<ClientSession> GetVoiceMembers(string channelId)
    {
        lock (AllClientsLock)
            return AllClients.Where(x => x.IsAuthenticated && x.VoiceChannelId == channelId).ToList();
    }

    private static VoiceChannelState BuildVoiceState(string channelId) => new(channelId,
        GetVoiceMembers(channelId).OrderBy(x => x.Username).Select(x => new VoiceParticipant(
            x.Username, x.VoiceMicrophoneEnabled, x.VoiceCameraEnabled,
            new DateTimeOffset(DateTime.SpecifyKind(x.VoiceJoinedAt, DateTimeKind.Utc)))).ToList());

    private static Task BroadcastVoiceStateAsync(string channelId)
    {
        var state = BuildVoiceState(channelId);
        return Task.WhenAll(GetVoiceMembers(channelId).Select(x => x.SendAsync("voice-channel-state", state)));
    }
        // ============ FRIEND SYSTEM HANDLERS ============

    private static async Task HandleFriendRequestAsync(ClientSession session, SendFriendRequest? req)
    {
        if (!session.IsAuthenticated || UserStore == null || req == null) return;

        var targetUsername = req.TargetUsername.Trim();
        if (string.IsNullOrWhiteSpace(targetUsername))
        {
            await session.SendAsync("error", new ErrorResponse("Tên người dùng không hợp lệ."));
            return;
        }

        if (string.Equals(targetUsername, session.Username, StringComparison.OrdinalIgnoreCase))
        {
            await session.SendAsync("error", new ErrorResponse("Không thể kết bạn với chính mình."));
            return;
        }

        if (!await TryConsumeRateLimitAsync(session, "friend-request", session.Username, 10, TimeSpan.FromMinutes(5))) return;

        var result = await UserStore.SendFriendRequestAsync(session.Username, targetUsername);
        await session.SendAsync(result.Success ? "friend-request-sent" : "error",
            result.Success ? new FriendRequestSentResponse(targetUsername) : new ErrorResponse(result.Error));

        // Push real-time notification to recipient if online
        if (result.Success)
        {
            ClientSession? recipientSession;
            lock (AllClientsLock)
            {
                recipientSession = AllClients.FirstOrDefault(c =>
                    c.IsAuthenticated && string.Equals(c.Username, targetUsername, StringComparison.OrdinalIgnoreCase));
            }

            if (recipientSession != null)
            {
                var senderProfile = await UserStore.GetProfileAsync(session.Username, IsUserOnline(session.Username));
                await recipientSession.SendAsync("friend-request-received", new FriendRequestReceived(session.Username));
            }
        }
    }

    private static async Task HandleFriendRespondAsync(ClientSession session, RespondFriendRequest? req)
    {
        if (!session.IsAuthenticated || UserStore == null || req == null) return;

        var fromUsername = req.FromUsername.Trim();
        if (string.IsNullOrWhiteSpace(fromUsername))
        {
            await session.SendAsync("error", new ErrorResponse("Tên người dùng không hợp lệ."));
            return;
        }

        var result = await UserStore.RespondFriendRequestAsync(session.Username, fromUsername, req.Accept);
        await session.SendAsync("friend-responded", new FriendRequestResponse(result.Success, result.Error));

        // Notify the other user
        ClientSession? otherSession;
        lock (AllClientsLock)
        {
            otherSession = AllClients.FirstOrDefault(c =>
                c.IsAuthenticated && string.Equals(c.Username, fromUsername, StringComparison.OrdinalIgnoreCase));
        }

        if (otherSession != null)
        {
            await otherSession.SendAsync("friend-responded", new FriendRequestResponse(result.Success, 
                req.Accept ? $"{session.Username} đã chấp nhận lời mời kết bạn." : $"{session.Username} đã từ chối lời mời kết bạn."));
        }
    }

    private static async Task HandleRemoveFriendAsync(ClientSession session, RemoveFriendRequest? req)
    {
        if (!session.IsAuthenticated || UserStore == null || req == null) return;

        var friendUsername = req.Username.Trim();
        if (string.IsNullOrWhiteSpace(friendUsername))
        {
            await session.SendAsync("error", new ErrorResponse("Tên người dùng không hợp lệ."));
            return;
        }

        var result = await UserStore.RemoveFriendAsync(session.Username, friendUsername);
        await session.SendAsync("friend-removed", new FriendRemovedResponse(result.Success, result.Error));

        // Notify the other user
        ClientSession? otherSession;
        lock (AllClientsLock)
        {
            otherSession = AllClients.FirstOrDefault(c =>
                c.IsAuthenticated && string.Equals(c.Username, friendUsername, StringComparison.OrdinalIgnoreCase));
        }

        if (otherSession != null && result.Success)
        {
            await otherSession.SendAsync("friend-removed", new FriendRemovedResponse(true, $"{session.Username} đã xóa bạn khỏi danh sách bạn bè."));
        }
    }

    private static async Task HandleGetFriendsAsync(ClientSession session)
    {
        if (!session.IsAuthenticated || UserStore == null) return;

        var friends = await UserStore.GetFriendsAsync(session.Username);
        
        // Update online status from server memory
        var onlineUsernames = new HashSet<string>();
        lock (AllClientsLock)
        {
            foreach (var client in AllClients.Where(c => c.IsAuthenticated))
            {
                onlineUsernames.Add(client.Username);
            }
        }

        var friendsWithStatus = friends.Select(f => f with { IsOnline = onlineUsernames.Contains(f.Username) }).ToList();
        await session.SendAsync("friends-list", new GetFriendsResponse(friendsWithStatus));
    }

    private static async Task HandleGetPendingRequestsAsync(ClientSession session)
    {
        if (!session.IsAuthenticated || UserStore == null) return;

        var sent = await UserStore.GetPendingRequestsAsync(session.Username, true);
        var received = await UserStore.GetPendingRequestsAsync(session.Username, false);
        
        await session.SendAsync("pending-requests", new GetPendingRequestsResponse(sent.Concat(received).ToList()));
    }
    private static Task SendVoiceStateAsync(ClientSession session, string channelId) =>
        session.SendAsync("voice-channel-state", BuildVoiceState(channelId));

    private static async Task HandleWebRtcSignalAsync(ClientSession session, WebRtcSignalRequest? req)
    {
        if (!session.IsAuthenticated || req == null || session.VoiceChannelId != req.ChannelId ||
            string.IsNullOrWhiteSpace(req.To) || (req.Kind != "renegotiate" && string.IsNullOrWhiteSpace(req.Payload)) ||
            req.Kind is not ("offer" or "answer" or "ice" or "renegotiate")) return;

        List<ClientSession> recipients;
        lock (AllClientsLock)
        {
            recipients = AllClients.Where(client => client.IsAuthenticated &&
                client.VoiceChannelId == req.ChannelId &&
                string.Equals(client.Username, req.To, StringComparison.OrdinalIgnoreCase)).ToList();
        }
        if (recipients.Count == 0)
        {
            await session.SendAsync("error", new ErrorResponse("Người nhận không còn ở kênh đàm thoại."));
            return;
        }
        var signal = new WebRtcSignal(req.ChannelId, session.Username, req.Kind, req.Payload);
        await Task.WhenAll(recipients.Select(client => client.SendAsync("webrtc-signal", signal)));
    }

    private static bool IsUserOnline(string username)
    {
        lock (AllClientsLock)
            return AllClients.Any(client => client.IsAuthenticated &&
                string.Equals(client.Username, username, StringComparison.OrdinalIgnoreCase));
    }
    
    private static async Task HandleJoinAsync(ClientSession session, JoinRequest? req)
    {
        if (!session.IsAuthenticated)
        {
            await session.SendAsync("error", new ErrorResponse("Ban can dang nhap truoc."));
            return;
        }

        var room = (req?.Room ?? "").Trim();
        if (!ChatLimits.IsValidRoomName(room))
        {
            await session.SendAsync("error", new ErrorResponse($"Tên phòng phải có từ 1 đến {ChatLimits.MaxRoomNameLength} ký tự và không chứa ký tự điều khiển."));
            return;
        }

        var previousRoom = session.CurrentRoom;
        var username = session.Username;

        Rooms.CreateRoomIfMissing(room);
        Rooms.Join(session, room);

        // Bao het phong cu la nguoi nay da roi
        if (previousRoom != null && previousRoom != room)
        {
            await Rooms.BroadcastAsync(previousRoom, "system",
                new SystemNotice(previousRoom, $"{username} da roi phong.", DateTimeOffset.Now));
            await Rooms.BroadcastAsync(previousRoom, "online-users",
                new OnlineUsersResponse(previousRoom, Rooms.GetUsernames(previousRoom)));
        }

        // Bao phong moi co nguoi vao
        await Rooms.BroadcastAsync(room, "system",
            new SystemNotice(room, $"{username} da vao phong.", DateTimeOffset.Now), except: session);

        await session.SendAsync("join", new JoinResponse(room, $"Da vao phong {room}."));

        if (MessageStore != null)
        {
            var history = await MessageStore.GetRecentAsync(room);
            UserProfile? viewerProfile = UserStore == null
                ? null
                : await UserStore.GetProfileAsync(session.Username, IsUserOnline(session.Username));
            if (viewerProfile?.ShowAvatar == false)
                history = history.Select(message => message with { AvatarBase64 = null }).ToList();
            await session.SendAsync("history", new HistoryResponse(room, history));
        }

        await Rooms.BroadcastAsync(room, "online-users",
            new OnlineUsersResponse(room, Rooms.GetUsernames(room)));

        await BroadcastRoomListToAllAsync();
    }

    private static async Task HandleCreateRoomAsync(ClientSession session, CreateRoomRequest? req)
    {
        if (!session.IsAuthenticated) return;

        var room = (req?.Room ?? "").Trim();
        if (!ChatLimits.IsValidRoomName(room))
        {
            await session.SendAsync("error", new ErrorResponse($"Tên phòng phải có từ 1 đến {ChatLimits.MaxRoomNameLength} ký tự và không chứa ký tự điều khiển."));
            return;
        }
        if (Rooms.RoomExists(room))
        {
            await session.SendAsync("error", new ErrorResponse($"Phong '{room}' da ton tai."));
            return;
        }
        Rooms.CreateRoomIfMissing(room);
        await BroadcastRoomListToAllAsync();
    }

    private static async Task HandleChatAsync(ClientSession session, ChatRequest? req)
    {
        if (!session.IsAuthenticated || session.CurrentRoom == null) return;

        var text = (req?.Text ?? "").Trim();
        if (!ChatLimits.IsValidChatText(text))
        {
            await session.SendAsync("error", new ErrorResponse($"Tin nhắn phải có từ 1 đến {ChatLimits.MaxChatTextLength} ký tự."));
            return;
        }

        if (text.StartsWith("/ai ", StringComparison.OrdinalIgnoreCase))
        {
            var prompt = text[4..].Trim();
            if (prompt.Length == 0) return;

            string reply;
            try
            {
                reply = await Ai.GenerateAsync(prompt, session.CurrentRoom, session.Username);
            }
            catch (Exception ex)
            {
                reply = $"Khong goi duoc AI service: {ex.Message}";
            }

            var aiMessage = new ChatMessage(Guid.NewGuid().ToString(), session.CurrentRoom, "AI", reply, DateTimeOffset.Now);
            if (MessageStore != null) await MessageStore.SaveAsync(aiMessage);
            await Rooms.BroadcastAsync(session.CurrentRoom, "chat", aiMessage);
            return;
        }

         var userAvatar = string.Empty;
         if (UserStore != null)
         {
             var profile = await UserStore.GetProfileAsync(session.Username, IsUserOnline(session.Username));
             userAvatar = profile?.AvatarBase64 ?? string.Empty;
         }

         var displayName = session.Username;
         if (UserStore != null)
         {
             var profile = await UserStore.GetProfileAsync(session.Username, IsUserOnline(session.Username));
             displayName = string.IsNullOrWhiteSpace(profile?.DisplayName) ? session.Username : profile.DisplayName;
         }

         var chatMessage = new ChatMessage(Guid.NewGuid().ToString(), session.CurrentRoom, session.Username, text, DateTimeOffset.Now,
             AvatarBase64: string.IsNullOrEmpty(userAvatar) ? null : userAvatar,
             DisplayName: displayName);
         if (MessageStore != null) await MessageStore.SaveAsync(chatMessage);
         await BroadcastChatAsync(session.CurrentRoom, chatMessage);
    }

    private static async Task HandleFileUploadStartAsync(ClientSession session, FileUploadStartRequest? req)
    {
        if (!session.IsAuthenticated || session.CurrentRoom == null || req == null) return;
        if (FileStore == null)
        {
            await session.SendAsync("error", new ErrorResponse("MongoDB chua san sang; khong the luu tep."));
            return;
        }

        var fileName = Path.GetFileName(req.FileName).Trim();
        if (string.IsNullOrWhiteSpace(req.UploadId) || string.IsNullOrWhiteSpace(fileName) ||
            req.FileSize <= 0 || req.FileSize > ChatLimits.MaxFileBytes)
        {
            await session.SendAsync("error", new ErrorResponse("Tep khong hop le hoac vuot qua gioi han 12 MB."));
            return;
        }

        if (session.Uploads.ContainsKey(req.UploadId))
        {
            await session.SendAsync("error", new ErrorResponse("Ma upload da ton tai."));
            return;
        }

        session.Uploads[req.UploadId] = new PendingUpload(fileName, req.FileSize, req.ThumbnailBase64);
        Console.WriteLine($"[File] Bat dau upload '{fileName}' tu {session.Username}, size={req.FileSize}.");
    }

    private static async Task HandleFileUploadChunkAsync(ClientSession session, FileUploadChunkRequest? req)
    {
        if (!session.IsAuthenticated || req == null || !session.Uploads.TryGetValue(req.UploadId, out var upload)) return;
        try
        {
            if (req.Index != upload.NextChunkIndex) throw new InvalidDataException("Chunk khong dung thu tu.");
            var bytes = Convert.FromBase64String(req.Base64Data);
            if (bytes.Length == 0 || bytes.Length > ChatLimits.FileChunkBytes || upload.Content.Length + bytes.Length > upload.ExpectedSize)
                throw new InvalidDataException("Chunk khong hop le.");

            await upload.Content.WriteAsync(bytes);
            upload.NextChunkIndex++;
        }
        catch (Exception ex) when (ex is FormatException or InvalidDataException)
        {
            session.Uploads.Remove(req.UploadId, out var removed);
            removed?.Dispose();
            await session.SendAsync("error", new ErrorResponse($"Du lieu tep khong hop le: {ex.Message}"));
        }
    }

    private static async Task HandleFileUploadCompleteAsync(ClientSession session, FileUploadCompleteRequest? req)
    {
        if (!session.IsAuthenticated || session.CurrentRoom == null || req == null || FileStore == null) return;
        if (!session.Uploads.Remove(req.UploadId, out var upload))
        {
            await session.SendAsync("error", new ErrorResponse("Khong tim thay phien upload."));
            return;
        }

        try
        {
            if (upload.Content.Length != upload.ExpectedSize)
            {
                await session.SendAsync("error", new ErrorResponse("Tep chua du du lieu."));
                return;
            }

            upload.Content.Position = 0;
            var storedFile = await FileStore.SaveAsync(upload.FileName, upload.Content);
            var message = new ChatMessage(
                Guid.NewGuid().ToString(), session.CurrentRoom, session.Username,
                $"Da gui tep: {upload.FileName}", DateTimeOffset.Now,
                upload.FileName, storedFile, upload.ExpectedSize,
                ThumbnailBase64: upload.ThumbnailBase64);
            if (MessageStore != null) await MessageStore.SaveAsync(message);
            await BroadcastChatAsync(session.CurrentRoom, message);
            Console.WriteLine($"[File] Da luu '{upload.FileName}' tu {session.Username}, storedFile={storedFile}.");
        }
        catch (Exception ex)
        {
            Console.WriteLine($"[File] Upload that bai tu {session.Username}: {ex.Message}");
            await session.SendAsync("error", new ErrorResponse("Khong the luu tep tren server."));
        }
        finally
        {
            upload.Dispose();
        }
    }

    private static async Task HandleFileDownloadAsync(ClientSession session, FileDownloadRequest? req)
    {
        if (!session.IsAuthenticated || req == null || FileStore == null) return;
        await using var stream = await FileStore.OpenAsync(req.StoredFile);
        if (stream == null)
        {
            await session.SendAsync("error", new ErrorResponse("Khong tim thay tep."));
            return;
        }

        var transferId = Guid.NewGuid().ToString();
        await session.SendAsync("file-download-start", new FileDownloadStart(transferId, stream.FileInfo.Filename, stream.FileInfo.Length));
        var buffer = new byte[ChatLimits.FileChunkBytes];
        var index = 0;
        int read;
        while ((read = await stream.ReadAsync(buffer.AsMemory(0, buffer.Length))) > 0)
        {
            await session.SendAsync("file-download-chunk", new FileDownloadChunk(transferId, index++, Convert.ToBase64String(buffer, 0, read)));
        }
        await session.SendAsync("file-download-complete", new FileDownloadComplete(transferId));
    }

    private static async Task BroadcastChatAsync(string room, ChatMessage message)
    {
        var members = Rooms.GetMembers(room);
        await Task.WhenAll(members.Select(async member =>
        {
            var profile = UserStore == null
                ? null
                : await UserStore.GetProfileAsync(member.Username, IsUserOnline(member.Username));
            var visibleMessage = profile?.ShowAvatar == false
                ? message with { AvatarBase64 = null }
                : message;
            await member.SendAsync("chat", visibleMessage);
        }));
    }

    private static async Task HandleTypingAsync(ClientSession session, TypingRequest? req)
    {
        if (!session.IsAuthenticated || session.CurrentRoom == null) return;

        await Rooms.BroadcastAsync(session.CurrentRoom, "typing",
            new TypingNotice(session.CurrentRoom, session.Username, req?.IsTyping ?? false), except: session);
    }

    private static async Task HandleDirectMessageAsync(ClientSession session, DirectMessageRequest? req)
    {
        if (!session.IsAuthenticated) return;

        var recipient = (req?.To ?? "").Trim();
        var text = (req?.Text ?? "").Trim();
        if (recipient.Length == 0 || !ChatLimits.IsValidChatText(text))
        {
            await session.SendAsync("error", new ErrorResponse($"Người nhận không được rỗng; tin nhắn tối đa {ChatLimits.MaxChatTextLength} ký tự."));
            return;
        }
        if (string.Equals(recipient, session.Username, StringComparison.OrdinalIgnoreCase))
        {
            await session.SendAsync("error", new ErrorResponse("Khong the gui tin nhan rieng cho chinh minh."));
            return;
        }
        if (UserStore == null || !await UserStore.ExistsAsync(recipient))
        {
            await session.SendAsync("error", new ErrorResponse("Nguoi nhan khong ton tai."));
            return;
        }

        var senderProfile = await UserStore.GetProfileAsync(session.Username, IsUserOnline(session.Username));
        var recipientProfile = await UserStore.GetProfileAsync(recipient, IsUserOnline(recipient));
        if (recipientProfile?.AllowDirectMessages == false ||
            recipientProfile?.BlockedUsers?.Contains(session.Username, StringComparer.OrdinalIgnoreCase) == true ||
            senderProfile?.BlockedUsers?.Contains(recipient, StringComparer.OrdinalIgnoreCase) == true)
        {
            await session.SendAsync("error", new ErrorResponse("Tin nhắn riêng đã bị giới hạn bởi quyền riêng tư."));
            return;
        }

        var message = new DirectMessage(Guid.NewGuid().ToString(), session.Username, recipient, text, DateTimeOffset.Now);
        ClientSession? recipientSession;
        lock (AllClientsLock)
        {
            recipientSession = AllClients.FirstOrDefault(client =>
                client.IsAuthenticated && string.Equals(client.Username, recipient, StringComparison.OrdinalIgnoreCase));
        }

        // Gui ban sao cho nguoi gui de giao dien hien thi ngay sau khi nhan Send.
        await session.SendAsync("direct-message", message);
        if (recipientSession != null)
        {
            await recipientSession.SendAsync("direct-message", message);
        }
        else if (DirectMessageStore != null)
        {
            await DirectMessageStore.SaveAsync(message);
        }
        else
        {
            await session.SendAsync("error", new ErrorResponse("Khong the luu tin nhan offline: MongoDB chua san sang."));
        }
    }

    private static async Task HandleGetUnreadDirectMessagesAsync(ClientSession session)
    {
        if (!session.IsAuthenticated) return;

        var messages = DirectMessageStore == null
            ? new List<DirectMessage>()
            : await DirectMessageStore.TakeUnreadAsync(session.Username);
        await session.SendAsync("unread-direct-messages", new UnreadDirectMessagesResponse(messages));
    }

    private static async Task HandleDisconnectAsync(ClientSession session)
    {
        var room = session.CurrentRoom;
        await LeaveVoiceChannelAsync(session);
        Rooms.Leave(session);
        lock (AllClientsLock) AllClients.Remove(session);
        session.Close();

        if (room != null && session.Username.Length > 0)
        {
            await Rooms.BroadcastAsync(room, "system",
                new SystemNotice(room, $"{session.Username} da ngat ket noi.", DateTimeOffset.Now));
            await Rooms.BroadcastAsync(room, "online-users",
                new OnlineUsersResponse(room, Rooms.GetUsernames(room)));
        }
        Console.WriteLine($"[-] Client {session.Username} da ngat ket noi.");
        await BroadcastRoomListToAllAsync();
    }

    private static async Task HandleRegisterRequestAsync(ClientSession session, RegisterRequest? req)
    {
        if (UserStore == null)
        {
            await session.SendAsync("error", new ErrorResponse("MongoDB chua san sang."));
            return;
        }

        var username = (req?.Username ?? "").Trim();
        var password = req?.Password ?? "";
        var email = (req?.Email ?? "").Trim();
        if (username.Length < 3 || password.Length < 6 || !email.Contains('@') || !email.Contains('.'))
        {
            await session.SendAsync("error", new ErrorResponse("Vui long nhap username >= 3 ky tu, mat khau >= 6 ky tu va email hop le."));
            return;
        }
        if (!await TryConsumeRateLimitAsync(session, "registration-otp", email, 3, TimeSpan.FromMinutes(15))) return;

        if (!Email.IsConfigured)
        {
            await session.SendAsync("error", new ErrorResponse("Chua cau hinh EMAIL_SCRIPT_URL/EMAIL_SCRIPT_SECRET hoac Resend."));
            return;
        }

        var otp = RandomNumberGenerator.GetInt32(100000, 1000000).ToString();
        var expiresAtUtc = DateTime.UtcNow.AddMinutes(10);
        Console.WriteLine($"[Register] Nhan yeu cau OTP cho username '{username}', email '{email}'. Chua ghi MongoDB.");
        try
        {
            await Email.SendOtpAsync(email, otp);
            var registrationToken = OtpTokens.Create(username, email, otp, expiresAtUtc);
            session.PendingRegistration = new PendingRegistration(username, password, email, registrationToken);
            Console.WriteLine($"[Register] Da gui OTP cho '{email}'. Dang cho xac nhan.");
            await session.SendAsync("register-otp-sent", new OtpSentResponse("Da gui ma OTP toi email cua ban."));
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine($"[Register] Gui OTP that bai cho '{email}': {ex}");
            await session.SendAsync("error", new ErrorResponse($"Khong gui duoc email OTP: {ex.Message}"));
        }
    }
    private static async Task HandleRegisterVerifyAsync(ClientSession session, VerifyRegistrationRequest? req)
    {
        if (UserStore == null)
        {
            await session.SendAsync("error", new ErrorResponse("MongoDB chua san sang."));
            return;
        }

        var pending = session.PendingRegistration;
        var otp = (req?.Otp ?? "").Trim();
        if (!await TryConsumeRateLimitAsync(session, "registration-otp-verify", session.IpAddress, 5, TimeSpan.FromMinutes(10))) return;
        if (pending == null)
        {
            session.PendingRegistration = null;
            await session.SendAsync("error", new ErrorResponse("Ma OTP da het han. Hay yeu cau dang ky lai."));
            return;
        }
        if (!OtpTokens.TryValidate(pending.RegistrationToken, otp, out var claims) ||
            claims == null || !string.Equals(claims.Username, pending.Username, StringComparison.Ordinal) ||
            !string.Equals(claims.Email, pending.Email, StringComparison.OrdinalIgnoreCase))
        {
            await session.SendAsync("error", new ErrorResponse("Ma OTP khong dung hoac da het han."));
            return;
        }

        var result = await UserStore.RegisterAsync(pending.Username, pending.Password, pending.Email);
        session.PendingRegistration = null;
        if (!result.Success)
        {
            await session.SendAsync("error", new ErrorResponse(result.Error));
            return;
        }

        session.Username = pending.Username;
        session.IsAuthenticated = true;
        Console.WriteLine($"[Register] Da xac nhan OTP va ghi user '{pending.Username}' vao MongoDB.");
        await session.SendAsync("auth", new AuthResponse(pending.Username, result.Error));
    }

    private static async Task HandleLoginAsync(ClientSession session, AuthRequest? req)
    {
        var username = (req?.Username ?? "").Trim();
        Console.WriteLine($"[Auth] Login attempt user='{username}' ip={session.IpAddress}");

        if (UserStore == null)
        {
            Console.Error.WriteLine($"[Auth] Login rejected for user='{username}': MongoDB is unavailable.");
            await session.SendAsync("error", new ErrorResponse("MongoDB chua san sang."));
            return;
        }

        if (!await TryConsumeRateLimitAsync(session, "login", username, 5, TimeSpan.FromMinutes(10)))
        {
            Console.WriteLine($"[Auth] Login rate-limited user='{username}' ip={session.IpAddress}");
            return;
        }
        if (!await TryConsumeRateLimitAsync(session, "login-ip", session.IpAddress, 20, TimeSpan.FromMinutes(10)))
        {
            Console.WriteLine($"[Auth] Login rate-limited ip={session.IpAddress}");
            return;
        }
        if (!await UserStore.ValidateLoginAsync(username, req?.Password ?? ""))
        {
            Console.WriteLine($"[Auth] Login rejected: invalid username or password user='{username}' ip={session.IpAddress}");
            await session.SendAsync("error", new ErrorResponse("Username hoac mat khau khong dung."));
            return;
        }

        // Check if 2FA is enabled
        var twoFactorEnabled = await UserStore.IsTwoFactorEnabledAsync(username);
        if (twoFactorEnabled)
        {
            // Send 2FA challenge
            session.Username = username;
            session.IsAuthenticated = false; // Not fully authenticated until 2FA
            Console.WriteLine($"[Auth] Password accepted; 2FA required user='{username}' ip={session.IpAddress}");
            await session.SendAsync("2fa-required", new TwoFactorVerifyResponse(false, "Vui lòng nhập mã xác thực 2FA."));
            return;
        }

        // No 2FA required, complete login
        await CompleteLoginAsync(session, username);
    }

    private static async Task<bool> TryConsumeRateLimitAsync(ClientSession session, string scope, string subject, int limit, TimeSpan window)
    {
        var normalizedSubject = string.IsNullOrWhiteSpace(subject) ? "unknown" : subject.Trim().ToUpperInvariant();
        var key = $"{scope}:{session.IpAddress}:{normalizedSubject}";
        if (AuthRateLimiter.TryAcquire(key, limit, window, out var retryAfter)) return true;

        var seconds = Math.Max(1, (int)Math.Ceiling(retryAfter.TotalSeconds));
        await session.SendAsync("error", new ErrorResponse($"Bạn đã thử quá nhiều lần. Vui lòng thử lại sau {seconds} giây."));
        return false;
    }

    private static async Task CompleteLoginAsync(ClientSession session, string username)
    {
        if (UserStore == null) return;

        session.Username = username;
        session.IsAuthenticated = true;
        session.TwoFactorVerified = true;
        session.LoginTime = DateTime.UtcNow;
        session.LastActive = DateTime.UtcNow;

        var userSession = new UserSession
        {
            SessionId = session.SessionId,
            DeviceInfo = session.DeviceInfo,
            IpAddress = session.IpAddress,
            LoginTime = session.LoginTime,
            LastActive = session.LastActive
        };
        await UserStore.AddSessionAsync(username, userSession);

        var loginEntry = new LoginHistoryEntry
        {
            LoginTime = session.LoginTime,
            IpAddress = session.IpAddress,
            DeviceInfo = session.DeviceInfo,
            Success = true
        };
        await UserStore.AddLoginHistoryAsync(username, loginEntry);

        Console.WriteLine($"[Auth] Login successful user='{username}' ip={session.IpAddress}");
        await session.SendAsync("auth", new AuthResponse(username, "Dang nhap thanh cong."));
    }

    private static async Task HandleTwoFactorVerifyAsync(ClientSession session, TwoFactorVerifyRequest? req)
    {
        if (UserStore == null || req == null || string.IsNullOrEmpty(session.Username)) return;

        var username = session.Username;
        if (!await TryConsumeRateLimitAsync(session, "two-factor-verify", username, 5, TimeSpan.FromMinutes(10))) return;
        var code = req.Code.Trim();

        if (await UserStore.VerifyTwoFactorAsync(username, code))
        {
            await CompleteLoginAsync(session, username);
        }
        else
        {
            await session.SendAsync("2fa-verify", new TwoFactorVerifyResponse(false, "Mã xác thực không đúng."));
        }
    }

    // Session management handlers
    private static async Task HandleGetSessionsAsync(ClientSession session)
    {
        if (!session.IsAuthenticated || UserStore == null) return;

        var sessions = await UserStore.GetSessionsAsync(session.Username);
        var sessionInfos = sessions.Select(s => new SessionInfo(
            s.SessionId,
            s.DeviceInfo,
            s.IpAddress,
            new DateTimeOffset(s.LoginTime, TimeSpan.Zero),
            new DateTimeOffset(s.LastActive, TimeSpan.Zero),
            s.SessionId == session.SessionId
        )).ToList();

        await session.SendAsync("sessions", new GetSessionsResponse(sessionInfos));
    }

    private static async Task HandleRevokeSessionAsync(ClientSession session, RevokeSessionRequest? req)
    {
        if (!session.IsAuthenticated || UserStore == null || req == null) return;

        // Don't allow revoking current session through this endpoint
        if (req.SessionId == session.SessionId)
        {
            await session.SendAsync("session-revoked", new RevokeSessionResponse(false, "Không thể thu hồi phiên hiện tại. Hãy dùng 'Đăng xuất khỏi tất cả thiết bị'."));
            return;
        }

        await UserStore.RemoveSessionAsync(session.Username, req.SessionId);

        // Also disconnect the session if it's currently connected
        ClientSession? targetSession;
        lock (AllClientsLock)
        {
            targetSession = AllClients.FirstOrDefault(c => c.SessionId == req.SessionId && c.IsAuthenticated);
        }

        if (targetSession != null)
        {
            await targetSession.SendAsync("session-revoked", new RevokeSessionResponse(true, "Phiên đăng nhập đã bị thu hồi."));
            targetSession.Close();
        }

        await session.SendAsync("session-revoked", new RevokeSessionResponse(true, "Đã thu hồi phiên đăng nhập."));
    }

    private static async Task HandleRevokeAllSessionsAsync(ClientSession session)
    {
        if (!session.IsAuthenticated || UserStore == null) return;

        // Keep current session, revoke all others
        await UserStore.RemoveAllSessionsAsync(session.Username, session.SessionId);

        // Disconnect all other sessions
        List<ClientSession> otherSessions;
        lock (AllClientsLock)
        {
            otherSessions = AllClients
                .Where(c => c.IsAuthenticated && 
                    string.Equals(c.Username, session.Username, StringComparison.OrdinalIgnoreCase) &&
                    c.SessionId != session.SessionId)
                .ToList();
        }

        foreach (var otherSession in otherSessions)
        {
            await otherSession.SendAsync("session-revoked", new RevokeSessionResponse(true, "Phiên đăng nhập đã bị thu hồi bởi người dùng."));
            otherSession.Close();
        }

        await session.SendAsync("all-sessions-revoked", new RevokeAllSessionsResponse(true, "Đã đăng xuất khỏi tất cả thiết bị khác."));
    }

    // Two-factor authentication handlers
    private static async Task HandleTwoFactorSetupAsync(ClientSession session)
    {
        if (!session.IsAuthenticated || UserStore == null) return;

        var result = await UserStore.SetupTwoFactorAsync(session.Username);
        if (!result.Success)
        {
            await session.SendAsync("2fa-setup", new TwoFactorSetupResponse("", "", new List<string>()));
            return;
        }

        // Generate QR code (base64 encoded PNG)
        var qrCodeBase64 = GenerateQrCodeBase64($"otpauth://totp/ChatNet:{session.Username}?secret={result.Secret}&issuer=ChatNet");

        await session.SendAsync("2fa-setup", new TwoFactorSetupResponse(result.Secret!, qrCodeBase64, result.BackupCodes!));
    }

    private static async Task HandleTwoFactorEnableAsync(ClientSession session, TwoFactorEnableRequest? req)
    {
        if (!session.IsAuthenticated || UserStore == null || req == null) return;

        var result = await UserStore.EnableTwoFactorAsync(session.Username, req.Code.Trim());
        await session.SendAsync("2fa-enabled", new TwoFactorEnableResponse(result.Success, result.Message, result.BackupCodes));
    }

    private static async Task HandleTwoFactorDisableAsync(ClientSession session, TwoFactorDisableRequest? req)
    {
        if (!session.IsAuthenticated || UserStore == null || req == null) return;

        var result = await UserStore.DisableTwoFactorAsync(session.Username, req.Code.Trim());
        await session.SendAsync("2fa-disabled", new TwoFactorDisableResponse(result.Success, result.Message));
    }

    private static async Task HandleTwoFactorStatusAsync(ClientSession session)
    {
        if (!session.IsAuthenticated || UserStore == null) return;

        var enabled = await UserStore.IsTwoFactorEnabledAsync(session.Username);
        await session.SendAsync("2fa-status", new TwoFactorStatusResponse(enabled));
    }

    // Login history handler
    private static async Task HandleGetLoginHistoryAsync(ClientSession session)
    {
        if (!session.IsAuthenticated || UserStore == null) return;

        var history = await UserStore.GetLoginHistoryAsync(session.Username);
        var historyEntries = history.Select(h => new LoginHistoryEntryResponse(
            h.LoginTime,
            h.IpAddress,
            h.DeviceInfo,
            h.Success
        )).ToList();

        await session.SendAsync("login-history", new GetLoginHistoryResponse(historyEntries));
    }

    private static async Task BroadcastRoomListToAllAsync()
    {
        List<ClientSession> clients;
        lock (AllClientsLock)
        {
            clients = AllClients.Where(client => client.IsAuthenticated).ToList();
        }

        var response = new RoomListResponse(Rooms.GetRoomList());
        await Task.WhenAll(clients.Select(client => client.SendAsync("room-list", response)));
    }

    private static string GenerateQrCodeBase64(string text)
    {
        using var generator = new QRCodeGenerator();
        using var data = generator.CreateQrCode(text, QRCodeGenerator.ECCLevel.Q);
        var qrCode = new PngByteQRCode(data);
        return Convert.ToBase64String(qrCode.GetGraphic(10));
    }

        private static async Task HandleAddReactionAsync(ClientSession session, AddReactionRequest? req)
    {
        if (!session.IsAuthenticated || MessageStore == null || req == null) return;
        if (string.IsNullOrWhiteSpace(req.MessageId) || string.IsNullOrWhiteSpace(req.Reaction)) return;

        var messageDoc = await MessageStore.GetMessageByIdAsync(req.MessageId);
        if (messageDoc == null) return;

        var reactions = messageDoc.Reactions?.ToList() ?? new List<string>();
        if (!reactions.Contains(req.Reaction))
        {
            reactions.Add(req.Reaction);
            await MessageStore.UpdateReactionsAsync(req.MessageId, reactions);

            var update = new ReactionUpdate(req.MessageId, req.Reaction, new List<string> { session.Username });
            await Rooms.BroadcastAsync(messageDoc.Room, "reaction-update", update);
        }
    }

    private static async Task HandleRemoveReactionAsync(ClientSession session, RemoveReactionRequest? req)
    {
        if (!session.IsAuthenticated || MessageStore == null || req == null) return;
        if (string.IsNullOrWhiteSpace(req.MessageId) || string.IsNullOrWhiteSpace(req.Reaction)) return;

        var messageDoc = await MessageStore.GetMessageByIdAsync(req.MessageId);
        if (messageDoc == null) return;

        var reactions = messageDoc.Reactions?.ToList() ?? new List<string>();
        if (reactions.Contains(req.Reaction))
        {
            reactions.Remove(req.Reaction);
            await MessageStore.UpdateReactionsAsync(req.MessageId, reactions);

            var update = new ReactionUpdate(req.MessageId, req.Reaction, new List<string>());
            await Rooms.BroadcastAsync(messageDoc.Room, "reaction-update", update);
        }
    }

    private static async Task HandleEditMessageAsync(ClientSession session, EditMessageRequest? req)
    {
        if (!session.IsAuthenticated || MessageStore == null || req == null) return;
        var text = req.Text?.Trim();
        if (string.IsNullOrWhiteSpace(req.MessageId) || !ChatLimits.IsValidChatText(text))
        {
            await session.SendAsync("error", new ErrorResponse("Noi dung tin nhan khong hop le."));
            return;
        }

        var message = await MessageStore.GetMessageByIdAsync(req.MessageId);
        if (message == null) return;
        if (!string.Equals(message.From, session.Username, StringComparison.OrdinalIgnoreCase))
        {
            await session.SendAsync("error", new ErrorResponse("Ban chi co the sua tin nhan cua chinh minh."));
            return;
        }

        await MessageStore.UpdateTextAsync(req.MessageId, text!);
        await Rooms.BroadcastAsync(message.Room, "message-updated", new MessageUpdated(req.MessageId, text!));
    }

    private static async Task HandleDeleteMessageAsync(ClientSession session, DeleteMessageRequest? req)
    {
        if (!session.IsAuthenticated || MessageStore == null || req == null || string.IsNullOrWhiteSpace(req.MessageId)) return;

        var message = await MessageStore.GetMessageByIdAsync(req.MessageId);
        if (message == null) return;
        if (!string.Equals(message.From, session.Username, StringComparison.OrdinalIgnoreCase))
        {
            await session.SendAsync("error", new ErrorResponse("Ban chi co the xoa tin nhan cua chinh minh."));
            return;
        }

        await MessageStore.DeleteAsync(req.MessageId);
        await Rooms.BroadcastAsync(message.Room, "message-deleted", new MessageDeleted(req.MessageId));
    }
    public class PrefixStream : Stream
    {
        private readonly Stream _subStream;
        private byte[]? _firstByte;

        public PrefixStream(Stream subStream, byte firstByte)
        {
            _subStream = subStream;
            _firstByte = new byte[] { firstByte };
        }

        public override async ValueTask<int> ReadAsync(Memory<byte> buffer, CancellationToken cancellationToken = default)
        {
            if (_firstByte != null)
            {
                buffer.Span[0] = _firstByte[0];
                _firstByte = null;
                return 1;
            }
            return await _subStream.ReadAsync(buffer, cancellationToken);
        }

        public override int Read(byte[] buffer, int offset, int count)
        {
            if (_firstByte != null)
            {
                buffer[offset] = _firstByte[0];
                _firstByte = null;
                return 1;
            }
            return _subStream.Read(buffer, offset, count);
        }

        public override bool CanRead => _subStream.CanRead;
        public override bool CanSeek => _subStream.CanSeek;
        public override bool CanWrite => _subStream.CanWrite;
        public override long Length => _subStream.Length;
        public override long Position { get => _subStream.Position; set => _subStream.Position = value; }
        public override void Flush() => _subStream.Flush();
        public override long Seek(long offset, SeekOrigin origin) => _subStream.Seek(offset, origin);
        public override void SetLength(long value) => _subStream.SetLength(value);
        public override void Write(byte[] buffer, int offset, int count) => _subStream.Write(buffer, offset, count);
    }
}
