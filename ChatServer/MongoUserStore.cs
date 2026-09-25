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
    }

    public async Task<(bool Success, string Error)> RegisterAsync(string username, string password)
    {
        if (username.Length < 3) return (false, "Username phai co it nhat 3 ky tu.");
        if (password.Length < 6) return (false, "Mat khau phai co it nhat 6 ky tu.");

        if (await _users.Find(user => user.Username == username).AnyAsync())
            return (false, "Username da ton tai.");

        var salt = RandomNumberGenerator.GetBytes(SaltSize);
        var hash = HashPassword(password, salt);
        try
        {
            await _users.InsertOneAsync(new UserAccount
            {
                Username = username,
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
            : new UserProfile(user.Username, new DateTimeOffset(DateTime.SpecifyKind(user.CreatedAt, DateTimeKind.Utc)), isOnline);
    }

    public Task<bool> ExistsAsync(string username) => _users.Find(account => account.Username == username).AnyAsync();

    private static byte[] HashPassword(string password, byte[] salt) =>
        Rfc2898DeriveBytes.Pbkdf2(password, salt, Iterations, HashAlgorithmName.SHA256, HashSize);
}

public sealed class UserAccount
{
    [BsonId]
    public ObjectId Id { get; set; }
    public string Username { get; set; } = "";
    public string PasswordHash { get; set; } = "";
    public string PasswordSalt { get; set; } = "";
    public DateTime CreatedAt { get; set; }
}
