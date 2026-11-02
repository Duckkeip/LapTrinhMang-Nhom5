using System.Net.WebSockets;
using ChatProtocol;

namespace ChatServer;

/// <summary>
/// Bọc một WebSocket client cùng trạng thái phiên chat.
/// </summary>
public class ClientSession
{
    public WebSocket Socket { get; }
    public string Username { get; set; } = "";
    public string? CurrentRoom { get; set; }
    public bool IsAuthenticated { get; set; }
    public PendingRegistration? PendingRegistration { get; set; }
    public string? PendingPasswordChangeToken { get; set; }
    public string? PendingPasswordResetToken { get; set; }
    public PendingServerCreation? PendingServerCreation { get; set; }
    public Dictionary<string, PendingUpload> Uploads { get; } = new();
    
    // Session tracking
    public string SessionId { get; } = Guid.NewGuid().ToString();
    public string DeviceInfo { get; set; } = "";
    public string IpAddress { get; set; } = "";
    public DateTime LoginTime { get; set; } = DateTime.UtcNow;
    public DateTime LastActive { get; set; } = DateTime.UtcNow;
    public bool TwoFactorVerified { get; set; } = false;
    public string? VoiceChannelId { get; set; }
    public DateTime VoiceJoinedAt { get; set; }
    public bool VoiceMicrophoneEnabled { get; set; }
    public bool VoiceCameraEnabled { get; set; }

    private readonly SemaphoreSlim _writeLock = new(1, 1);

    public ClientSession(WebSocket socket, string ipAddress)
    {
        Socket = socket;
        IpAddress = ipAddress;
    }

    /// <summary>Gui 1 message (type + payload) toi client nay.</summary>
    public async Task SendAsync(string type, object? data = null)
    {
        await _writeLock.WaitAsync();
        try
        {
            if (Socket.State == WebSocketState.Open)
                await WebSocketEnvelopeCodec.SendAsync(Socket, type, data);
        }
        catch
        {
            // Client da ngat ket noi giua chung; vong lap doc chinh se don dep sau.
        }
        finally
        {
            _writeLock.Release();
        }
    }

    public void Close()
    {
        if (Socket.State == WebSocketState.Open)
        {
            try { Socket.Abort(); } catch { /* ignore */ }
        }
        try { Socket.Dispose(); } catch { /* ignore */ }
    }
}

public sealed class PendingRegistration
{
    public string Username { get; }
    public string Password { get; }
    public string Email { get; }
    public string RegistrationToken { get; }

    public PendingRegistration(string username, string password, string email, string registrationToken)
    {
        Username = username;
        Password = password;
        Email = email;
        RegistrationToken = registrationToken;
    }
}

public sealed class PendingServerCreation
{
    public string Name { get; }
    public string? Description { get; }
    public string VerificationToken { get; }

    public PendingServerCreation(string name, string? description, string verificationToken)
    {
        Name = name;
        Description = description;
        VerificationToken = verificationToken;
    }
}

public sealed class PendingUpload : IDisposable
{
    public string FileName { get; }
    public long ExpectedSize { get; }
    public string? ThumbnailBase64 { get; }
    public int NextChunkIndex { get; set; }
    public MemoryStream Content { get; } = new();

    public PendingUpload(string fileName, long expectedSize, string? thumbnailBase64 = null)
    {
        FileName = fileName;
        ExpectedSize = expectedSize;
        ThumbnailBase64 = thumbnailBase64;
    }

    public void Dispose() => Content.Dispose();
}
