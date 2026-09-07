using System.Net;
using System.Net.Sockets;

namespace CosmoNet.App.Services;

internal static class SecurityPolicy
{
    private const string ApiHost = "api.cosmonet.shop";
    private const string XuiSubscriptionHost = "45.151.69.119";
    private const int XuiSubscriptionPort = 2096;
    private static readonly string[] PaymentHosts = ["yookassa.ru", "yoomoney.ru"];

    public static Uri RequireCosmoNetApi(string value)
    {
        var uri = RequireHttps(value, "адрес API");
        if (!uri.Host.Equals(ApiHost, StringComparison.OrdinalIgnoreCase) || uri.Port != 18443) throw new InvalidOperationException("Недоверенный адрес API CosmoNet.");
        return uri;
    }
    public static Uri RequireHttps(string value, string label)
    {
        if (!Uri.TryCreate(value?.Trim(), UriKind.Absolute, out var uri) || !uri.Scheme.Equals(Uri.UriSchemeHttps, StringComparison.OrdinalIgnoreCase) || string.IsNullOrWhiteSpace(uri.Host)) throw new InvalidOperationException($"Недопустимый {label}.");
        return uri;
    }
    public static Uri RequireSubscriptionUrl(string value)
    {
        if (!Uri.TryCreate(value?.Trim(), UriKind.Absolute, out var uri) || string.IsNullOrWhiteSpace(uri.Host))
        {
            throw new InvalidOperationException("Недопустимая ссылка подписки.");
        }

        if (uri.Scheme.Equals(Uri.UriSchemeHttps, StringComparison.OrdinalIgnoreCase))
        {
            return uri;
        }

        if (uri.Scheme.Equals(Uri.UriSchemeHttp, StringComparison.OrdinalIgnoreCase) &&
            uri.Host.Equals(XuiSubscriptionHost, StringComparison.OrdinalIgnoreCase) &&
            uri.Port == XuiSubscriptionPort &&
            uri.AbsolutePath.StartsWith("/sub/", StringComparison.OrdinalIgnoreCase))
        {
            return uri;
        }

        throw new InvalidOperationException("Недоверенная ссылка подписки.");
    }

    public static Uri RequirePaymentUrl(string value)
    {
        var uri = RequireHttps(value, "ссылка оплаты");
        if (!PaymentHosts.Any(host => uri.Host.Equals(host, StringComparison.OrdinalIgnoreCase) || uri.Host.EndsWith("." + host, StringComparison.OrdinalIgnoreCase))) throw new InvalidOperationException("Недоверенная ссылка оплаты.");
        return uri;
    }
    public static Uri RequireTelegramDeepLink(string value)
    {
        if (!Uri.TryCreate(value?.Trim(), UriKind.Absolute, out var uri) || !uri.Scheme.Equals("tg", StringComparison.OrdinalIgnoreCase) || !uri.Host.Equals("resolve", StringComparison.OrdinalIgnoreCase)) throw new InvalidOperationException("Недоверенная ссылка Telegram.");
        return uri;
    }
    public static Uri RequireTelegramWebLink(string value)
    {
        var uri = RequireHttps(value, "ссылка Telegram");
        if (!uri.Host.Equals("web.telegram.org", StringComparison.OrdinalIgnoreCase)) throw new InvalidOperationException("Недоверенная ссылка Telegram.");
        return uri;
    }    public static bool IsPublicAddress(IPAddress address)
    {
        if (IPAddress.IsLoopback(address) || address.IsIPv6LinkLocal || address.IsIPv6SiteLocal) return false;
        var b = address.GetAddressBytes();
        return address.AddressFamily == AddressFamily.InterNetwork && b[0] != 10 && b[0] != 127 && !(b[0] == 169 && b[1] == 254) && !(b[0] == 192 && b[1] == 168) && !(b[0] == 172 && b[1] >= 16 && b[1] <= 31);
    }
}
