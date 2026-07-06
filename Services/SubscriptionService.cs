using System.Net;
using System.Net.Http;
using System.Text;
using CosmoNet.App.Models;

namespace CosmoNet.App.Services;

public sealed class SubscriptionService
{
    private readonly HttpClient _httpClient = new()
    {
        Timeout = TimeSpan.FromSeconds(20)
    };

    public async Task<IReadOnlyList<VpnProfile>> LoadProfilesAsync(
        string subscriptionUrl,
        CancellationToken cancellationToken = default)
    {
        if (!Uri.TryCreate(subscriptionUrl.Trim(), UriKind.Absolute, out var uri))
        {
            throw new InvalidOperationException("Введите корректную ссылку подписки CosmoNet.");
        }

        var raw = await _httpClient.GetStringAsync(uri, cancellationToken);
        return ParseSubscription(raw);
    }

    public IReadOnlyList<VpnProfile> ParseSubscription(string raw)
    {
        var text = DecodeSubscription(raw.Trim());
        var profiles = text
            .Split(new[] { '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Select(ParseProfile)
            .Where(profile => profile is not null)
            .Cast<VpnProfile>()
            .ToList();

        if (profiles.Count == 0)
        {
            throw new InvalidOperationException("В подписке не найдено поддерживаемых VLESS-профилей.");
        }

        return profiles;
    }

    private static string DecodeSubscription(string raw)
    {
        if (raw.Contains("://", StringComparison.Ordinal))
        {
            return raw;
        }

        try
        {
            var normalized = raw.Replace('-', '+').Replace('_', '/');
            var padding = normalized.Length % 4;

            if (padding > 0)
            {
                normalized = normalized.PadRight(normalized.Length + 4 - padding, '=');
            }

            return Encoding.UTF8.GetString(Convert.FromBase64String(normalized));
        }
        catch (FormatException)
        {
            return raw;
        }
    }

    private static VpnProfile? ParseProfile(string line)
    {
        if (!line.StartsWith("vless://", StringComparison.OrdinalIgnoreCase))
        {
            return null;
        }

        var uri = new Uri(line);
        var query = ParseQuery(uri.Query);

        return new VpnProfile
        {
            Name = WebUtility.UrlDecode(uri.Fragment.TrimStart('#')) switch
            {
                { Length: > 0 } value => value,
                _ => "CosmoNet"
            },
            Protocol = uri.Scheme,
            Uuid = uri.UserInfo,
            Server = uri.Host,
            Port = uri.Port,
            Query = query
        };
    }

    private static Dictionary<string, string> ParseQuery(string query)
    {
        return query
            .TrimStart('?')
            .Split('&', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Select(part => part.Split('=', 2))
            .Where(parts => parts.Length == 2)
            .ToDictionary(
                parts => WebUtility.UrlDecode(parts[0]),
                parts => WebUtility.UrlDecode(parts[1]),
                StringComparer.OrdinalIgnoreCase);
    }
}


