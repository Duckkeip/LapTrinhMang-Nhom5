using System.Security.Cryptography;
using MongoDB.Bson;
using MongoDB.Bson.Serialization.Attributes;
using MongoDB.Driver;
using ChatProtocol;

namespace ChatServer;

/// <summary>
/// Persistent data for RE:CHAT communities.  Membership is stored separately
/// from users so a user can join any number of servers without growing the
/// user document indefinitely.
/// </summary>
public sealed class MongoServerStore
{
    private const int MaxServerNameLength = 80;
    private const int MaxDescriptionLength = 250;
    private const int MaxChannelNameLength = 64;
    private readonly IMongoCollection<ChatServerDocument> _servers;
    private readonly IMongoCollection<ServerMemberDocument> _members;
    private readonly IMongoCollection<ServerChannelDocument> _channels;
    private readonly IMongoCollection<ServerInviteDocument> _invites;

    public MongoServerStore(IMongoDatabase database)
    {
        _servers = database.GetCollection<ChatServerDocument>("chat_servers");
        _members = database.GetCollection<ServerMemberDocument>("server_members");
        _channels = database.GetCollection<ServerChannelDocument>("server_channels");
        _invites = database.GetCollection<ServerInviteDocument>("server_invites");
    }

    public async Task InitializeAsync()
    {
        await _members.Indexes.CreateOneAsync(new CreateIndexModel<ServerMemberDocument>(
            Builders<ServerMemberDocument>.IndexKeys.Ascending(x => x.ServerId).Ascending(x => x.Username),
            new CreateIndexOptions { Unique = true }));
        await _members.Indexes.CreateOneAsync(new CreateIndexModel<ServerMemberDocument>(
            Builders<ServerMemberDocument>.IndexKeys.Ascending(x => x.Username)));
        await _channels.Indexes.CreateOneAsync(new CreateIndexModel<ServerChannelDocument>(
            Builders<ServerChannelDocument>.IndexKeys
                .Ascending(x => x.ServerId)
                .Ascending(x => x.Type)
                .Ascending(x => x.Position)));
        await _invites.Indexes.CreateOneAsync(new CreateIndexModel<ServerInviteDocument>(
            Builders<ServerInviteDocument>.IndexKeys.Ascending(x => x.Code), new CreateIndexOptions { Unique = true }));
        await _invites.Indexes.CreateOneAsync(new CreateIndexModel<ServerInviteDocument>(
            Builders<ServerInviteDocument>.IndexKeys.Ascending(x => x.ExpiresAt),
            new CreateIndexOptions { ExpireAfter = TimeSpan.Zero }));
    }

    public async Task<(bool Success, string Error, ServerSummary? Server, List<ServerChannelInfo>? Channels)> CreateAsync(
        string ownerUsername, string name, string? description)
    {
        name = name.Trim();
        description = string.IsNullOrWhiteSpace(description) ? null : description.Trim();
        if (!IsValidName(name, MaxServerNameLength)) return (false, $"Tên server phải có từ 2 đến {MaxServerNameLength} ký tự.", null, null);
        if (description?.Length > MaxDescriptionLength) return (false, $"Mô tả server tối đa {MaxDescriptionLength} ký tự.", null, null);

        var now = DateTime.UtcNow;
        var server = new ChatServerDocument { Name = name, Description = description, OwnerUsername = ownerUsername, CreatedAt = now };
        await _servers.InsertOneAsync(server);
        var member = new ServerMemberDocument { ServerId = server.Id, Username = ownerUsername, Role = ServerRole.Owner, JoinedAt = now };
        var defaultTextChannel = new ServerChannelDocument { ServerId = server.Id, Name = "general", Type = ServerChannelType.Text, Position = 0, CreatedAt = now };
        var defaultVoiceChannel = new ServerChannelDocument { ServerId = server.Id, Name = "Phòng Chung", Type = ServerChannelType.Voice, Position = 0, CreatedAt = now };
        try
        {
            await _members.InsertOneAsync(member);
            await _channels.InsertManyAsync(new[] { defaultTextChannel, defaultVoiceChannel });
        }
        catch
        {
            await _servers.DeleteOneAsync(x => x.Id == server.Id);
            throw;
        }

        return (true, "Đã tạo server.", ToSummary(server, ServerRole.Owner, 1), new List<ServerChannelInfo>
        {
            ToChannel(defaultTextChannel),
            ToChannel(defaultVoiceChannel)
        });
    }

    public async Task<List<ServerSummary>> GetForUserAsync(string username)
    {
        var memberships = await _members.Find(x => x.Username == username).ToListAsync();
        if (memberships.Count == 0) return [];
        var serverIds = memberships.Select(x => x.ServerId).ToList();
        var servers = await _servers.Find(x => serverIds.Contains(x.Id)).ToListAsync();
        var memberCounts = await _members.Aggregate()
            .Match(x => serverIds.Contains(x.ServerId))
            .Group(x => x.ServerId, group => new { ServerId = group.Key, Count = group.Count() })
            .ToListAsync();
        var counts = memberCounts.ToDictionary(x => x.ServerId, x => x.Count);
        var roles = memberships.ToDictionary(x => x.ServerId, x => x.Role);
        return servers.OrderBy(x => x.Name).Select(x => ToSummary(x, roles[x.Id], counts.GetValueOrDefault(x.Id))).ToList();
    }

    public async Task<List<ServerChannelInfo>?> GetChannelsAsync(string username, string serverId)
    {
        if (!await IsMemberAsync(username, serverId)) return null;
        var channels = await _channels.Find(x => x.ServerId == serverId).SortBy(x => x.Position).ToListAsync();
        return channels.Select(ToChannel).ToList();
    }

    public async Task<bool> CanJoinVoiceChannelAsync(string username, string channelId)
    {
        var channel = await _channels.Find(x => x.Id == channelId && x.Type == ServerChannelType.Voice).FirstOrDefaultAsync();
        return channel != null && await IsMemberAsync(username, channel.ServerId);
    }

    public async Task<(bool Success, string Error, ServerChannelInfo? Channel)> CreateChannelAsync(
        string username, string serverId, string name, ServerChannelType type)
    {
        name = name.Trim();
        if (!IsValidName(name, MaxChannelNameLength)) return (false, $"Tên kênh phải có từ 2 đến {MaxChannelNameLength} ký tự.", null);
        if (!await HasManagementPermissionAsync(username, serverId)) return (false, "Bạn không có quyền tạo kênh trong server này.", null);
        var last = await _channels
            .Find(x => x.ServerId == serverId && x.Type == type)
            .SortByDescending(x => x.Position)
            .FirstOrDefaultAsync();
        var channel = new ServerChannelDocument { ServerId = serverId, Name = name, Type = type, Position = last?.Position + 1 ?? 0, CreatedAt = DateTime.UtcNow };
        await _channels.InsertOneAsync(channel);
        return (true, "Đã tạo kênh.", ToChannel(channel));
    }

    public async Task<(bool Success, string Error, ServerInviteInfo? Invite)> CreateInviteAsync(
        string username, string serverId, int? maxUses, int? expiresInMinutes)
    {
        if (!await HasManagementPermissionAsync(username, serverId)) return (false, "Bạn không có quyền tạo link mời cho server này.", null);
        if (maxUses is <= 0 or > 10_000) maxUses = null;
        if (expiresInMinutes is <= 0 or > 43_200) return (false, "Thời hạn link mời phải từ 1 phút đến 30 ngày.", null);
        var invite = new ServerInviteDocument
        {
            ServerId = serverId,
            Code = GenerateInviteCode(),
            CreatedBy = username,
            MaxUses = maxUses,
            ExpiresAt = expiresInMinutes is null ? null : DateTime.UtcNow.AddMinutes(expiresInMinutes.Value),
            CreatedAt = DateTime.UtcNow
        };
        await _invites.InsertOneAsync(invite);
        return (true, "Đã tạo link mời.", ToInvite(invite));
    }

    public async Task<(bool Success, string Error, ServerSummary? Server, List<ServerChannelInfo>? Channels)> AcceptInviteAsync(string username, string code)
    {
        code = code.Trim();
        var invite = await _invites.Find(x => x.Code == code).FirstOrDefaultAsync();
        if (invite == null || invite.ExpiresAt <= DateTime.UtcNow) return (false, "Link mời không hợp lệ hoặc đã hết hạn.", null, null);
        if (await IsMemberAsync(username, invite.ServerId)) return (false, "Bạn đã là thành viên của server này.", null, null);

        var availability = Builders<ServerInviteDocument>.Filter.And(
            Builders<ServerInviteDocument>.Filter.Eq(x => x.Id, invite.Id),
            invite.MaxUses is null ? Builders<ServerInviteDocument>.Filter.Empty : Builders<ServerInviteDocument>.Filter.Lt(x => x.UsedCount, invite.MaxUses.Value));
        if ((await _invites.UpdateOneAsync(availability, Builders<ServerInviteDocument>.Update.Inc(x => x.UsedCount, 1))).ModifiedCount != 1)
            return (false, "Link mời đã dùng hết lượt.", null, null);

        try { await _members.InsertOneAsync(new ServerMemberDocument { ServerId = invite.ServerId, Username = username, Role = ServerRole.Member, JoinedAt = DateTime.UtcNow }); }
        catch (MongoWriteException ex) when (ex.WriteError?.Category == ServerErrorCategory.DuplicateKey)
        {
            return (false, "Bạn đã là thành viên của server này.", null, null);
        }
        var server = await _servers.Find(x => x.Id == invite.ServerId).FirstOrDefaultAsync();
        if (server == null) return (false, "Server này không còn tồn tại.", null, null);
        var count = await _members.CountDocumentsAsync(x => x.ServerId == server.Id);
        var channels = await _channels.Find(x => x.ServerId == server.Id).SortBy(x => x.Position).ToListAsync();
        return (true, "Đã tham gia server.", ToSummary(server, ServerRole.Member, (int)count), channels.Select(ToChannel).ToList());
    }

    private Task<bool> IsMemberAsync(string username, string serverId) => _members.Find(x => x.Username == username && x.ServerId == serverId).AnyAsync();
    private async Task<bool> HasManagementPermissionAsync(string username, string serverId)
    {
        var membership = await _members.Find(x => x.Username == username && x.ServerId == serverId).FirstOrDefaultAsync();
        return membership?.Role is ServerRole.Owner or ServerRole.Admin;
    }
    private static bool IsValidName(string value, int maxLength) => value.Length is >= 2 && value.Length <= maxLength && value.All(x => !char.IsControl(x));
    private static string GenerateInviteCode() => Convert.ToHexString(RandomNumberGenerator.GetBytes(6)).ToLowerInvariant();
    private static ServerSummary ToSummary(ChatServerDocument server, ServerRole role, int memberCount) => new(server.Id, server.Name, server.Description, server.OwnerUsername, role, memberCount, new DateTimeOffset(DateTime.SpecifyKind(server.CreatedAt, DateTimeKind.Utc)));
    private static ServerChannelInfo ToChannel(ServerChannelDocument channel) => new(channel.Id, channel.ServerId, channel.Name, channel.Type, channel.Position);
    private static ServerInviteInfo ToInvite(ServerInviteDocument invite) => new(invite.Code, invite.ServerId, invite.MaxUses, invite.UsedCount, invite.ExpiresAt is null ? null : new DateTimeOffset(DateTime.SpecifyKind(invite.ExpiresAt.Value, DateTimeKind.Utc)));
}

[BsonIgnoreExtraElements]
public sealed class ChatServerDocument
{
    [BsonId, BsonRepresentation(BsonType.ObjectId)] public string Id { get; set; } = ObjectId.GenerateNewId().ToString();
    public string Name { get; set; } = "";
    public string? Description { get; set; }
    public string OwnerUsername { get; set; } = "";
    public DateTime CreatedAt { get; set; }
}

[BsonIgnoreExtraElements]
public sealed class ServerMemberDocument
{
    [BsonId, BsonRepresentation(BsonType.ObjectId)] public string Id { get; set; } = ObjectId.GenerateNewId().ToString();
    public string ServerId { get; set; } = "";
    public string Username { get; set; } = "";
    [BsonRepresentation(BsonType.String)] public ServerRole Role { get; set; }
    public DateTime JoinedAt { get; set; }
}

[BsonIgnoreExtraElements]
public sealed class ServerChannelDocument
{
    [BsonId, BsonRepresentation(BsonType.ObjectId)] public string Id { get; set; } = ObjectId.GenerateNewId().ToString();
    public string ServerId { get; set; } = "";
    public string Name { get; set; } = "";
    [BsonRepresentation(BsonType.String)] public ServerChannelType Type { get; set; }
    public int Position { get; set; }
    public DateTime CreatedAt { get; set; }
}

[BsonIgnoreExtraElements]
public sealed class ServerInviteDocument
{
    [BsonId, BsonRepresentation(BsonType.ObjectId)] public string Id { get; set; } = ObjectId.GenerateNewId().ToString();
    public string ServerId { get; set; } = "";
    public string Code { get; set; } = "";
    public string CreatedBy { get; set; } = "";
    public int? MaxUses { get; set; }
    public int UsedCount { get; set; }
    public DateTime? ExpiresAt { get; set; }
    public DateTime CreatedAt { get; set; }
}
