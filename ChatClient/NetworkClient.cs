using System.Net.Sockets;
using ChatProtocol;

namespace ChatClient;

/// <summary>
/// Boc mot ket noi TCP toi server, gui/nhan qua FrameCodec (length-prefix JSON).
/// UI (WinForms) khong dung truc tiep Socket/Stream, ma dang ky event MessageReceived/
/// Disconnected roi nhan Envelope qua do.
/// </summary>
public class NetworkClient
{
    private TcpClient? _tcpClient;
    private NetworkStream? _stream;
    private CancellationTokenSource? _cts;
    private readonly SemaphoreSlim _writeLock = new(1, 1);

    public event Action<Envelope>? MessageReceived;
    public event Action<string>? Disconnected;

    public bool IsConnected => _tcpClient?.Connected ?? false;

    public async Task ConnectAsync(string host, int port)
    {
        _tcpClient = new TcpClient();
        await _tcpClient.ConnectAsync(host, port);
        _stream = _tcpClient.GetStream();
        _cts = new CancellationTokenSource();

        // Vong lap doc chay nen (khong block UI thread) - vi vay cac noi nhan du lieu
        // phai Invoke ve UI thread truoc khi dung cham vao Control (xem ChatForm).
        _ = Task.Run(() => ReadLoopAsync(_cts.Token));
    }

    /// <summary>Gui bat dong bo - dung trong code da la async (man hinh dang nhap).</summary>
    public async Task SendAsync(string type, object? data = null)
    {
        if (_stream == null) return;
        await _writeLock.WaitAsync();
        try
        {
            await FrameCodec.WriteAsync(_stream, type, data);
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
                    envelope = await FrameCodec.ReadAsync(_stream!, token);
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
        try { _stream?.Close(); } catch { /* ignore */ }
        try { _tcpClient?.Close(); } catch { /* ignore */ }
    }
}
