namespace ChatServer;

public static class EnvironmentConfig
{
    public static Dictionary<string, string> Load()
    {
        var values = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        var path = FindEnvFile();

        if (path != null)
        {
            foreach (var rawLine in File.ReadLines(path))
            {
                var line = rawLine.Trim();
                if (line.Length == 0 || line.StartsWith('#')) continue;

                var separator = line.IndexOf('=');
                if (separator <= 0) continue;

                var key = line[..separator].Trim();
                var value = line[(separator + 1)..].Trim().Trim('"', '\'');
                values[key] = NormalizeValue(value);
            }
        }

        var environmentKeys = new[]
        {
            "MONGODB_URI",
            "PORT",
            "EMAIL_SCRIPT_URL",
            "EMAIL_SCRIPT_SECRET",
            "RESEND_API_KEY",
            "EMAIL_FROM",
            "JWT_SECRET",
            "AI_SERVICE_URL",
            "LIVEKIT_URL",
            "LIVEKIT_API_KEY",
            "LIVEKIT_API_SECRET"
        };

        foreach (var key in environmentKeys)
        {
            var environmentValue = Environment.GetEnvironmentVariable(key);
            if (!string.IsNullOrWhiteSpace(environmentValue))
                values[key] = NormalizeValue(environmentValue);
        }

        return values;
    }

    private static string? FindEnvFile()
    {
        var candidates = new[]
        {
            Path.Combine(AppContext.BaseDirectory, ".env"),
            Path.Combine(Directory.GetCurrentDirectory(), ".env"),
            Path.Combine(AppContext.BaseDirectory, "..", "..", "..", ".env")
        };

        return candidates.Select(Path.GetFullPath).FirstOrDefault(File.Exists);
    }

    private static string NormalizeValue(string value)
    {
        if (value.StartsWith('[') && value.Contains("](") && value.EndsWith(')'))
        {
            var start = value.IndexOf("](", StringComparison.Ordinal) + 2;
            return value[start..^1];
        }

        return value;
    }
}