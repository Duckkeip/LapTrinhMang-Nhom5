using System.Net;
using System.Net.WebSockets;
using ChatProtocol;

namespace ChatClient;

/// <summary>
/// Bọc một kết nối WebSocket tới server và gửi/nhận JSON Envelope.
/// UI (WinForms) không dùng trực tiếp WebSocket, mà đăng ký event MessageReceived/
/// Disconnected roi nhan Envelope qua do.
/// </summary>
public class NetworkClient
{
    private ClientWebSocket? _socket;
    private CancellationTokenSource? _cts;
    private readonly SemaphoreSlim _writeLock = new(1, 1);

    public event Action<Envelope>? MessageReceived;
    public event Action<string>? Disconnected;

    public bool IsConnected => _socket?.State == WebSocketState.Open;

    public async Task ConnectAsync(string host, int port)
    {
        host = host.Trim();
        if (Uri.TryCreate(host, UriKind.Absolute, out var suppliedUri) &&
            suppliedUri.Scheme is "http" or "https" or "ws" or "wss")
        {
            host = suppliedUri.Host;
        }

        var scheme = IsLoopbackHost(host) ? "ws" : "wss";
        var endpoint = new UriBuilder(scheme, host, port, "/ws").Uri;
        _socket = new ClientWebSocket();
        _cts = new CancellationTokenSource();
        try
        {
            await _socket.ConnectAsync(endpoint, _cts.Token);
        }
        catch (Exception ex)
        {
            Close();
            throw new InvalidOperationException($"Không thể kết nối WebSocket tới {endpoint}.", ex);
        }

        // Vong lap doc chay nen (khong block UI thread) - vi vay cac noi nhan du lieu
        // phai Invoke ve UI thread truoc khi dung cham vao Control (xem ChatForm).
        _ = Task.Run(() => ReadLoopAsync(_cts.Token));
    }

    private static bool IsLoopbackHost(string host) =>
        string.Equals(host, "localhost", StringComparison.OrdinalIgnoreCase) ||
        IPAddress.TryParse(host, out var address) && IPAddress.IsLoopback(address);

    /// <summary>Gui bat dong bo - dung trong code da la async (man hinh dang nhap).</summary>
    public async Task SendAsync(string type, object? data = null)
    {
        if (_socket?.State != WebSocketState.Open) return;
        await _writeLock.WaitAsync();
        try
        {
            await WebSocketEnvelopeCodec.SendAsync(_socket, type, data, _cts?.Token ?? CancellationToken.None);
        }
        catch (Exception ex)
        {
            Disconnected?.Invoke($"Loi khi gui: {ex.Message}");
        }
        finally
        {
            _writeLock.Release();
        }
    }

    /// <summary>Gui "quen ket qua" - tien dung trong cac event handler dong bo (Click, TextChanged...).</summary>
    public void Send(string type, object? data = null) => _ = SendAsync(type, data);

    private async Task ReadLoopAsync(CancellationToken token)
    {
        try
        {
            while (!token.IsCancellationRequested)
            {
                Envelope? envelope;
                try
                {
                    envelope = await WebSocketEnvelopeCodec.ReceiveAsync(_socket!, token);
                }
                catch (InvalidDataException ex)
                {
                    Disconnected?.Invoke($"Frame khong hop le: {ex.Message}");
                    return;
                }

                if (envelope == null)
                {
                    Disconnected?.Invoke("Server da dong ket noi.");
                    return;
                }

                MessageReceived?.Invoke(envelope);
            }
        }
        catch (Exception ex)
        {
            if (!token.IsCancellationRequested)
                Disconnected?.Invoke($"Mat ket noi: {ex.Message}");
        }
    }

    public void Close()
    {
        _cts?.Cancel();
        try { _socket?.Abort(); } catch { /* ignore */ }
        _socket?.Dispose();
    }
}
