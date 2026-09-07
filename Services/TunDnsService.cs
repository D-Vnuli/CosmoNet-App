using System.Diagnostics;
using System.Net.NetworkInformation;

namespace CosmoNet.App.Services;

public sealed class TunDnsService
{
    private const string TunAdapterDescription = "sing-tun Tunnel";
    private const string DnsServer = "172.19.0.1";

    public async Task ConfigureAsync(CancellationToken cancellationToken = default)
    {
        NetworkInterface? adapter = null;
        for (var attempt = 0; attempt < 20; attempt++)
        {
            adapter = NetworkInterface.GetAllNetworkInterfaces().FirstOrDefault(network =>
                network.OperationalStatus == OperationalStatus.Up &&
                string.Equals(network.Description, TunAdapterDescription, StringComparison.OrdinalIgnoreCase));

            if (adapter is not null)
            {
                break;
            }

            await Task.Delay(100, cancellationToken);
        }

        if (adapter is null)
        {
            return;
        }

        using var process = Process.Start(new ProcessStartInfo
        {
            FileName = "netsh.exe",
            Arguments = $"interface ipv4 set dnsservers name=\"{adapter.Name}\" static {DnsServer} primary validate=no",
            UseShellExecute = false,
            CreateNoWindow = true
        }) ?? throw new InvalidOperationException("Не удалось настроить DNS VPN-адаптера.");

        await process.WaitForExitAsync(cancellationToken);
        if (process.ExitCode != 0)
        {
            return;
        }
    }
}