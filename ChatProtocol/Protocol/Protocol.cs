using System.Buffers.Binary;
using System.Net.WebSockets;
using System.Text;
using System.Text.Json;
namespace ChatProtocol;

// Envelope là hợp đồng JSON dùng chung cho client và server. WebSocket giữ ranh giới
// message; FrameCodec bên dưới vẫn hỗ trợ length-prefix cho các công cụ TCP cũ.
public record Envelope(string Type, JsonElement Data);

// ==== Cac kieu du lieu (payload) cho tung loai message ====
// Moi Envelope.Type ("chat", "join", "auth", ...) tuong ung 1 record ben duoi lam "Data".

public record AuthRequest(string Username, string Password);           // "login"
public record RegisterRequest(string Username, string Password, string Email); // "register-request"
public record VerifyRegistrationRequest(string Otp);                   // "register-verify"
public record AuthResponse(string Username, string Message);           // "auth"
public record OtpSentResponse(string Message);                         // "register-otp-sent"
public record ErrorResponse(string Message);                           // "error"

public record JoinRequest(string Room);                                // client -> server: "join"
public record JoinResponse(string Room, string Text);                  // server -> client: "join"
public record CreateRoomRequest(string Room);                          // "create-room"


// === Friend System ===
public record SendFriendRequest(string TargetUsername);           // client -> server: "friend-request"
public record FriendRequestSentResponse(string TargetUsername);   // server -> client: "friend-request-sent"
public record FriendRequestReceived(string FromUsername);         // server -> client (push): "friend-request-received"
public record RespondFriendRequest(string FromUsername, bool Accept); // client -> server: "friend-respond"
public record FriendRequestResponse(bool Success, string Message); // server -> client: "friend-responded"
public record RemoveFriendRequest(string Username);                // client -> server: "remove-friend"
public record FriendRemovedResponse(bool Success, string Message); // server -> client: "friend-removed"
public record GetFriendsRequest;                                   // client -> server: "get-friends"
public record GetFriendsResponse(List<FriendInfo> Friends);        // server -> client: "friends-list"
public record GetPendingRequestsRequest;                           // client -> server: "get-pending-requests"
public record GetPendingRequestsResponse(List<FriendRequestInfo> Requests); // server -> client: "pending-requests"

public record FriendInfo(
    string Username,
    string? DisplayName,
    string? AvatarBase64,
    bool IsOnline,
    DateTimeOffset AddedAt);

public record FriendRequestInfo(
    string FromUsername,
    string? FromDisplayName,
    string? FromAvatarBase64,
    DateTimeOffset SentAt);


// ==== RE:CHAT communities (Discord-style servers) ====
// A community is separate from the legacy global rooms above.  The client will
// use these records from its main shell once the server selector UI is added.
public enum ServerRole { Owner, Admin, Member }
public enum ServerChannelType { Text, Voice, Announcement }

public record CreateServerRequest(string Name, string? Description);  // "request-server-creation-otp"
public record VerifyServerCreationRequest(string Otp);                 // "verify-server-creation"
public record ServerCreationOtpSentResponse(string Message);
public record ServerSummary(
    string Id, string Name, string? Description, string OwnerUsername,
    ServerRole Role, int MemberCount, DateTimeOffset CreatedAt);
public record ServerChannelInfo(
    string Id, string ServerId, string Name, ServerChannelType Type, int Position);
public record ServerCreatedResponse(ServerSummary Server, List<ServerChannelInfo> Channels);
public record GetMyServersRequest;
public record MyServersResponse(List<ServerSummary> Servers);
public record GetServerChannelsRequest(string ServerId);
public record ServerChannelsResponse(string ServerId, List<ServerChannelInfo> Channels);
public record CreateServerChannelRequest(string ServerId, string Name, ServerChannelType Type);
public record CreateServerInviteRequest(string ServerId, int? MaxUses = null, int? ExpiresInMinutes = null);
public record ServerInviteInfo(string Code, string ServerId, int? MaxUses, int UsedCount, DateTimeOffset? ExpiresAt);
public record JoinServerInviteRequest(string Code);
public record ServerInviteAcceptedResponse(ServerSummary Server, List<ServerChannelInfo> Channels);

// Voice-channel control plane. These messages synchronize presence and device
// state; audio/video media will later travel through a dedicated WebRTC layer.
public record JoinVoiceChannelRequest(string ChannelId);
public record LeaveVoiceChannelRequest;
public record UpdateVoiceDeviceStateRequest(bool MicrophoneEnabled, bool CameraEnabled);
public record GetVoiceParticipantsRequest(string ChannelId);
public record LiveKitCredentials(string Url, string Token);
public record VoiceParticipant(string Username, bool MicrophoneEnabled, bool CameraEnabled, DateTimeOffset JoinedAt);
public record VoiceChannelState(string ChannelId, List<VoiceParticipant> Participants);
public record WebRtcSignalRequest(string ChannelId, string To, string Kind, string Payload);
public record WebRtcSignal(string ChannelId, string From, string Kind, string Payload);

public record ChatRequest(string Text);                                // client -> server: "chat"
public record ChatMessage(
     string Id, string Room, string From, string Text, DateTimeOffset Time,
     string? FileName = null, string? StoredFile = null, long FileSize = 0,
     List<string>? Reactions = null, bool Edited = false, string? ThumbnailBase64 = null,
    string? AvatarBase64 = null, string? DisplayName = null); // server -> client: "chat"

public record SystemNotice(string Room, string Text, DateTimeOffset Time); // "system"
public record HistoryResponse(string Room, List<ChatMessage> Messages);    // server -> client: "history" (khi vao phong)
public record TypingRequest(bool IsTyping);                                // client -> server: "typing"
public record TypingNotice(string Room, string Username, bool IsTyping);   // server -> client: "typing"

public record RoomInfo(string Name, int Online)
{
    public override string ToString() => $"{Name} ({Online} online)";
}
public record RoomListResponse(List<RoomInfo> Rooms);                  // "room-list"
public record OnlineUsersResponse(string Room, List<string> Users);    // "online-users"

// Nhắn tin riêng: server chuyển trực tiếp tới người nhận, không broadcast vào phòng.
public record DirectMessageRequest(string To, string Text);             // client -> server: "direct-message"
public record DirectMessage(string Id, string From, string To, string Text, DateTimeOffset Time); // server -> client
public record UnreadDirectMessagesResponse(List<DirectMessage> Messages);

// Hồ sơ tối giản, chỉ công khai thông tin an toàn để hiển thị trong ứng dụng.
public record ProfileRequest(string Username);                           // client -> server: "profile"
public record UserProfile(
    string Username,
    DateTimeOffset JoinedAt,
    bool IsOnline,
    string? AvatarBase64 = null,
    string? DisplayName = null,
    string? Email = null,
    string? Bio = null,
    string Status = "online",
    bool AllowDirectMessages = true,
    bool ShowAvatar = true,
    bool ShowOnlineStatus = true,
    List<string>? BlockedUsers = null,
    string Theme = "dark",
    string AccentColor = "#7467F0",
    float ChatFontSize = 10F,
    bool ShowAvatarsInChat = true,
    bool ShowImagePreviews = true,
    bool MessageSound = true,
    bool MentionNotifications = true,
    bool DirectMessageNotifications = true,
    bool OnlineNotifications = false,
    List<string>? Friends = null,           // danh sách username bạn bè
    List<string>? PendingFriendRequests = null, // lời mời gửi đi
    List<string>? ReceivedFriendRequests = null // lời mời nhận được
    );

    
public record ProfileResponse(UserProfile? Profile);                     // server -> client
public record RequestPasswordChangeOtp;
public record VerifyPasswordChangeRequest(string Otp, string NewPassword);
public record PasswordChangeOtpSentResponse(string Message);
public record RequestPasswordResetOtp(string Email);
public record VerifyPasswordResetRequest(string Otp, string NewPassword);
public record PasswordResetOtpSentResponse(string Message);
public record UpdateAvatarRequest(string? AvatarBase64);
public record UpdateProfileRequest(string DisplayName, string Email, string Bio, string Status);
public record UpdatePrivacyRequest(bool AllowDirectMessages, bool ShowAvatar, bool ShowOnlineStatus);
public record BlockUserRequest(string Username, bool Blocked);
public record UpdateAppearanceRequest(
    string Theme,
    string AccentColor,
    float ChatFontSize,
    bool ShowAvatarsInChat,
    bool ShowImagePreviews,
    bool MessageSound,
    bool MentionNotifications,
    bool DirectMessageNotifications,
    bool OnlineNotifications);
public record SettingsUpdatedResponse(string Message);

// Session management
public record SessionInfo(
    string SessionId,
    string DeviceInfo,
    string IpAddress,
    DateTimeOffset LoginTime,
    DateTimeOffset LastActive,
    bool IsCurrentSession);
public record GetSessionsRequest;                                      // client -> server: "get-sessions"
public record GetSessionsResponse(List<SessionInfo> Sessions);         // server -> client: "sessions"
public record RevokeSessionRequest(string SessionId);                  // client -> server: "revoke-session"
public record RevokeSessionResponse(bool Success, string Message);     // server -> client: "session-revoked"
public record RevokeAllSessionsRequest;                                // client -> server: "revoke-all-sessions"
public record RevokeAllSessionsResponse(bool Success, string Message); // server -> client: "all-sessions-revoked"

// Two-factor authentication
public record TwoFactorSetupRequest;                                   // client -> server: "2fa-setup"
public record TwoFactorSetupResponse(string Secret, string QrCodeBase64, List<string> BackupCodes); // server -> client: "2fa-setup"
public record TwoFactorEnableRequest(string Code);                     // client -> server: "2fa-enable"
public record TwoFactorEnableResponse(bool Success, string Message, List<string>? BackupCodes = null); // server -> client: "2fa-enabled"
public record TwoFactorDisableRequest(string Code);                    // client -> server: "2fa-disable"
public record TwoFactorDisableResponse(bool Success, string Message);  // server -> client: "2fa-disabled"
public record TwoFactorStatusResponse(bool Enabled);                   // server -> client: "2fa-status"
public record TwoFactorVerifyRequest(string Code);                     // client -> server: "2fa-verify" (during login)
public record TwoFactorVerifyResponse(bool Success, string Message);   // server -> client: "2fa-verify"

// Login history
public record LoginHistoryEntry(
    DateTimeOffset LoginTime,
    string IpAddress,
    string DeviceInfo,
    bool Success);
public record LoginHistoryEntryResponse(
    DateTimeOffset LoginTime,
    string IpAddress,
    string DeviceInfo,
    bool Success);
public record GetLoginHistoryRequest;                                  // client -> server: "get-login-history"
public record GetLoginHistoryResponse(List<LoginHistoryEntryResponse> History); // server -> client: "login-history"

// Tệp được tách thành nhiều chunk nhỏ để không vượt giới hạn frame của TCP protocol.
public static class ChatLimits
{
    public const long MaxFileBytes = 12 * 1024 * 1024;
    public const int FileChunkBytes = 512 * 1024;
    public const int MaxRoomNameLength = 64;
    public const int MaxChatTextLength = 4_000;
    public const int MaxChannelNameLength = 64;
    public static bool IsValidRoomName(string? room)
    {
        if (string.IsNullOrWhiteSpace(room) || room.Length > MaxRoomNameLength) return false;
        return room.All(character => !char.IsControl(character));
    }
    public static bool IsValidChannelName(string channelName)
    {
        return !string.IsNullOrWhiteSpace(channelName) && channelName.Length <= MaxChannelNameLength && !channelName.Any(char.IsControl);
    }
    public static bool IsValidChatText(string? text) =>
        !string.IsNullOrWhiteSpace(text) && text.Length <= MaxChatTextLength;
}

public record FileUploadStartRequest(string UploadId, string FileName, long FileSize, string? ThumbnailBase64 = null);
public record FileUploadChunkRequest(string UploadId, int Index, string Base64Data);
public record FileUploadCompleteRequest(string UploadId);
public record FileDownloadRequest(string StoredFile);
public record FileDownloadStart(string TransferId, string FileName, long FileSize);
public record FileDownloadChunk(string TransferId, int Index, string Base64Data);
public record FileDownloadComplete(string TransferId);

// Du dinh cho tinh nang dang ky/dang nhap tu luu (hien MongoUserStore dang tu quan ly rieng).
public record UserRecord(string Username, string Salt, string PasswordHash);



// ==== Reaction types ====
public record AddReactionRequest(string MessageId, string Reaction);           // client -> server: "add-reaction"
public record RemoveReactionRequest(string MessageId, string Reaction);        // client -> server: "remove-reaction"
public record ReactionUpdate(string MessageId, string Reaction, List<string> Users); // server -> client: "reaction-update"
public record EditMessageRequest(string MessageId, string Text);                // client -> server: "edit-message"
public record DeleteMessageRequest(string MessageId);                           // client -> server: "delete-message"
public record MessageUpdated(string MessageId, string Text);                    // server -> client: "message-updated"
public record MessageDeleted(string MessageId);                                 // server -> client: "message-deleted"

public static class FrameCodec
{
    public const int MaxFrameBytes = 6 * 1024 * 1024; // 6MB - du cho text, du du sau nay gui anh nho

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = true,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase
    };
    public static JsonSerializerOptions Options => JsonOptions;

    public static async Task WriteAsync(Stream stream, string type, object? data, CancellationToken ct = default)
    {
        var json = JsonSerializer.Serialize(new { type, data }, JsonOptions);
        var body = Encoding.UTF8.GetBytes(json);
        if (body.Length > MaxFrameBytes) throw new InvalidDataException("Goi tin vuot qua 6 MB.");

        var header = new byte[4];
        BinaryPrimitives.WriteInt32BigEndian(header, body.Length);

        await stream.WriteAsync(header, ct);
        await stream.WriteAsync(body, ct);
        await stream.FlushAsync(ct);
    }

    public static async Task<Envelope?> ReadAsync(Stream stream, CancellationToken ct = default)
    {
        var header = await ReadExactlyAsync(stream, 4, ct);
        if (header is null) return null; // client dong ket noi giua chung

        var size = BinaryPrimitives.ReadInt32BigEndian(header);
        if (size <= 0 || size > MaxFrameBytes) throw new InvalidDataException("Do dai frame khong hop le.");

        var body = await ReadExactlyAsync(stream, size, ct);
        if (body is null) return null;

        return JsonSerializer.Deserialize<Envelope>(body, JsonOptions);
    }

    private static async Task<byte[]?> ReadExactlyAsync(Stream s, int count, CancellationToken ct)
    {
        var buffer = new byte[count];
        var read = 0;
        while (read < count)
        {
            var n = await s.ReadAsync(buffer.AsMemory(read, count - read), ct);
            if (n == 0) return null; // ket noi da dong
            read += n;
        }
        return buffer;
    }
}

public static class WebSocketEnvelopeCodec
{
    private const int ReceiveBufferBytes = 16 * 1024;

    public static async Task SendAsync(WebSocket socket, string type, object? data, CancellationToken cancellationToken = default)
    {
        var payload = JsonSerializer.SerializeToUtf8Bytes(new { type, data }, FrameCodec.Options);
        if (payload.Length > FrameCodec.MaxFrameBytes)
            throw new InvalidDataException("Goi tin vuot qua 6 MB.");

        await socket.SendAsync(payload, WebSocketMessageType.Text, true, cancellationToken);
    }

    public static async Task<Envelope?> ReceiveAsync(WebSocket socket, CancellationToken cancellationToken = default)
    {
        using var payload = new MemoryStream();
        var buffer = new byte[ReceiveBufferBytes];
        WebSocketMessageType? messageType = null;

        while (true)
        {
            var result = await socket.ReceiveAsync(buffer.AsMemory(), cancellationToken);
            if (result.MessageType == WebSocketMessageType.Close)
            {
                if (socket.State == WebSocketState.CloseReceived)
                {
                    await socket.CloseOutputAsync(
                        socket.CloseStatus ?? WebSocketCloseStatus.NormalClosure,
                        socket.CloseStatusDescription,
                        cancellationToken);
                }
                return null;
            }
            if (result.MessageType != WebSocketMessageType.Text)
                throw new InvalidDataException("Chi chap nhan WebSocket message dang text.");

            messageType ??= result.MessageType;
            if (messageType != result.MessageType)
                throw new InvalidDataException("WebSocket message thay doi kieu giua cac fragment.");
            if (payload.Length + result.Count > FrameCodec.MaxFrameBytes)
                throw new InvalidDataException("Goi tin vuot qua 6 MB.");

            payload.Write(buffer, 0, result.Count);
            if (result.EndOfMessage) break;
        }

        if (payload.Length == 0) throw new InvalidDataException("WebSocket message rong.");
        return JsonSerializer.Deserialize<Envelope>(payload.GetBuffer().AsSpan(0, (int)payload.Length), FrameCodec.Options);
    }
}

/// <summary>Rut gon viec doc Envelope.Data (kieu JsonElement) ve dung kieu payload can dung.</summary>
public static class EnvelopeExtensions
{
    public static T? As<T>(this Envelope envelope) => envelope.Data.Deserialize<T>(FrameCodec.Options);
}
