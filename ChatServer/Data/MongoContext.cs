using MongoDB.Driver;

namespace ChatServer;

/// <summary>
/// Boc 1 ket noi MongoDB dung chung cho ca MongoUserStore va MongoMessageStore,
/// thay vi moi store tu mo 1 MongoClient rieng.
/// </summary>
public sealed class MongoContext
{
    public IMongoDatabase Database { get; }

    public MongoContext(string connectionString)
    {
        var url = new MongoUrl(connectionString);
        var databaseName = string.IsNullOrWhiteSpace(url.DatabaseName) ? "multiroom_chat" : url.DatabaseName;
        Database = new MongoClient(connectionString).GetDatabase(databaseName);
    }
}
