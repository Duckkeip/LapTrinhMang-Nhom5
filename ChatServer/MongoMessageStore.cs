using MongoDB.Bson;
using MongoDB.Bson.IO;
using MongoDB.Bson.Serialization.Attributes;
using MongoDB.Bson.Serialization;
using MongoDB.Bson.Serialization.Serializers;
using MongoDB.Driver;
using ChatProtocol;
using System.Globalization;

namespace ChatServer;

/// <summary>
/// Luu tin nhan chat vao collection "messages" va lay lai N tin gan nhat cua 1 phong
/// khi co client vao phong (thay vi chi giu trong RAM nhu truoc, mat het khi tat server).
/// </summary>
public sealed class MongoMessageStore
{
    private const int DefaultHistoryLimit = 50;
    private readonly IMongoCollection<ChatMessageDocument> _messages;

    public MongoMessageStore(IMongoDatabase database)
    {
        _messages = database.GetCollection<ChatMessageDocument>("messages");
    }

    public async Task InitializeAsync()
    {
        // Index theo (Room, Time) vi truy van pho bien nhat la "N tin gan nhat cua 1 phong".
        var keys = Builders<ChatMessageDocument>.IndexKeys
            .Ascending(m => m.Room)
            .Ascending(m => m.Time);
        await _messages.Indexes.CreateOneAsync(new CreateIndexModel<ChatMessageDocument>(keys));
    }

    public async Task SaveAsync(ChatMessage message)
    {
        await _messages.InsertOneAsync(new ChatMessageDocument
        {
            MessageId = message.Id,
            Room = message.Room,
            From = message.From,
            Text = message.Text,
            Time = message.Time.UtcDateTime,
            FileName = message.FileName,
            StoredFile = message.StoredFile,
            FileSize = message.FileSize,
            Reactions = message.Reactions,
            Edited = message.Edited,
            ThumbnailBase64 = message.ThumbnailBase64
        });
    }

    /// <summary>Lay toi da <paramref name="limit"/> tin gan nhat cua 1 phong, sap xep cu -> moi.</summary>
    public async Task<List<ChatMessage>> GetRecentAsync(string room, int limit = DefaultHistoryLimit)
    {
        var docs = await _messages.Find(m => m.Room == room)
            .SortByDescending(m => m.Time)
            .Limit(limit)
            .ToListAsync();

        docs.Reverse(); // dao lai thanh thu tu cu -> moi de hien thi tu tren xuong

        return docs.Select(d => new ChatMessage(
            d.MessageId,
            d.Room,
            d.From,
            d.Text,
            new DateTimeOffset(DateTime.SpecifyKind(d.Time, DateTimeKind.Utc)),
            d.FileName,
            d.StoredFile,
            d.FileSize,
            d.Reactions,
            d.Edited,
            d.ThumbnailBase64
        )).ToList();
    }
}

[BsonIgnoreExtraElements]
public sealed class ChatMessageDocument
{
    public string MessageId { get; set; } = "";
    public string Room { get; set; } = "";
    public string From { get; set; } = "";
    public string Text { get; set; } = "";
    [BsonSerializer(typeof(FlexibleDateTimeSerializer))]
    public DateTime Time { get; set; }
    public string? FileName { get; set; }
    public string? StoredFile { get; set; }
    public long FileSize { get; set; }
    public List<string>? Reactions { get; set; }
    public bool Edited { get; set; }
    public string? ThumbnailBase64 { get; set; }
}

internal sealed class FlexibleDateTimeSerializer : SerializerBase<DateTime>
{
    public override DateTime Deserialize(BsonDeserializationContext context, BsonDeserializationArgs args)
    {
        var reader = context.Reader;
        switch (reader.GetCurrentBsonType())
        {
            case BsonType.DateTime:
                return BsonUtils.ToDateTimeFromMillisecondsSinceEpoch(reader.ReadDateTime()).ToUniversalTime();

            case BsonType.Document:
                DateTime value = default;
                reader.ReadStartDocument();
                while (reader.ReadBsonType() != BsonType.EndOfDocument)
                {
                    var name = reader.ReadName(Utf8NameDecoder.Instance);
                    if (name == "DateTime" && reader.GetCurrentBsonType() == BsonType.DateTime)
                        value = BsonUtils.ToDateTimeFromMillisecondsSinceEpoch(reader.ReadDateTime()).ToUniversalTime();
                    else
                        reader.SkipValue();
                }
                reader.ReadEndDocument();
                return value;

            case BsonType.String:
                return DateTime.Parse(reader.ReadString(), null, DateTimeStyles.RoundtripKind).ToUniversalTime();

            case BsonType.Null:
                reader.ReadNull();
                return default;

            default:
                throw new FormatException($"Khong ho tro BSON time type: {reader.GetCurrentBsonType()}");
        }
    }

    public override void Serialize(BsonSerializationContext context, BsonSerializationArgs args, DateTime value)
    {
        context.Writer.WriteDateTime(BsonUtils.ToMillisecondsSinceEpoch(value.ToUniversalTime()));
    }
}
