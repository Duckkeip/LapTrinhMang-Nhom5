using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace ChatServer;

public sealed class OtpJwtService
{
    private const string Header = "{\"alg\":\"HS256\",\"typ\":\"JWT\"}";
    private readonly byte[] _secret;
    string secret = Environment.GetEnvironmentVariable("JWT_SECRET");
    public OtpJwtService(string secret)
    {
        if (string.IsNullOrWhiteSpace(secret))
            throw new InvalidOperationException("Thieu JWT_SECRET trong .env.");

        _secret = Encoding.UTF8.GetBytes(secret);
    }

    public string Create(string username, string email, string otp, DateTime expiresAtUtc)
    {
        var encodedHeader = Base64UrlEncode(Encoding.UTF8.GetBytes(Header));
        var payload = JsonSerializer.SerializeToUtf8Bytes(new
        {
            sub = username,
            email,
            otp,
            exp = new DateTimeOffset(expiresAtUtc).ToUnixTimeSeconds()
        });
        var encodedPayload = Base64UrlEncode(payload);
        var unsignedToken = $"{encodedHeader}.{encodedPayload}";
        return $"{unsignedToken}.{Sign(unsignedToken)}";
    }

    public bool TryValidate(string token, string otp, out OtpJwtClaims? claims)
    {
        claims = null;
        var parts = token.Split('.');
        if (parts.Length != 3) return false;

        var expectedSignature = Encoding.UTF8.GetBytes(Sign($"{parts[0]}.{parts[1]}"));
        var actualSignature = Encoding.UTF8.GetBytes(parts[2]);
        if (!CryptographicOperations.FixedTimeEquals(expectedSignature, actualSignature)) return false;

        try
        {
            var payload = JsonSerializer.Deserialize<OtpJwtPayload>(Base64UrlDecode(parts[1]));
            if (payload == null || string.IsNullOrWhiteSpace(payload.sub) ||
                string.IsNullOrWhiteSpace(payload.email) || string.IsNullOrWhiteSpace(payload.otp) ||
                payload.exp <= DateTimeOffset.UtcNow.ToUnixTimeSeconds()) return false;
            if (!CryptographicOperations.FixedTimeEquals(
                    Encoding.UTF8.GetBytes(payload.otp), Encoding.UTF8.GetBytes(otp))) return false;

            claims = new OtpJwtClaims(payload.sub, payload.email, DateTimeOffset.FromUnixTimeSeconds(payload.exp));
            return true;
        }
        catch (FormatException)
        {
            return false;
        }
        catch (JsonException)
        {
            return false;
        }
    }

    private string Sign(string value)
    {
        using var hmac = new HMACSHA256(_secret);
        return Base64UrlEncode(hmac.ComputeHash(Encoding.UTF8.GetBytes(value)));
    }

    private static string Base64UrlEncode(byte[] bytes) =>
        Convert.ToBase64String(bytes).TrimEnd('=').Replace('+', '-').Replace('/', '_');

    private static byte[] Base64UrlDecode(string value)
    {
        var padded = value.Replace('-', '+').Replace('_', '/');
        padded += new string('=', (4 - padded.Length % 4) % 4);
        return Convert.FromBase64String(padded);
    }

    private sealed record OtpJwtPayload(string sub, string email, string otp, long exp);
}

public sealed record OtpJwtClaims(string Username, string Email, DateTimeOffset ExpiresAt);