using System.Security.Cryptography;
using MongoDB.Bson;
using MongoDB.Bson.Serialization.Attributes;
using MongoDB.Driver;
using ChatProtocol;

namespace ChatServer;

public sealed class MongoUserStore
{
    private const int SaltSize = 16;
    private const int HashSize = 32;
    private const int Iterations = 100_000;
    private readonly IMongoCollection<UserAccount> _users;

    public MongoUserStore(IMongoDatabase database)
    {
        _users = database.GetCollection<UserAccount>("users");
    }

    public async Task InitializeAsync()
    {
        var keys = Builders<UserAccount>.IndexKeys.Ascending(user => user.Username);
        var usernameFilter = new BsonDocumentFilterDefinition<UserAccount>(
            new BsonDocument("Username", new BsonDocument("$type", "string")));
        await _users.Indexes.CreateOneAsync(new CreateIndexModel<UserAccount>(keys, new CreateIndexOptions<UserAccount>
        {
            Unique = true,
            PartialFilterExpression = usernameFilter
        }));

        var emailKeys = Builders<UserAccount>.IndexKeys.Ascending(user => user.Email);
        var emailFilter = new BsonDocumentFilterDefinition<UserAccount>(
            new BsonDocument("Email", new BsonDocument("$type", "string")));
        await _users.Indexes.CreateOneAsync(new CreateIndexModel<UserAccount>(emailKeys, new CreateIndexOptions<UserAccount>
        {
            Unique = true,
            PartialFilterExpression = emailFilter
        }));

        // Index for sessions
        var sessionKeys = Builders<UserAccount>.IndexKeys.Ascending(user => user.Sessions);
        await _users.Indexes.CreateOneAsync(new CreateIndexModel<UserAccount>(sessionKeys));

        // Index for friend system
        var friendsKeys = Builders<UserAccount>.IndexKeys.Ascending(user => user.Friends);
        await _users.Indexes.CreateOneAsync(new CreateIndexModel<UserAccount>(friendsKeys));

        var pendingSentKeys = Builders<UserAccount>.IndexKeys.Ascending(user => user.PendingSentRequests);
        await _users.Indexes.CreateOneAsync(new CreateIndexModel<UserAccount>(pendingSentKeys));

        var pendingReceivedKeys = Builders<UserAccount>.IndexKeys.Ascending(user => user.PendingReceivedRequests);
        await _users.Indexes.CreateOneAsync(new CreateIndexModel<UserAccount>(pendingReceivedKeys));
    }

    public async Task<(bool Success, string Error)> RegisterAsync(string username, string password, string email)
    {
        if (username.Length < 3) return (false, "Username phai co it nhat 3 ky tu.");
        if (password.Length < 6) return (false, "Mat khau phai co it nhat 6 ky tu.");
        if (!email.Contains('@') || !email.Contains('.')) return (false, "Email khong hop le.");

        if (await _users.Find(user => user.Username == username).AnyAsync())
            return (false, "Username da ton tai.");
        if (await _users.Find(user => user.Email == email).AnyAsync())
            return (false, "Email da duoc su dung.");

        var salt = RandomNumberGenerator.GetBytes(SaltSize);
        var hash = HashPassword(password, salt);
        try
        {
            await _users.InsertOneAsync(new UserAccount
            {
                Username = username,
                Email = email,
                PasswordSalt = Convert.ToBase64String(salt),
                PasswordHash = Convert.ToBase64String(hash),
                CreatedAt = DateTime.UtcNow
            });
            return (true, "Dang ky thanh cong.");
        }
        catch (MongoWriteException ex) when (ex.WriteError?.Category == ServerErrorCategory.DuplicateKey)
        {
            return (false, "Username da ton tai.");
        }
    }

    public async Task<bool> ValidateLoginAsync(string username, string password)
    {
        var user = await _users.Find(account => account.Username == username).FirstOrDefaultAsync();
        if (user == null) return false;

        var salt = Convert.FromBase64String(user.PasswordSalt);
        var expectedHash = Convert.FromBase64String(user.PasswordHash);
        var actualHash = HashPassword(password, salt);
        return CryptographicOperations.FixedTimeEquals(actualHash, expectedHash);
    }

    public async Task<UserProfile?> GetProfileAsync(string username, bool isOnline)
    {
        var user = await _users.Find(account => account.Username == username).FirstOrDefaultAsync();
        return user == null
            ? null
            : new UserProfile(user.Username, new DateTimeOffset(DateTime.SpecifyKind(user.CreatedAt, DateTimeKind.Utc)), isOnline,
                user.AvatarBase64, user.DisplayName, user.Email, user.Bio, user.Status,
                user.AllowDirectMessages, user.ShowAvatar, user.ShowOnlineStatus, user.BlockedUsers,
                user.Theme, user.AccentColor, user.ChatFontSize, user.ShowAvatarsInChat, user.ShowImagePreviews,
                user.MessageSound, user.MentionNotifications, user.DirectMessageNotifications, user.OnlineNotifications,user.Friends,               
                user.PendingSentRequests,        
                user.PendingReceivedRequests );
    }
    public async Task<UserProfile?> GetProfileWithFriendsAsync(string username, bool isOnline)
    {
        var user = await _users.Find(account => account.Username == username).FirstOrDefaultAsync();
        if (user == null) return null;

        // Build FriendInfo list từ Friends array
        var friends = new List<FriendInfo>();
        if (user.Friends?.Count > 0)
        {
            var friendUsers = await _users.Find(account => user.Friends.Contains(account.Username)).ToListAsync();
            var onlineUsernames = new HashSet<string>(); // Sẽ được fill từ server memory
            
            foreach (var friend in friendUsers)
            {
                friends.Add(new FriendInfo(
                    friend.Username,
                    friend.DisplayName,
                    friend.AvatarBase64,
                    isOnline, // TODO: truyền online status thực tế
                    new DateTimeOffset(DateTime.SpecifyKind(friend.CreatedAt, DateTimeKind.Utc)) // approx
                ));
            }
        }

        return new UserProfile(
            user.Username,
            new DateTimeOffset(DateTime.SpecifyKind(user.CreatedAt, DateTimeKind.Utc)),
            isOnline,
            user.AvatarBase64,
            user.DisplayName,
            user.Email,
            user.Bio,
            user.Status,
            user.AllowDirectMessages,
            user.ShowAvatar,
            user.ShowOnlineStatus,
            user.BlockedUsers,
            user.Theme,
            user.AccentColor,
            user.ChatFontSize,
            user.ShowAvatarsInChat,
            user.ShowImagePreviews,
            user.MessageSound,
            user.MentionNotifications,
            user.DirectMessageNotifications,
            user.OnlineNotifications,
            user.Friends,                    // Friends list
            user.PendingSentRequests,        // Pending sent
            user.PendingReceivedRequests     // Pending received
        );
    }
    public async Task<string?> GetEmailAsync(string username)
    {
        var user = await _users.Find(account => account.Username == username).FirstOrDefaultAsync();
        return user?.Email;
    }

    public async Task<string?> GetUsernameByEmailAsync(string email)
    {
        var user = await _users.Find(account => account.Email.ToLower() == email.ToLower()).FirstOrDefaultAsync();
        return user?.Username;
    }

    public async Task<(bool Success, string Error)> ChangePasswordAfterOtpAsync(string username, string newPassword)
    {
        if (newPassword.Length < 6) return (false, "Mat khau moi phai co it nhat 6 ky tu.");

        var user = await _users.Find(account => account.Username == username).FirstOrDefaultAsync();
        if (user == null) return (false, "Khong tim thay tai khoan.");

        var salt = RandomNumberGenerator.GetBytes(SaltSize);
        var update = Builders<UserAccount>.Update
            .Set(account => account.PasswordSalt, Convert.ToBase64String(salt))
            .Set(account => account.PasswordHash, Convert.ToBase64String(HashPassword(newPassword, salt)));
        await _users.UpdateOneAsync(account => account.Id == user.Id, update);
        return (true, "Doi mat khau thanh cong.");
    }

    public async Task<bool> UpdateAvatarAsync(string username, string? avatarBase64)
    {
        var update = Builders<UserAccount>.Update.Set(account => account.AvatarBase64, avatarBase64);
        var result = await _users.UpdateOneAsync(account => account.Username == username, update);
        return result.ModifiedCount > 0 || result.MatchedCount > 0;
    }

    public async Task<(bool Success, string Error)> UpdateProfileAsync(string username, string displayName, string email, string bio, string status)
    {
        displayName = displayName.Trim();
        email = email.Trim();
        bio = bio.Trim();
        status = status.Trim().ToLowerInvariant();

        if (displayName.Length < 2 || displayName.Length > 32) return (false, "Tên hiển thị phải từ 2 đến 32 ký tự.");
        if (!email.Contains('@') || !email.Contains('.')) return (false, "Email không hợp lệ.");
        if (bio.Length > 160) return (false, "Tiểu sử tối đa 160 ký tự.");
        if (status is not ("online" or "busy" or "hidden")) return (false, "Trạng thái không hợp lệ.");

        var emailOwner = await _users.Find(account => account.Email == email && account.Username != username).AnyAsync();
        if (emailOwner) return (false, "Email đã được sử dụng.");

        var update = Builders<UserAccount>.Update
            .Set(account => account.DisplayName, displayName)
            .Set(account => account.Email, email)
            .Set(account => account.Bio, bio)
            .Set(account => account.Status, status);
        var result = await _users.UpdateOneAsync(account => account.Username == username, update);
        return result.MatchedCount > 0
            ? (true, "Cập nhật thông tin cá nhân thành công.")
            : (false, "Không tìm thấy tài khoản.");
    }

    public async Task<bool> UpdatePrivacyAsync(string username, bool allowDirectMessages, bool showAvatar, bool showOnlineStatus)
    {
        var update = Builders<UserAccount>.Update
            .Set(account => account.AllowDirectMessages, allowDirectMessages)
            .Set(account => account.ShowAvatar, showAvatar)
            .Set(account => account.ShowOnlineStatus, showOnlineStatus);
        var result = await _users.UpdateOneAsync(account => account.Username == username, update);
        return result.MatchedCount > 0;
    }

    public async Task<bool> UpdateAppearanceAsync(string username, UpdateAppearanceRequest request)
    {
        var theme = request.Theme.Trim().ToLowerInvariant();
        if (theme is not ("dark" or "light" or "system")) return false;
        if (request.ChatFontSize is < 9 or > 18) return false;

        var update = Builders<UserAccount>.Update
            .Set(account => account.Theme, theme)
            .Set(account => account.AccentColor, request.AccentColor)
            .Set(account => account.ChatFontSize, request.ChatFontSize)
            .Set(account => account.ShowAvatarsInChat, request.ShowAvatarsInChat)
            .Set(account => account.ShowImagePreviews, request.ShowImagePreviews)
            .Set(account => account.MessageSound, request.MessageSound)
            .Set(account => account.MentionNotifications, request.MentionNotifications)
            .Set(account => account.DirectMessageNotifications, request.DirectMessageNotifications)
            .Set(account => account.OnlineNotifications, request.OnlineNotifications);
        var result = await _users.UpdateOneAsync(account => account.Username == username, update);
        return result.MatchedCount > 0;
    }

    public async Task<bool> SetBlockedUserAsync(string username, string blockedUsername, bool blocked)
    {
        var update = blocked
            ? Builders<UserAccount>.Update.AddToSet(account => account.BlockedUsers, blockedUsername)
            : Builders<UserAccount>.Update.Pull(account => account.BlockedUsers, blockedUsername);
        var result = await _users.UpdateOneAsync(account => account.Username == username, update);
        return result.MatchedCount > 0;
    }

    public Task<bool> ExistsAsync(string username) => _users.Find(account => account.Username == username).AnyAsync();

    // Session management
    public async Task AddSessionAsync(string username, UserSession session)
    {
        var update = Builders<UserAccount>.Update
            .Push(account => account.Sessions, session)
            .Set(account => account.LastLoginAt, DateTime.UtcNow)
            .Set(account => account.LastLoginIp, session.IpAddress)
            .Set(account => account.LastLoginDevice, session.DeviceInfo);
        await _users.UpdateOneAsync(account => account.Username == username, update);
    }

    public async Task RemoveSessionAsync(string username, string sessionId)
    {
        var update = Builders<UserAccount>.Update.PullFilter(account => account.Sessions, s => s.SessionId == sessionId);
        await _users.UpdateOneAsync(account => account.Username == username, update);
    }

    public async Task RemoveAllSessionsAsync(string username, string? exceptSessionId = null)
    {
        var filter = Builders<UserAccount>.Filter.Eq(account => account.Username, username);
        var user = await _users.Find(filter).FirstOrDefaultAsync();
        if (user == null) return;

        if (exceptSessionId != null)
        {
            var sessionsToKeep = user.Sessions.Where(s => s.SessionId == exceptSessionId).ToList();
            var update = Builders<UserAccount>.Update.Set(account => account.Sessions, sessionsToKeep);
            await _users.UpdateOneAsync(filter, update);
        }
        else
        {
            var update = Builders<UserAccount>.Update.Set(account => account.Sessions, new List<UserSession>());
            await _users.UpdateOneAsync(filter, update);
        }
    }

    public async Task<List<UserSession>> GetSessionsAsync(string username)
    {
        var user = await _users.Find(account => account.Username == username).FirstOrDefaultAsync();
        return user?.Sessions ?? new List<UserSession>();
    }

    public async Task UpdateSessionActivityAsync(string username, string sessionId)
    {
        var filter = Builders<UserAccount>.Filter.And(
            Builders<UserAccount>.Filter.Eq(account => account.Username, username),
            Builders<UserAccount>.Filter.ElemMatch(account => account.Sessions, s => s.SessionId == sessionId));
        var update = Builders<UserAccount>.Update.Set("Sessions.$.LastActive", DateTime.UtcNow);
        await _users.UpdateOneAsync(filter, update);
    }

    // Two-factor authentication
    public async Task<(bool Success, string Error, string? Secret, List<string>? BackupCodes)> SetupTwoFactorAsync(string username)
    {
        var user = await _users.Find(account => account.Username == username).FirstOrDefaultAsync();
        if (user == null) return (false, "Không tìm thấy tài khoản.", null, null);

        if (user.TwoFactorEnabled) return (false, "Xác thực hai bước đã được bật.", null, null);

        // Generate secret key (base32 encoded)
        var secretBytes = RandomNumberGenerator.GetBytes(20);
        var secret = Base32Encode(secretBytes);

        // Generate backup codes
        var backupCodes = new List<string>();
        for (int i = 0; i < 10; i++)
        {
            backupCodes.Add(GenerateBackupCode());
        }

        var update = Builders<UserAccount>.Update
            .Set(account => account.TwoFactorSecret, secret)
            .Set(account => account.TwoFactorBackupCodes, backupCodes);
        await _users.UpdateOneAsync(account => account.Username == username, update);

        return (true, "Đã tạo khóa bí mật. Quét mã QR để thiết lập.", secret, backupCodes);
    }

    public async Task<(bool Success, string Message, List<string>? BackupCodes)> EnableTwoFactorAsync(string username, string code)
    {
        var user = await _users.Find(account => account.Username == username).FirstOrDefaultAsync();
        if (user == null) return (false, "Không tìm thấy tài khoản.", null);

        if (string.IsNullOrEmpty(user.TwoFactorSecret)) return (false, "Chưa thiết lập xác thực hai bước.", null);

        if (!VerifyTotp(user.TwoFactorSecret, code)) return (false, "Mã xác thực không đúng.", null);

        var update = Builders<UserAccount>.Update
            .Set(account => account.TwoFactorEnabled, true);
        await _users.UpdateOneAsync(account => account.Username == username, update);

        return (true, "Đã bật xác thực hai bước.", user.TwoFactorBackupCodes);
    }

    public async Task<(bool Success, string Message)> DisableTwoFactorAsync(string username, string code)
    {
        var user = await _users.Find(account => account.Username == username).FirstOrDefaultAsync();
        if (user == null) return (false, "Không tìm thấy tài khoản.");

        if (!user.TwoFactorEnabled) return (false, "Xác thực hai bước chưa được bật.");

        if (!VerifyTotp(user.TwoFactorSecret!, code) && !user.TwoFactorBackupCodes!.Contains(code))
            return (false, "Mã xác thực không đúng.");

        var update = Builders<UserAccount>.Update
            .Set(account => account.TwoFactorEnabled, false)
            .Set(account => account.TwoFactorSecret, "")
            .Set(account => account.TwoFactorBackupCodes, new List<string>());
        await _users.UpdateOneAsync(account => account.Username == username, update);

        return (true, "Đã tắt xác thực hai bước.");
    }

    public async Task<bool> IsTwoFactorEnabledAsync(string username)
    {
        var user = await _users.Find(account => account.Username == username).FirstOrDefaultAsync();
        return user?.TwoFactorEnabled ?? false;
    }

    public async Task<string?> GetTwoFactorSecretAsync(string username)
    {
        var user = await _users.Find(account => account.Username == username).FirstOrDefaultAsync();
        return user?.TwoFactorSecret;
    }

    public async Task<bool> VerifyTwoFactorAsync(string username, string code)
    {
        var user = await _users.Find(account => account.Username == username).FirstOrDefaultAsync();
        if (user == null || !user.TwoFactorEnabled) return false;

        if (VerifyTotp(user.TwoFactorSecret!, code)) return true;

        // Check backup codes
        if (user.TwoFactorBackupCodes?.Contains(code) == true)
        {
            // Remove used backup code
            var update = Builders<UserAccount>.Update.Pull(account => account.TwoFactorBackupCodes, code);
            await _users.UpdateOneAsync(account => account.Username == username, update);
            return true;
        }

        return false;
    }

    // Login history
    public async Task AddLoginHistoryAsync(string username, LoginHistoryEntry entry)
    {
        var update = Builders<UserAccount>.Update
            .Push(account => account.LoginHistory, entry)
            .Set(account => account.LastLoginAt, entry.LoginTime)
            .Set(account => account.LastLoginIp, entry.IpAddress)
            .Set(account => account.LastLoginDevice, entry.DeviceInfo);
        await _users.UpdateOneAsync(account => account.Username == username, update);
    }

    public async Task<List<LoginHistoryEntry>> GetLoginHistoryAsync(string username, int limit = 50)
    {
        var user = await _users.Find(account => account.Username == username).FirstOrDefaultAsync();
        if (user == null) return new List<LoginHistoryEntry>();

        return user.LoginHistory
            .OrderByDescending(e => e.LoginTime)
            .Take(limit)
            .ToList();
    }
    // ============ FRIEND SYSTEM METHODS ============

    public async Task<(bool Success, string Error)> SendFriendRequestAsync(string from, string to)
    {
        if (string.Equals(from, to, StringComparison.OrdinalIgnoreCase))
            return (false, "Không thể kết bạn với chính mình.");

        var fromUser = await _users.Find(u => u.Username == from).FirstOrDefaultAsync();
        var toUser = await _users.Find(u => u.Username == to).FirstOrDefaultAsync();
        
        if (fromUser == null || toUser == null)
            return (false, "Người dùng không tồn tại.");

        // Kiểm tra đã là bạn
        if (fromUser.Friends.Contains(to))
            return (false, "Đã là bạn bè.");

        // Kiểm tra đã gửi lời mời
        if (fromUser.PendingSentRequests.Contains(to))
            return (false, "Đã gửi lời mời kết bạn.");

        // Kiểm tra đã nhận lời mời từ đối phương
        if (fromUser.PendingReceivedRequests.Contains(to))
            return (false, "Đối phương đã gửi lời mời cho bạn. Hãy chấp nhận.");

        // Kiểm tra block
        if (fromUser.BlockedUsers.Contains(to) || toUser.BlockedUsers.Contains(from))
            return (false, "Không thể kết bạn do cài đặt quyền riêng tư.");

        // Thêm vào pending
        var fromUpdate = Builders<UserAccount>.Update
            .AddToSet(u => u.PendingSentRequests, to);
        await _users.UpdateOneAsync(u => u.Username == from, fromUpdate);

        var toUpdate = Builders<UserAccount>.Update
            .AddToSet(u => u.PendingReceivedRequests, from);
        await _users.UpdateOneAsync(u => u.Username == to, toUpdate);

        return (true, "Đã gửi lời mời kết bạn.");
    }

    public async Task<(bool Success, string Error)> RespondFriendRequestAsync(string user, string from, bool accept)
    {
        var userDoc = await _users.Find(u => u.Username == user).FirstOrDefaultAsync();
        var fromDoc = await _users.Find(u => u.Username == from).FirstOrDefaultAsync();
        
        if (userDoc == null || fromDoc == null)
            return (false, "Người dùng không tồn tại.");

        // Kiểm tra lời mời tồn tại
        if (!userDoc.PendingReceivedRequests.Contains(from))
            return (false, "Không tìm thấy lời mời kết bạn.");

        // Xóa pending cả 2 bên
        var userUpdate = Builders<UserAccount>.Update
            .Pull(u => u.PendingReceivedRequests, from);
        await _users.UpdateOneAsync(u => u.Username == user, userUpdate);

        var fromUpdate = Builders<UserAccount>.Update
            .Pull(u => u.PendingSentRequests, user);
        await _users.UpdateOneAsync(u => u.Username == from, fromUpdate);

        if (accept)
        {
            // Thêm vào friends cả 2 bên
            var addFriendUpdate = Builders<UserAccount>.Update
                .AddToSet(u => u.Friends, from);
            await _users.UpdateOneAsync(u => u.Username == user, addFriendUpdate);

            var addFriendUpdate2 = Builders<UserAccount>.Update
                .AddToSet(u => u.Friends, user);
            await _users.UpdateOneAsync(u => u.Username == from, addFriendUpdate2);

            return (true, "Đã chấp nhận lời mời kết bạn.");
        }

        return (true, "Đã từ chối lời mời kết bạn.");
    }

    public async Task<(bool Success, string Error)> RemoveFriendAsync(string user, string friend)
    {
        var userDoc = await _users.Find(u => u.Username == user).FirstOrDefaultAsync();
        if (userDoc == null) return (false, "Không tìm thấy tài khoản.");

        if (!userDoc.Friends.Contains(friend))
            return (false, "Không phải bạn bè.");

        // Xóa khỏi friends cả 2 bên
        var update = Builders<UserAccount>.Update.Pull(u => u.Friends, friend);
        await _users.UpdateOneAsync(u => u.Username == user, update);
        await _users.UpdateOneAsync(u => u.Username == friend, update);

        return (true, "Đã xóa bạn bè.");
    }

    public async Task<List<FriendInfo>> GetFriendsAsync(string username)
    {
        var user = await _users.Find(u => u.Username == username).FirstOrDefaultAsync();
        if (user == null || user.Friends == null || user.Friends.Count == 0)
            return new List<FriendInfo>();

        var friendUsers = await _users.Find(u => user.Friends.Contains(u.Username)).ToListAsync();
        
        return friendUsers.Select(f => new FriendInfo(
            f.Username,
            f.DisplayName,
            f.AvatarBase64,
            false, // TODO: check online status từ server memory
            new DateTimeOffset(DateTime.SpecifyKind(f.CreatedAt, DateTimeKind.Utc))
        )).ToList();
    }

    public async Task<List<FriendRequestInfo>> GetPendingRequestsAsync(string username, bool sent)
    {
        var user = await _users.Find(u => u.Username == username).FirstOrDefaultAsync();
        if (user == null) return new List<FriendRequestInfo>();

        var field = sent ? user.PendingSentRequests : user.PendingReceivedRequests;
        if (field == null || field.Count == 0) return new List<FriendRequestInfo>();

        var requestUsers = await _users.Find(u => field.Contains(u.Username)).ToListAsync();
        
        return requestUsers.Select(f => new FriendRequestInfo(
            f.Username,
            f.DisplayName,
            f.AvatarBase64,
            new DateTimeOffset(DateTime.SpecifyKind(f.CreatedAt, DateTimeKind.Utc))
        )).ToList();
    }

    public async Task<bool> AreFriendsAsync(string user1, string user2)
    {
        var user = await _users.Find(u => u.Username == user1).FirstOrDefaultAsync();
        return user?.Friends?.Contains(user2) ?? false;
    }
    public async Task<DateTimeOffset?> GetLastLoginAsync(string username)
    {
        var user = await _users.Find(account => account.Username == username).FirstOrDefaultAsync();
        return user?.LastLoginAt != null ? new DateTimeOffset(DateTime.SpecifyKind(user.LastLoginAt.Value, DateTimeKind.Utc)) : null;
    }

    private static byte[] HashPassword(string password, byte[] salt) =>
        Rfc2898DeriveBytes.Pbkdf2(password, salt, Iterations, HashAlgorithmName.SHA256, HashSize);

    private static string Base32Encode(byte[] data)
    {
        const string alphabet = "ABCDEFGHIJKLMNOPQRSTUVWXYZ234567";
        var bits = new System.Collections.BitArray(data);
        var result = new System.Text.StringBuilder();
        int bitIndex = 0;
        while (bitIndex < bits.Length)
        {
            int value = 0;
            for (int i = 0; i < 5; i++)
            {
                if (bitIndex < bits.Length && bits[bitIndex++]) value |= 1 << (4 - i);
            }
            result.Append(alphabet[value]);
        }
        return result.ToString();
    }

    private static string GenerateBackupCode()
    {
        var bytes = RandomNumberGenerator.GetBytes(4);
        return Convert.ToHexString(bytes).ToUpper();
    }

    private static bool VerifyTotp(string secret, string code)
    {
        if (code.Length != 6 || !int.TryParse(code, out _)) return false;

        var key = Base32Decode(secret);
        var timeStep = DateTimeOffset.UtcNow.ToUnixTimeSeconds() / 30;
        var counter = BitConverter.GetBytes(timeStep);
        if (BitConverter.IsLittleEndian) Array.Reverse(counter);

        using var hmac = new HMACSHA1(key);
        var hash = hmac.ComputeHash(counter);
        var offset = hash[^1] & 0x0F;
        var binary = ((hash[offset] & 0x7F) << 24) | (hash[offset + 1] << 16) | (hash[offset + 2] << 8) | hash[offset + 3];
        var otp = binary % 1000000;
        return otp.ToString("D6") == code;
    }

    private static byte[] Base32Decode(string input)
    {
        const string alphabet = "ABCDEFGHIJKLMNOPQRSTUVWXYZ234567";
        var bits = new System.Collections.BitArray(input.Length * 5);
        int bitIndex = 0;
        foreach (var c in input.ToUpperInvariant())
        {
            var value = alphabet.IndexOf(c);
            if (value < 0) continue;
            for (int i = 4; i >= 0; i--)
            {
                bits[bitIndex++] = (value & (1 << i)) != 0;
            }
        }
        var bytes = new byte[(bitIndex + 7) / 8];
        bits.CopyTo(bytes, 0);
        return bytes;
    }
}

public sealed class UserAccount
{
    [BsonId]
    public ObjectId Id { get; set; }
    public string Username { get; set; } = "";
    public string DisplayName { get; set; } = "";
    public string Email { get; set; } = "";
    public string Bio { get; set; } = "";
    public string Status { get; set; } = "online";
    public bool AllowDirectMessages { get; set; } = true;
    public bool ShowAvatar { get; set; } = true;
    public bool ShowOnlineStatus { get; set; } = true;
    public List<string> BlockedUsers { get; set; } = new();
    public string Theme { get; set; } = "dark";
    public string AccentColor { get; set; } = "#7467F0";
    public float ChatFontSize { get; set; } = 10F;
    public bool ShowAvatarsInChat { get; set; } = true;
    public bool ShowImagePreviews { get; set; } = true;
    public bool MessageSound { get; set; } = true;
    public bool MentionNotifications { get; set; } = true;
    public bool DirectMessageNotifications { get; set; } = true;
    public bool OnlineNotifications { get; set; }
    public string PasswordHash { get; set; } = "";
    public string PasswordSalt { get; set; } = "";
    public DateTime CreatedAt { get; set; }
    public string? AvatarBase64 { get; set; }

    // Session tracking
    public List<UserSession> Sessions { get; set; } = new();
    public DateTime? LastLoginAt { get; set; }
    public string? LastLoginIp { get; set; }
    public string? LastLoginDevice { get; set; }

    // Two-factor authentication
    public bool TwoFactorEnabled { get; set; }
    public string? TwoFactorSecret { get; set; }
    public List<string> TwoFactorBackupCodes { get; set; } = new();

    // Login history
    public List<LoginHistoryEntry> LoginHistory { get; set; } = new();

    // Friend system
    public List<string> Friends { get; set; } = new();                    // danh sách bạn bè
    public List<string> PendingSentRequests { get; set; } = new();       // lời mời đã gửi
    public List<string> PendingReceivedRequests { get; set; } = new();   // lời mời nhận được
}

public sealed class UserSession
{
    public string SessionId { get; set; } = Guid.NewGuid().ToString();
    public string DeviceInfo { get; set; } = "";
    public string IpAddress { get; set; } = "";
    public DateTime LoginTime { get; set; } = DateTime.UtcNow;
    public DateTime LastActive { get; set; } = DateTime.UtcNow;
}

public sealed class LoginHistoryEntry
{
    public DateTime LoginTime { get; set; } = DateTime.UtcNow;
    public string IpAddress { get; set; } = "";
    public string DeviceInfo { get; set; } = "";
    public bool Success { get; set; }
}