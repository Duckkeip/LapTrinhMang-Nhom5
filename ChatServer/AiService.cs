using System.Net.Http.Json;

namespace ChatServer;

public sealed class AiService
{
    private readonly HttpClient _httpClient = new() { Timeout = TimeSpan.FromSeconds(30) };
    private readonly string _baseUrl;

    public AiService(string? serviceUrl)
    {
        _baseUrl = (serviceUrl ?? "").TrimEnd('/');
    }

    public async Task<string> GenerateAsync(string prompt, string room, string username)
    {
        if (_baseUrl.Length == 0) return "AI_SERVICE_URL chua duoc cau hinh.";

        using var response = await _httpClient.PostAsJsonAsync($"{_baseUrl}/generate", new
        {
            prompt,
            room,
            username
        });

        if (!response.IsSuccessStatusCode)
            return $"AI service loi HTTP {(int)response.StatusCode}.";

        var result = await response.Content.ReadFromJsonAsync<AiResponse>();
        return string.IsNullOrWhiteSpace(result?.Reply) ? "AI khong tra ve noi dung." : result.Reply;
    }

    private sealed class AiResponse
    {
        public string? Reply { get; set; }
    }
}