using System.Buffers.Binary;
using System.Text;
using System.Text.Json;

namespace ChatProtocol;

// TCP la mot stream, khong tu bao dau/cuoi 1 message. Moi "phong bi" (envelope) duoc
// dong khung theo kieu length-prefix: [4 byte big-endian = do dai than message][UTF-8 JSON].
// Day la ky thuat framing pho bien khi lam viec truc tiep voi socket TCP (khac voi
// giao thuc newline-delimited don gian hon nhung de vo nghia neu noi dung co chua '\n',
// vi du sau nay gui van ban nhieu dong hoac du lieu nhi phan).
public record Envelope(string Type, JsonElement Data);

// ==== Cac kieu du lieu (payload) cho tung loai message ====
// Moi Envelope.Type ("chat", "join", "auth", ...) tuong ung 1 record ben duoi lam "Data".

public record AuthRequest(string Username, string Password);           // "register" / "login"
public record AuthResponse(string Username, string Message);           // "auth"
public record ErrorResponse(string Message);                           // "error"

public record JoinRequest(string Room);                                // client -> server: "join"
public record JoinResponse(string Room, string Text);                  // server -> client: "join"
public record CreateRoomRequest(string Room);                          // "create-room"

public record ChatRequest(string Text);                                // client -> server: "chat"
public record ChatMessage(
    string Id, string Room, string From, string Text, DateTimeOffset Time,
    string? FileName = null, string? StoredFile = null, long FileSize = 0,
    List<string>? Reactions = null, bool Edited = false, string? ThumbnailBase64 = null); // server -> client: "chat"

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
public record UserProfile(string Username, DateTimeOffset JoinedAt, bool IsOnline);
public record ProfileResponse(UserProfile? Profile);                     // server -> client

// Tệp được tách thành nhiều chunk nhỏ để không vượt giới hạn frame của TCP protocol.
public static class ChatLimits
{
    public const long MaxFileBytes = 12 * 1024 * 1024;
    public const int FileChunkBytes = 512 * 1024;
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

/// <summary>Rut gon viec doc Envelope.Data (kieu JsonElement) ve dung kieu payload can dung.</summary>
public static class EnvelopeExtensions
{
    public static T? As<T>(this Envelope envelope) => envelope.Data.Deserialize<T>(FrameCodec.Options);
}
