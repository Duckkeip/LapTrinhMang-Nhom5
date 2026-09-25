using System.Net;
using System.Net.Sockets;
using ChatProtocol;

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
    private static MongoFileStore? FileStore;
    private static MongoDirectMessageStore? DirectMessageStore;
    private static AiService Ai = new(null);

    public static async Task Main(string[] args)
    {
        var config = EnvironmentConfig.Load();
        if (config.TryGetValue("MONGODB_URI", out var mongoUri) && mongoUri.Length > 0)
        {
            try
            {
                var mongo = new MongoContext(mongoUri);

                UserStore = new MongoUserStore(mongo.Database);
                await UserStore.InitializeAsync();

                MessageStore = new MongoMessageStore(mongo.Database);
                await MessageStore.InitializeAsync();
                FileStore = new MongoFileStore(mongo.Database);
                DirectMessageStore = new MongoDirectMessageStore(mongo.Database);
                await DirectMessageStore.InitializeAsync();

                Console.WriteLine($"[ChatServer] Đã kết nối MongoDB tại URI: {mongoUri}");
                Console.WriteLine($"[ChatServer] Tên cơ sở dữ liệu: {mongo.Database.DatabaseNamespace.DatabaseName}");
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[ChatServer] Khong ket noi duoc MongoDB: {ex.Message}");
            }
        }
        else
        {
            Console.WriteLine("[ChatServer] Thieu MONGODB_URI trong .env.");
        }

        config.TryGetValue("AI_SERVICE_URL", out var aiServiceUrl);
        Ai = new AiService(aiServiceUrl);

        int port = config.TryGetValue("PORT", out var configuredPort) && int.TryParse(configuredPort, out var envPort)
            ? envPort
            : 5050;
        if (args.Length > 0 && int.TryParse(args[0], out var p)) port = p;

        var listener = new TcpListener(IPAddress.Any, port);
        listener.Start();
        Console.WriteLine($"[ChatServer] Dang lang nghe TCP tai cong {port} (giao thuc length-prefix JSON) ...");
        Console.WriteLine($"[ChatServer] Phong mac dinh: {string.Join(", ", DefaultRooms)}");

        while (true)
        {
            TcpClient tcpClient = await listener.AcceptTcpClientAsync();
            var session = new ClientSession(tcpClient);
            lock (AllClientsLock) AllClients.Add(session);

            // Moi client duoc xu ly tren 1 Task rieng -> server phuc vu nhieu client dong thoi
            _ = Task.Run(() => HandleClientAsync(session));
        }
    }

    private static async Task HandleClientAsync(ClientSession session)
    {
        var remoteEp = session.TcpClient.Client.RemoteEndPoint;
        Console.WriteLine($"[+] Ket noi moi tu {remoteEp}");

        try
        {
            while (true)
            {
                Envelope? envelope;
                try
                {
                    envelope = await FrameCodec.ReadAsync(session.Stream);
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
        catch (IOException)
        {
            // Client rot mang dot ngot -> coi nhu disconnect binh thuong
        }
        catch (Exception ex)
        {
            Console.WriteLine($"[!] Loi voi client {remoteEp}: {ex.Message}");
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
                await HandleRegisterAsync(session, envelope.As<AuthRequest>());
                break;

            case "login":
                await HandleLoginAsync(session, envelope.As<AuthRequest>());
                break;

            case "create-room":
                await HandleCreateRoomAsync(session, envelope.As<CreateRoomRequest>());
                break;

            case "chat":
                await HandleChatAsync(session, envelope.As<ChatRequest>());
                break;

            case "typing":
                await HandleTypingAsync(session, envelope.As<TypingRequest>());
                break;

            case "direct-message":
                await HandleDirectMessageAsync(session, envelope.As<DirectMessageRequest>());
                break;

            case "get-unread-direct-messages":
                await HandleUnreadDirectMessagesAsync(session);
                break;

            case "profile":
                await HandleProfileAsync(session, envelope.As<ProfileRequest>());
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

            default:
                await session.SendAsync("error", new ErrorResponse($"Loai message khong hop le: {envelope.Type}"));
                break;
        }
    }

    private static async Task HandleJoinAsync(ClientSession session, JoinRequest? req)
    {
        if (!session.IsAuthenticated)
        {
            await session.SendAsync("error", new ErrorResponse("Ban can dang nhap truoc."));
            return;
        }

        var room = (req?.Room ?? "").Trim();
        if (room.Length == 0)
        {
            await session.SendAsync("error", new ErrorResponse("Thieu room."));
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
        if (room.Length == 0)
        {
            await session.SendAsync("error", new ErrorResponse("Ten phong khong duoc rong."));
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
        if (text.Length == 0) return;

        if (TryGetAiPrompt(text, out var prompt))
        {
            if (prompt.Length == 0)
            {
                await session.SendAsync("error", new ErrorResponse("Hãy nhập câu hỏi sau @AI hoặc /AI."));
                return;
            }

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

        var chatMessage = new ChatMessage(Guid.NewGuid().ToString(), session.CurrentRoom, session.Username, text, DateTimeOffset.Now);
        if (MessageStore != null) await MessageStore.SaveAsync(chatMessage);
        await Rooms.BroadcastAsync(session.CurrentRoom, "chat", chatMessage);
    }

    private static bool TryGetAiPrompt(string text, out string prompt)
    {
        foreach (var command in new[] { "@ai", "/ai" })
        {
            if (!text.StartsWith(command, StringComparison.OrdinalIgnoreCase)) continue;
            if (text.Length > command.Length && !char.IsWhiteSpace(text[command.Length])) continue;
            prompt = text[command.Length..].Trim();
            return true;
        }

        prompt = "";
        return false;
    }

    private static async Task HandleDirectMessageAsync(ClientSession session, DirectMessageRequest? req)
    {
        if (!session.IsAuthenticated) return;

        var recipientName = (req?.To ?? "").Trim();
        var text = (req?.Text ?? "").Trim();
        if (recipientName.Length == 0 || text.Length == 0)
        {
            await session.SendAsync("error", new ErrorResponse("Tin nhắn riêng cần người nhận và nội dung."));
            return;
        }
        if (string.Equals(recipientName, session.Username, StringComparison.OrdinalIgnoreCase))
        {
            await session.SendAsync("error", new ErrorResponse("Bạn không thể nhắn riêng cho chính mình."));
            return;
        }

        var recipient = FindOnlineClient(recipientName);
        if (recipient == null)
        {
            if (UserStore == null || DirectMessageStore == null || !await UserStore.ExistsAsync(recipientName))
            {
                await session.SendAsync("error", new ErrorResponse($"Không tìm thấy người dùng {recipientName}."));
                return;
            }
            var offlineMessage = new DirectMessage(Guid.NewGuid().ToString(), session.Username, recipientName, text, DateTimeOffset.Now);
            await DirectMessageStore.SaveAsync(offlineMessage);
            await session.SendAsync("direct-message", offlineMessage);
            await session.SendAsync("system", new SystemNotice(session.CurrentRoom ?? "", $"{recipientName} đang offline. Tin nhắn đã được lưu.", DateTimeOffset.Now));
            return;
        }

        var message = new DirectMessage(Guid.NewGuid().ToString(), session.Username, recipient.Username, text, DateTimeOffset.Now);
        await Task.WhenAll(
            recipient.SendAsync("direct-message", message),
            session.SendAsync("direct-message", message));
    }

    private static async Task HandleUnreadDirectMessagesAsync(ClientSession session)
    {
        if (!session.IsAuthenticated || DirectMessageStore == null) return;
        var messages = await DirectMessageStore.TakeUnreadAsync(session.Username);
        if (messages.Count > 0) await session.SendAsync("unread-direct-messages", new UnreadDirectMessagesResponse(messages));
    }

    private static async Task HandleProfileAsync(ClientSession session, ProfileRequest? req)
    {
        if (!session.IsAuthenticated || UserStore == null) return;

        var username = (req?.Username ?? session.Username).Trim();
        if (username.Length == 0) username = session.Username;
        var profile = await UserStore.GetProfileAsync(username, FindOnlineClient(username) != null);
        await session.SendAsync("profile", new ProfileResponse(profile));
    }

    private static async Task HandleFileUploadStartAsync(ClientSession session, FileUploadStartRequest? req)
    {
        if (!session.IsAuthenticated || session.CurrentRoom == null) return;
        if (FileStore == null)
        {
            await session.SendAsync("error", new ErrorResponse("MongoDB chưa sẵn sàng để lưu tệp."));
            return;
        }
        var uploadId = req?.UploadId ?? "";
        var fileName = Path.GetFileName(req?.FileName ?? "");
        var fileSize = req?.FileSize ?? 0;
        if (!Guid.TryParse(uploadId, out _) || fileName.Length == 0 || fileSize <= 0 || fileSize > ChatLimits.MaxFileBytes)
        {
            await session.SendAsync("error", new ErrorResponse("Tệp không hợp lệ hoặc vượt giới hạn 12 MB."));
            return;
        }
        if (session.Uploads.Remove(uploadId, out var oldUpload)) oldUpload.Dispose();
        var thumbnail = req?.ThumbnailBase64;
        if (thumbnail?.Length > 350_000) thumbnail = null;
        session.Uploads[uploadId] = new PendingUpload(fileName, fileSize, thumbnail);
    }

    private static async Task HandleFileUploadChunkAsync(ClientSession session, FileUploadChunkRequest? req)
    {
        if (req == null || !session.Uploads.TryGetValue(req.UploadId, out var upload)) return;
        try
        {
            if (req.Index != upload.NextChunkIndex) throw new InvalidDataException("Thứ tự chunk không hợp lệ.");
            var bytes = Convert.FromBase64String(req.Base64Data);
            if (bytes.Length == 0 || bytes.Length > ChatLimits.FileChunkBytes || upload.Content.Length + bytes.Length > upload.ExpectedSize)
                throw new InvalidDataException("Chunk tệp không hợp lệ.");
            await upload.Content.WriteAsync(bytes);
            upload.NextChunkIndex++;
        }
        catch (Exception)
        {
            session.Uploads.Remove(req.UploadId);
            upload.Dispose();
            await session.SendAsync("error", new ErrorResponse("Không thể nhận dữ liệu tệp."));
        }
    }

    private static async Task HandleFileUploadCompleteAsync(ClientSession session, FileUploadCompleteRequest? req)
    {
        if (!session.IsAuthenticated || session.CurrentRoom == null || FileStore == null || req == null
            || !session.Uploads.Remove(req.UploadId, out var upload)) return;
        using (upload)
        {
            if (upload.Content.Length != upload.ExpectedSize)
            {
                await session.SendAsync("error", new ErrorResponse("Tệp tải lên chưa hoàn tất."));
                return;
            }
            upload.Content.Position = 0;
            var storedFile = await FileStore.SaveAsync(upload.FileName, upload.Content);
            var message = new ChatMessage(Guid.NewGuid().ToString(), session.CurrentRoom, session.Username,
                $"Đã gửi tệp: {upload.FileName}", DateTimeOffset.Now, upload.FileName, storedFile, upload.ExpectedSize,
                ThumbnailBase64: upload.ThumbnailBase64);
            if (MessageStore != null) await MessageStore.SaveAsync(message);
            await Rooms.BroadcastAsync(session.CurrentRoom, "chat", message);
        }
    }

    private static async Task HandleFileDownloadAsync(ClientSession session, FileDownloadRequest? req)
    {
        if (!session.IsAuthenticated || FileStore == null || string.IsNullOrWhiteSpace(req?.StoredFile)) return;
        await using var file = await FileStore.OpenAsync(req.StoredFile);
        if (file == null)
        {
            await session.SendAsync("error", new ErrorResponse("Không tìm thấy tệp trên máy chủ."));
            return;
        }
        if (file.Length > ChatLimits.MaxFileBytes)
        {
            await session.SendAsync("error", new ErrorResponse("Tệp vượt giới hạn tải xuống."));
            return;
        }

        var transferId = Guid.NewGuid().ToString();
        await session.SendAsync("file-download-start", new FileDownloadStart(transferId, file.FileInfo.Filename, file.Length));
        var buffer = new byte[ChatLimits.FileChunkBytes];
        var index = 0;
        int read;
        while ((read = await file.ReadAsync(buffer.AsMemory(0, buffer.Length))) > 0)
        {
            var base64 = Convert.ToBase64String(buffer, 0, read);
            await session.SendAsync("file-download-chunk", new FileDownloadChunk(transferId, index++, base64));
        }
        await session.SendAsync("file-download-complete", new FileDownloadComplete(transferId));
    }

    private static async Task HandleTypingAsync(ClientSession session, TypingRequest? req)
    {
        if (!session.IsAuthenticated || session.CurrentRoom == null) return;

        await Rooms.BroadcastAsync(session.CurrentRoom, "typing",
            new TypingNotice(session.CurrentRoom, session.Username, req?.IsTyping ?? false), except: session);
    }

    private static ClientSession? FindOnlineClient(string username)
    {
        lock (AllClientsLock)
        {
            return AllClients.FirstOrDefault(client => client.IsAuthenticated
                && string.Equals(client.Username, username, StringComparison.OrdinalIgnoreCase));
        }
    }

    private static async Task HandleDisconnectAsync(ClientSession session)
    {
        var room = session.CurrentRoom;
        Rooms.Leave(session);
        foreach (var upload in session.Uploads.Values) upload.Dispose();
        session.Uploads.Clear();
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

    private static async Task HandleRegisterAsync(ClientSession session, AuthRequest? req)
    {
        if (UserStore == null)
        {
            await session.SendAsync("error", new ErrorResponse("MongoDB chua san sang."));
            return;
        }

        var username = (req?.Username ?? "").Trim();
        var result = await UserStore.RegisterAsync(username, req?.Password ?? "");
        if (!result.Success)
        {
            await session.SendAsync("error", new ErrorResponse(result.Error));
            return;
        }

        session.Username = username;
        session.IsAuthenticated = true;
        await session.SendAsync("auth", new AuthResponse(username, result.Error));
    }

    private static async Task HandleLoginAsync(ClientSession session, AuthRequest? req)
    {
        if (UserStore == null)
        {
            await session.SendAsync("error", new ErrorResponse("MongoDB chua san sang."));
            return;
        }

        var username = (req?.Username ?? "").Trim();
        if (!await UserStore.ValidateLoginAsync(username, req?.Password ?? ""))
        {
            await session.SendAsync("error", new ErrorResponse("Username hoac mat khau khong dung."));
            return;
        }

        session.Username = username;
        session.IsAuthenticated = true;
        await session.SendAsync("auth", new AuthResponse(username, "Dang nhap thanh cong."));
    }

    private static async Task BroadcastRoomListToAllAsync()
    {
        
        var roomList = Rooms.GetRoomList();
        List<ClientSession> snapshot;
        lock (AllClientsLock) snapshot = AllClients.ToList();

        await Task.WhenAll(snapshot.Select(c => c.SendAsync("room-list", new RoomListResponse(roomList))));
    }
}
