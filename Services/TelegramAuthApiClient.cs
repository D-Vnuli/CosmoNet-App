using System.Net.Http;
using System.Net.Http.Json;
using CosmoNet.App.Models;

namespace CosmoNet.App.Services;

public sealed class TelegramAuthApiClient
{
    private readonly HttpClient _httpClient = new()
    {
        Timeout = TimeSpan.FromSeconds(20)
    };

    public async Task<TelegramAuthStartResult> StartAsync(
        string apiBaseUrl,
        string deviceId,
        CancellationToken cancellationToken = default)
    {
        var baseUri = ParseBaseUri(apiBaseUrl);
        using var response = await _httpClient.PostAsJsonAsync(
            new Uri(baseUri, "api/app/auth/start"),
            new
            {
                deviceId,
                deviceName = Environment.MachineName,
                platform = "windows"
            },
            cancellationToken);

        response.EnsureSuccessStatusCode();
        return await response.Content.ReadFromJsonAsync<TelegramAuthStartResult>(cancellationToken: cancellationToken)
            ?? throw new InvalidOperationException("Пустой ответ сервера авторизации.");
    }

    public async Task<TelegramAuthStatusResult> GetStatusAsync(
        string apiBaseUrl,
        string deviceId,
        string loginCode,
        CancellationToken cancellationToken = default)
    {
        var baseUri = ParseBaseUri(apiBaseUrl);
        var url = new Uri(
            baseUri,
            $"api/app/auth/status?deviceId={Uri.EscapeDataString(deviceId)}&code={Uri.EscapeDataString(loginCode)}");

        using var response = await _httpClient.GetAsync(url, cancellationToken);
        response.EnsureSuccessStatusCode();
        return await response.Content.ReadFromJsonAsync<TelegramAuthStatusResult>(cancellationToken: cancellationToken)
            ?? throw new InvalidOperationException("Пустой ответ статуса авторизации.");
    }

    private static Uri ParseBaseUri(string apiBaseUrl)
    {
        if (!Uri.TryCreate(apiBaseUrl.Trim(), UriKind.Absolute, out var uri))
        {
            throw new InvalidOperationException("Введите корректный backend URL авторизации.");
        }

        var builder = new UriBuilder(uri);
        if (!builder.Path.EndsWith('/'))
        {
            builder.Path += "/";
        }

        return builder.Uri;
    }
}

