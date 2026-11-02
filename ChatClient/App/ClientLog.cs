namespace ChatClient;

internal static class ClientLog
{
    private static readonly object Sync = new();
    private static readonly string LogDirectory = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "RECHAT");
    private static readonly string LogPath = Path.Combine(LogDirectory, "client.log");

    public static string FilePath => LogPath;

    public static void Info(string message) => Write("INFO", message);

    public static void Error(string message, Exception? exception = null)
    {
        var detail = exception == null ? message : $"{message} | {exception}";
        Write("ERROR", detail);
    }

    private static void Write(string level, string message)
    {
        try
        {
            lock (Sync)
            {
                Directory.CreateDirectory(LogDirectory);
                File.AppendAllText(LogPath, $"{DateTime.Now:yyyy-MM-dd HH:mm:ss.fff} [{level}] {message}{Environment.NewLine}");
            }
        }
        catch
        {
        }
    }
}
