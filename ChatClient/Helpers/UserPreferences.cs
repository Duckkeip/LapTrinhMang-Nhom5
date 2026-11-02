using System.Text.Json;

namespace ChatClient;

internal sealed class UserPreferences
{
    public string Theme { get; set; } = "dark";
    public string AccentColor { get; set; } = "#7467F0";
    public float ChatFontSize { get; set; } = 10F;
    public bool ShowAvatarsInChat { get; set; } = true;
    public bool ShowImagePreviews { get; set; } = true;
    public bool MessageSound { get; set; } = true;
    public bool MentionNotifications { get; set; } = true;
    public bool DirectMessageNotifications { get; set; } = true;
    public bool OnlineNotifications { get; set; } = false;

    private static readonly string FilePath = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "ChatTcpWinForms",
        "settings.json");

    public static UserPreferences Load()
    {
        try
        {
            if (File.Exists(FilePath))
            {
                var preferences = JsonSerializer.Deserialize<UserPreferences>(File.ReadAllText(FilePath));
                if (preferences != null) return preferences;
            }
        }
        catch
        {
            // Use defaults when the local settings file is missing or invalid.
        }

        return new UserPreferences();
    }

    public void Save()
    {
        Directory.CreateDirectory(Path.GetDirectoryName(FilePath)!);
        File.WriteAllText(FilePath, JsonSerializer.Serialize(this, new JsonSerializerOptions { WriteIndented = true }));
    }
}
