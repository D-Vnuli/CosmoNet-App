using System.Net.Http;
using System.Net.Http.Json;

namespace CosmoNet.App.Services;

public sealed class FeedbackApiClient
{
    private readonly HttpClient _httpClient = new()
    {
        Timeout = TimeSpan.FromSeconds(20)
    };

    public async Task SendAsync(
        string apiBaseUrl,
        string name,
        string contacts,
        string message,
        CancellationToken cancellationToken = default)
    {
        var baseUri = ParseBaseUri(apiBaseUrl);
        using var response = await _httpClient.PostAsJsonAsync(
            new Uri(baseUri, "api/app/feedback"),
            new
            {
                name = name.Trim(),
                contacts = contacts.Trim(),
                message = message.Trim()
            },
            cancellationToken);

        if (!response.IsSuccessStatusCode)
        {
            throw new InvalidOperationException("Сервис обратной связи недоступен.");
        }
    }

    private static Uri ParseBaseUri(string apiBaseUrl)
    {
        if (!Uri.TryCreate(apiBaseUrl.Trim(), UriKind.Absolute, out var uri))
        {
            throw new InvalidOperationException("Некорректный адрес сервиса обратной связи.");
        }

        var builder = new UriBuilder(uri);
        if (!builder.Path.EndsWith('/'))
        {
            builder.Path += "/";
        }

        return builder.Uri;
    }
}