using MongoDB.Bson;
using MongoDB.Bson.Serialization.Attributes;
using MongoDB.Driver;
using ChatProtocol;

namespace ChatServer;

/// <summary>Hàng đợi DM offline; một bản ghi chỉ được lấy một lần khi người nhận đăng nhập.</summary>
public sealed class MongoDirectMessageStore
{
    private readonly IMongoCollection<OfflineDirectMessageDocument> _messages;

    public MongoDirectMessageStore(IMongoDatabase database) => _messages = database.GetCollection<OfflineDirectMessageDocument>("offline_direct_messages");

    public async Task InitializeAsync()
    {
        var keys = Builders<OfflineDirectMessageDocument>.IndexKeys.Ascending(message => message.To).Ascending(message => message.Time);
        await _messages.Indexes.CreateOneAsync(new CreateIndexModel<OfflineDirectMessageDocument>(keys));
    }

    public Task SaveAsync(DirectMessage message) => _messages.InsertOneAsync(new OfflineDirectMessageDocument
    {
        MessageId = message.Id, From = message.From, To = message.To, Text = message.Text, Time = message.Time.UtcDateTime
    });

    public async Task<List<DirectMessage>> TakeUnreadAsync(string username)
    {
        var filter = Builders<OfflineDirectMessageDocument>.Filter.Eq(message => message.To, username);
        var messages = await _messages.Find(filter).SortBy(message => message.Time).ToListAsync();
        if (messages.Count > 0) await _messages.DeleteManyAsync(filter);
        return messages.Select(message => new DirectMessage(message.MessageId, message.From, message.To, message.Text,
            new DateTimeOffset(DateTime.SpecifyKind(message.Time, DateTimeKind.Utc)))).ToList();
    }
}

[BsonIgnoreExtraElements]
public sealed class OfflineDirectMessageDocument
{
    [BsonId] public ObjectId Id { get; set; }
    public string MessageId { get; set; } = "";
    public string From { get; set; } = "";
    public string To { get; set; } = "";
    public string Text { get; set; } = "";
    public DateTime Time { get; set; }
}
