using System.Text.Json;

namespace ChatClient;

/// <summary>
/// PHAI khop 100% voi ChatServer/Protocol.cs vi day la "hop dong" (giao thuc) giua 2 ben.
/// Neu sau nay tach thanh 1 Class Library dung chung (Shared Project) cho ca Server va
/// Client se sach hon - o day de don gian, minh copy 1 ban.
/// </summary>
public class Message
{
    public string Type { get; set; } = "";

    public string? Username { get; set; }
    public string? Password { get; set; }
    public string? Room { get; set; }
    public string? Text { get; set; }
    public bool? IsTyping { get; set; }
    public string? Time { get; set; }

    public List<RoomInfo>? Rooms { get; set; }
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

    public override string ToString() => $"{Name} ({Online} online)";
}
