using MongoDB.Bson;
using MongoDB.Bson.Serialization.Attributes;
using MongoDB.Driver;

namespace ChatServer;

/// <summary>Audit trail for voice participation. Active presence stays in memory.</summary>
public sealed class MongoVoiceSessionStore
{
    private readonly IMongoCollection<VoiceSessionDocument> _sessions;

    public MongoVoiceSessionStore(IMongoDatabase database) =>
        _sessions = database.GetCollection<VoiceSessionDocument>("voice_sessions");

    public Task InitializeAsync() => _sessions.Indexes.CreateOneAsync(new CreateIndexModel<VoiceSessionDocument>(
        Builders<VoiceSessionDocument>.IndexKeys.Ascending(x => x.ChannelId).Descending(x => x.JoinedAt)));

    public Task StartAsync(string sessionId, string channelId, string username, DateTime joinedAt) =>
        _sessions.InsertOneAsync(new VoiceSessionDocument
        {
            ConnectionSessionId = sessionId,
            ChannelId = channelId,
            Username = username,
            JoinedAt = joinedAt
        });

    public Task UpdateDeviceStateAsync(string sessionId, bool microphoneEnabled, bool cameraEnabled) =>
        _sessions.UpdateOneAsync(x => x.ConnectionSessionId == sessionId && x.LeftAt == null,
            Builders<VoiceSessionDocument>.Update
                .Set(x => x.MicrophoneEnabled, microphoneEnabled)
                .Set(x => x.CameraEnabled, cameraEnabled));

    public Task EndAsync(string sessionId, DateTime leftAt) =>
        _sessions.UpdateOneAsync(x => x.ConnectionSessionId == sessionId && x.LeftAt == null,
            Builders<VoiceSessionDocument>.Update.Set(x => x.LeftAt, leftAt));
}

[BsonIgnoreExtraElements]
public sealed class VoiceSessionDocument
{
    [BsonId, BsonRepresentation(BsonType.ObjectId)] public string Id { get; set; } = ObjectId.GenerateNewId().ToString();
    public string ConnectionSessionId { get; set; } = "";
    public string ChannelId { get; set; } = "";
    public string Username { get; set; } = "";
    public DateTime JoinedAt { get; set; }
    public DateTime? LeftAt { get; set; }
    public bool MicrophoneEnabled { get; set; }
    public bool CameraEnabled { get; set; }
}
