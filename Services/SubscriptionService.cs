using System.Globalization;
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
        var result = await LoadSubscriptionAsync(subscriptionUrl, cancellationToken);
        return result.Profiles;
    }

    public async Task<SubscriptionLoadResult> LoadSubscriptionAsync(
        string subscriptionUrl,
        CancellationToken cancellationToken = default)
    {
        var source = subscriptionUrl.Trim();
        if (source.StartsWith("vless://", StringComparison.OrdinalIgnoreCase))
        {
            return new SubscriptionLoadResult
            {
                Profiles = ParseSubscription(source),
                Summary = new SubscriptionSummary
                {
                    TariffName = "CosmoNet",
                    LastSyncedAt = DateTimeOffset.Now
                }
            };
        }

        if (!Uri.TryCreate(source, UriKind.Absolute, out var uri) ||
            (uri.Scheme != Uri.UriSchemeHttp && uri.Scheme != Uri.UriSchemeHttps))
        {
            throw new InvalidOperationException("Введите ссылку подписки HTTPS или конфигурацию vless://.");
        }

        using var response = await _httpClient.GetAsync(uri, cancellationToken);
        response.EnsureSuccessStatusCode();

        var raw = await response.Content.ReadAsStringAsync(cancellationToken);
        var profiles = ParseSubscription(raw);
        var summary = ParseSubscriptionSummary(response);

        return new SubscriptionLoadResult
        {
            Profiles = profiles,
            Summary = summary
        };
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

    private static SubscriptionSummary ParseSubscriptionSummary(HttpResponseMessage response)
    {
        var summary = new SubscriptionSummary
        {
            TariffName = ReadHeader(response, "profile-title") ?? "CosmoNet",
            LastSyncedAt = DateTimeOffset.Now
        };

        var userInfo = ReadHeader(response, "subscription-userinfo");
        if (string.IsNullOrWhiteSpace(userInfo))
        {
            return summary;
        }

        var values = userInfo
            .Split(';', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Select(part => part.Split('=', 2, StringSplitOptions.TrimEntries))
            .Where(parts => parts.Length == 2)
            .ToDictionary(parts => parts[0], parts => parts[1], StringComparer.OrdinalIgnoreCase);

        var upload = ReadLong(values, "upload");
        var download = ReadLong(values, "download");
        var total = ReadLong(values, "total");
        var expire = ReadLong(values, "expire");

        summary.TrafficUsedBytes = Math.Max(0, upload) + Math.Max(0, download);
        summary.TrafficLimitBytes = Math.Max(0, total);

        if (expire > 0)
        {
            summary.ExpiresAt = DateTimeOffset.FromUnixTimeSeconds(expire);
        }

        summary.Status = ResolveStatus(summary.ExpiresAt);
        return summary;
    }

    private static SubscriptionStatus ResolveStatus(DateTimeOffset? expiresAt)
    {
        if (expiresAt is null)
        {
            return SubscriptionStatus.Unknown;
        }

        var now = DateTimeOffset.Now;
        if (expiresAt <= now)
        {
            return SubscriptionStatus.Expired;
        }

        return expiresAt <= now.AddDays(3)
            ? SubscriptionStatus.ExpiringSoon
            : SubscriptionStatus.Active;
    }

    private static string? ReadHeader(HttpResponseMessage response, string name)
    {
        if (response.Headers.TryGetValues(name, out var headerValues))
        {
            return WebUtility.UrlDecode(headerValues.FirstOrDefault());
        }

        return response.Content.Headers.TryGetValues(name, out var contentValues)
            ? WebUtility.UrlDecode(contentValues.FirstOrDefault())
            : null;
    }

    private static long ReadLong(IReadOnlyDictionary<string, string> values, string key)
    {
        return values.TryGetValue(key, out var value) && long.TryParse(value, NumberStyles.Integer, CultureInfo.InvariantCulture, out var parsed)
            ? parsed
            : 0;
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
