using System.Text.Json;

namespace ChatServer;

/// <summary>
/// Giao thuc ung dung tu thiet ke, chay tren nen TCP thuan (khong dung SignalR/Socket.io).
/// Moi message la 1 dong JSON, ket thuc bang ky tu '\n' (newline-delimited JSON).
/// Day la cach pho bien de "dinh khung" (frame) du lieu khi lam viec truc tiep voi TCP stream,
/// vi TCP la giao thuc "stream" khong tu bao dau/cuoi 1 message nhu UDP.
/// </summary>
public class Message
{
    // "join", "chat", "system", "typing", "create-room", "room-list", "online-users", "error"
    public string Type { get; set; } = "";

    public string? Username { get; set; }
    public string? Password { get; set; }
    public string? Room { get; set; }
    public string? Text { get; set; }
    public bool? IsTyping { get; set; }
    public string? Time { get; set; }

    // Dung cho "room-list": ["General(2)", "Random(0)", ...] duoc build san o server
    public List<RoomInfo>? Rooms { get; set; }

    // Dung cho "online-users"
    public List<string>? Users { get; set; }

    public string ToJsonLine() => JsonSerializer.Serialize(this) + "\n";

    public static Message? FromJson(string json)
    {
        try
        {
            return JsonSerializer.Deserialize<Message>(json);
        }
        catch
        {
            return null;
        }
    }
}

public class RoomInfo
{
    public string Name { get; set; } = "";
    public int Online { get; set; }
}
