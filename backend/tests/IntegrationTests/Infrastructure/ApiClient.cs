using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;

namespace ComicPlatform.IntegrationTests.Infrastructure;

/// <summary>Thin JSON client that tracks the bearer token, refresh cookie and selected house.</summary>
public sealed class ApiClient(HttpClient http)
{
    public HttpClient Http { get; } = http;
    public string? Token { get; set; }
    public string? RefreshCookie { get; set; }
    public Guid? HouseId { get; set; }
    public Guid UserId { get; set; }
    public string Username { get; set; } = "";
    public string UserAgent { get; set; } = "Mozilla/5.0 (X11; Linux x86_64) AppleWebKit/537.36 (KHTML, like Gecko) Chrome/140.0 Safari/537.36";

    public async Task<HttpResponseMessage> SendAsync(HttpMethod method, string url, object? body = null, Action<HttpRequestMessage>? configure = null)
    {
        using var request = new HttpRequestMessage(method, url);
        if (body is HttpContent content)
        {
            request.Content = content;
        }
        else if (body is not null)
        {
            request.Content = JsonContent.Create(body);
        }

        if (Token is not null)
        {
            request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", Token);
        }

        if (HouseId is not null)
        {
            request.Headers.Add("X-House-Id", HouseId.Value.ToString());
        }

        if (RefreshCookie is not null)
        {
            request.Headers.Add("Cookie", $"cp_rt={RefreshCookie}");
        }

        // Like a browser: the view verification treats requests without a real user agent as bots.
        request.Headers.TryAddWithoutValidation("User-Agent", UserAgent);
        configure?.Invoke(request);
        var response = await Http.SendAsync(request);
        CaptureRefreshCookie(response);
        return response;
    }

    public async Task<JsonElement> JsonAsync(HttpMethod method, string url, object? body = null, HttpStatusCode expected = HttpStatusCode.OK)
    {
        using var response = await SendAsync(method, url, body);
        var text = await response.Content.ReadAsStringAsync();
        if (response.StatusCode != expected)
        {
            throw new Xunit.Sdk.XunitException($"{method} {url}: expected {(int)expected} but got {(int)response.StatusCode}: {text}");
        }

        return text.Length == 0 ? default : JsonDocument.Parse(text).RootElement.Clone();
    }

    public Task<JsonElement> GetAsync(string url, HttpStatusCode expected = HttpStatusCode.OK) => JsonAsync(HttpMethod.Get, url, null, expected);
    public Task<JsonElement> PostAsync(string url, object? body = null, HttpStatusCode expected = HttpStatusCode.OK) => JsonAsync(HttpMethod.Post, url, body ?? new { }, expected);
    public Task<JsonElement> PutAsync(string url, object? body = null, HttpStatusCode expected = HttpStatusCode.OK) => JsonAsync(HttpMethod.Put, url, body ?? new { }, expected);
    public Task<JsonElement> DeleteAsync(string url, HttpStatusCode expected = HttpStatusCode.NoContent) => JsonAsync(HttpMethod.Delete, url, null, expected);

    /// <summary>Asserts a ProblemDetails error and returns its code.</summary>
    public async Task<string> ProblemAsync(HttpMethod method, string url, object? body, HttpStatusCode expected)
    {
        using var response = await SendAsync(method, url, body);
        var text = await response.Content.ReadAsStringAsync();
        Assert.True(response.StatusCode == expected, $"{method} {url}: expected {(int)expected} but got {(int)response.StatusCode}: {text}");
        return text.Length == 0 ? "" : JsonDocument.Parse(text).RootElement.TryGetProperty("code", out var code) ? code.GetString() ?? "" : "";
    }

    public async Task<HttpResponseMessage> UploadAsync(string url, byte[] data, string fileName)
    {
        var form = new MultipartFormDataContent();
        var file = new ByteArrayContent(data);
        file.Headers.ContentType = new MediaTypeHeaderValue("application/zip");
        form.Add(file, "file", fileName);
        return await SendAsync(HttpMethod.Post, url, form);
    }

    private void CaptureRefreshCookie(HttpResponseMessage response)
    {
        if (!response.Headers.TryGetValues("Set-Cookie", out var cookies))
        {
            return;
        }

        foreach (var cookie in cookies)
        {
            if (cookie.StartsWith("cp_rt=", StringComparison.Ordinal))
            {
                var value = cookie["cp_rt=".Length..].Split(';')[0];
                RefreshCookie = value.Length == 0 ? null : value;
            }
        }
    }
}

public static class JsonExtensions
{
    public static string Str(this JsonElement e, string name) => e.GetProperty(name).GetString()!;

    public static Guid Id(this JsonElement e, string name = "id") => e.GetProperty(name).GetGuid();

    public static int Int(this JsonElement e, string name) => e.GetProperty(name).GetInt32();

    public static IEnumerable<JsonElement> Items(this JsonElement e) =>
        e.ValueKind == JsonValueKind.Array ? e.EnumerateArray() : e.GetProperty("items").EnumerateArray();
}
