using System.Net;
using System.Net.Sockets;

namespace CosmoNet.App.Services;

internal static class SecurityPolicy
{
    private const string ApiHost = "api.cosmonet.shop";
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
    public static Uri RequirePaymentUrl(string value)
    {
        var uri = RequireHttps(value, "ссылка оплаты");
        if (!PaymentHosts.Any(host => uri.Host.Equals(host, StringComparison.OrdinalIgnoreCase) || uri.Host.EndsWith("." + host, StringComparison.OrdinalIgnoreCase))) throw new InvalidOperationException("Недоверенная ссылка оплаты.");
        return uri;
    }
    public static bool IsPublicAddress(IPAddress address)
    {
        if (IPAddress.IsLoopback(address) || address.IsIPv6LinkLocal || address.IsIPv6SiteLocal) return false;
        var b = address.GetAddressBytes();
        return address.AddressFamily == AddressFamily.InterNetwork && b[0] != 10 && b[0] != 127 && !(b[0] == 169 && b[1] == 254) && !(b[0] == 192 && b[1] == 168) && !(b[0] == 172 && b[1] >= 16 && b[1] <= 31);
    }
}
