using System.Net.Http;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using CosmoNet.App.Models;

namespace CosmoNet.App.Services;

public sealed class TelegramAuthApiClient
{
    private readonly HttpClient _httpClient = new() { Timeout = TimeSpan.FromSeconds(20) };

    public async Task<TelegramAuthStartResult> StartAsync(string baseUrl, string deviceId, CancellationToken ct = default)
    {
        using var response = await _httpClient.PostAsJsonAsync(new Uri(BaseUri(baseUrl), "api/app/auth/start"),
            new { deviceId, deviceName = Environment.MachineName, platform = "windows" }, ct);
        response.EnsureSuccessStatusCode();
        return await response.Content.ReadFromJsonAsync<TelegramAuthStartResult>(cancellationToken: ct)
            ?? throw new InvalidOperationException("Empty authorization response.");
    }

    public async Task<TelegramAuthStatusResult> GetStatusAsync(string baseUrl, string deviceId, string sessionId, CancellationToken ct = default)
    {
        var uri = new Uri(BaseUri(baseUrl), $"api/app/auth/status?deviceId={Uri.EscapeDataString(deviceId)}&sessionId={Uri.EscapeDataString(sessionId)}");
        using var response = await _httpClient.GetAsync(uri, ct);
        response.EnsureSuccessStatusCode();
        return await response.Content.ReadFromJsonAsync<TelegramAuthStatusResult>(cancellationToken: ct)
            ?? throw new InvalidOperationException("Empty authorization status.");
    }

    public async Task LogoutAsync(string baseUrl, string token, CancellationToken ct = default)
    {
        using var request = new HttpRequestMessage(HttpMethod.Post, new Uri(BaseUri(baseUrl), "api/app/auth/logout"));
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
        using var response = await _httpClient.SendAsync(request, ct);
        response.EnsureSuccessStatusCode();
    }

    private static Uri BaseUri(string value)
    {
        if (!Uri.TryCreate(value.Trim(), UriKind.Absolute, out var uri)) throw new InvalidOperationException("Authorization API is not configured.");
        var builder = new UriBuilder(uri);
        if (!builder.Path.EndsWith('/')) builder.Path += "/";
        return builder.Uri;
    }
}
