using System.Net.Http;
using System.Net.Http.Json;
using CosmoNet.App.Models;

namespace CosmoNet.App.Services;

public sealed class SubscriptionMetadataApiClient
{
    private readonly HttpClient _httpClient = new()
    {
        Timeout = TimeSpan.FromSeconds(10)
    };

    public async Task<XuiSubscriptionMetadata> GetAsync(
        string apiBaseUrl,
        string? subscriptionId,
        string? clientId,
        CancellationToken cancellationToken = default)
    {
        var parameters = new List<string>();
        if (!string.IsNullOrWhiteSpace(subscriptionId))
        {
            parameters.Add($"subId={Uri.EscapeDataString(subscriptionId)}");
        }
        if (!string.IsNullOrWhiteSpace(clientId))
        {
            parameters.Add($"clientId={Uri.EscapeDataString(clientId)}");
        }
        if (parameters.Count == 0)
        {
            throw new InvalidOperationException("Subscription identifier is missing.");
        }

        var requestUri = new Uri(ParseBaseUri(apiBaseUrl),
            $"api/app/subscription/device-limit?{string.Join("&", parameters)}");
        using var response = await _httpClient.GetAsync(requestUri, cancellationToken);
        response.EnsureSuccessStatusCode();

        var result = await response.Content.ReadFromJsonAsync<MetadataResponse>(cancellationToken: cancellationToken)
            ?? throw new InvalidOperationException("Empty 3X-UI response.");
        if (result.DeviceLimit < 0)
        {
            throw new InvalidOperationException("Invalid device limit.");
        }

        return new XuiSubscriptionMetadata
        {
            DeviceLimit = result.DeviceLimit,
            IsEnabled = result.IsEnabled,
            ExpiresAt = result.ExpiresAtUnixMilliseconds is { } timestamp
                ? DateTimeOffset.FromUnixTimeMilliseconds(timestamp)
                : null
        };
    }

    private static Uri ParseBaseUri(string apiBaseUrl)
    {
        if (!Uri.TryCreate(apiBaseUrl.Trim(), UriKind.Absolute, out var uri))
        {
            throw new InvalidOperationException("Invalid 3X-UI URL.");
        }
        var builder = new UriBuilder(uri);
        if (!builder.Path.EndsWith('/'))
        {
            builder.Path += "/";
        }
        return builder.Uri;
    }

    private sealed class MetadataResponse
    {
        public int DeviceLimit { get; set; }
        public bool IsEnabled { get; set; }
        public long? ExpiresAtUnixMilliseconds { get; set; }
    }
}