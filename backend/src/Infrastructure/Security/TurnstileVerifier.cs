using System.Net.Http.Json;
using ComicPlatform.Application.Analytics;
using ComicPlatform.Infrastructure.Configuration;
using Microsoft.Extensions.Logging;

namespace ComicPlatform.Infrastructure.Security;

/// <summary>Cloudflare Turnstile server-side validation (siteverify). Fails closed on invalid tokens.</summary>
public sealed class TurnstileVerifier(IHttpClientFactory http, ViewSettings settings, ILogger<TurnstileVerifier> logger) : IHumanVerifier
{
    private static readonly Uri Endpoint = new("https://challenges.cloudflare.com/turnstile/v0/siteverify");

    public bool IsEnabled => true;

    public async Task<bool> VerifyAsync(string? token, string ip, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(token) || token.Length > 2048)
        {
            return false;
        }

        try
        {
            using var content = new FormUrlEncodedContent(new Dictionary<string, string>
            {
                ["secret"] = settings.TurnstileSecretKey,
                ["response"] = token,
                ["remoteip"] = ip,
            });
            using var response = await http.CreateClient("turnstile").PostAsync(Endpoint, content, ct);
            var result = await response.Content.ReadFromJsonAsync<TurnstileResult>(ct);
            return result?.Success == true;
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException or System.Text.Json.JsonException)
        {
            // Turnstile outage: do not block readers, but the view still has to pass every other check.
            logger.LogWarning(ex, "Turnstile verification unavailable");
            return true;
        }
    }

    private sealed record TurnstileResult(bool Success);
}
