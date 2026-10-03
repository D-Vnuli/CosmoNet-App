using System.Net;
using System.Net.Http;

namespace CosmoNet.App.Services;

public interface IVpnConnectivityVerifier
{
    Task VerifyAsync(CancellationToken cancellationToken = default);
}

public sealed class VpnConnectivityVerifier : IVpnConnectivityVerifier
{
    private static readonly Uri[] ProbeEndpoints =
    [
        new("https://www.gstatic.com/generate_204"),
        new("https://www.cloudflare.com/cdn-cgi/trace")
    ];

    private static readonly TimeSpan VerificationTimeout = TimeSpan.FromSeconds(6);

    public async Task VerifyAsync(CancellationToken cancellationToken = default)
    {
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeout.CancelAfter(VerificationTimeout);
        using var handler = new HttpClientHandler
        {
            Proxy = new WebProxy($"http://127.0.0.1:{SystemProxyService.VpnPort}")
            {
                BypassProxyOnLocal = false
            },
            UseProxy = true,
            AllowAutoRedirect = false
        };
        using var client = new HttpClient(handler) { Timeout = Timeout.InfiniteTimeSpan };

        Exception? lastError = null;
        foreach (var endpoint in ProbeEndpoints)
        {
            try
            {
                using var response = await client.GetAsync(endpoint, timeout.Token);
                if ((int)response.StatusCode is >= 200 and < 400)
                {
                    return;
                }

                lastError = new HttpRequestException($"Connectivity probe returned HTTP {(int)response.StatusCode}.");
            }
            catch (Exception error) when (error is HttpRequestException or OperationCanceledException)
            {
                lastError = error;
                if (timeout.IsCancellationRequested)
                {
                    break;
                }
            }
        }

        throw new InvalidOperationException("Не удалось подтвердить VPN-соединение через Mihomo.", lastError);
    }
}
