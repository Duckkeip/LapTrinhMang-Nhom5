using System.Net.Sockets;
using ChatProtocol;

namespace ChatServer;

/// <summary>
/// Boc mot ket noi TCP (1 client) + trang thai cua no (username, phong, da xac thuc chua).
/// Gui du lieu qua FrameCodec (length-prefix JSON) dinh nghia trong ChatProtocol.
/// </summary>
public class ClientSession
{
    public TcpClient TcpClient { get; }
    public NetworkStream Stream { get; }
    public string Username { get; set; } = "";
    public string? CurrentRoom { get; set; }
    public bool IsAuthenticated { get; set; }
    public Dictionary<string, PendingUpload> Uploads { get; } = new();

    // SemaphoreSlim thay vi "lock" thuong, vi FrameCodec.WriteAsync la ham async
    // (khong the giu "lock" C# qua mot "await").
    private readonly SemaphoreSlim _writeLock = new(1, 1);

    public ClientSession(TcpClient tcpClient)
    {
        TcpClient = tcpClient;
        Stream = tcpClient.GetStream();
    }

    /// <summary>Gui 1 message (type + payload) toi client nay.</summary>
    public async Task SendAsync(string type, object? data = null)
    {
        await _writeLock.WaitAsync();
        try
        {
            if (TcpClient.Connected)
                await FrameCodec.WriteAsync(Stream, type, data);
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
        try { Stream.Close(); } catch { /* ignore */ }
        try { TcpClient.Close(); } catch { /* ignore */ }
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
