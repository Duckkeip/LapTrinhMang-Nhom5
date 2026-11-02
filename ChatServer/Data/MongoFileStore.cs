using MongoDB.Bson;
using MongoDB.Driver;
using MongoDB.Driver.GridFS;

namespace ChatServer;

/// <summary>Lưu file vào GridFS của MongoDB; dữ liệu được chia chunk bởi MongoDB thay vì nhét vào document thường.</summary>
public sealed class MongoFileStore
{
    private readonly GridFSBucket _bucket;

    public MongoFileStore(IMongoDatabase database) => _bucket = new GridFSBucket(database, new GridFSBucketOptions
    {
        BucketName = "chat_files",
        ChunkSizeBytes = 255 * 1024
    });

    public async Task<string> SaveAsync(string fileName, Stream content)
    {
        var id = await _bucket.UploadFromStreamAsync(fileName, content);
        return id.ToString();
    }

    public async Task<GridFSDownloadStream?> OpenAsync(string storedFile)
    {
        if (!ObjectId.TryParse(storedFile, out var id)) return null;
        try
        {
            return await _bucket.OpenDownloadStreamAsync(id);
        }
        catch (GridFSFileNotFoundException)
        {
            return null;
        }
    }
}
