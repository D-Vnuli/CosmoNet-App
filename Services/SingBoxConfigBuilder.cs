using System.Text.Json;
using CosmoNet.App.Models;

namespace CosmoNet.App.Services;

public sealed class SingBoxConfigBuilder
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true
    };

    public async Task<string> WriteBootstrapConfigAsync(
        VpnProfile profile,
        CancellationToken cancellationToken = default)
    {
        AppPaths.EnsureDataDirectory();

        var dns = BuildDns();
        foreach (var server in (object[])dns["servers"]!)
        {
            ((Dictionary<string, object?>)server)["detour"] = "profile-0";
        }

        var config = new Dictionary<string, object?>
        {
            ["log"] = new Dictionary<string, object?>
            {
                ["level"] = "warn",
                ["timestamp"] = true,
                ["output"] = AppPaths.SingBoxLogPath,
            },
            ["dns"] = dns,
            ["inbounds"] = new object[]
            {
                new Dictionary<string, object?>
                {
                    ["type"] = "mixed",
                    ["tag"] = "bootstrap-proxy",
                    ["listen"] = "127.0.0.1",
                    ["listen_port"] = SystemProxyService.BootstrapPort,
                },
            },
            ["outbounds"] = new object[]
            {
                BuildOutbound(profile, 0),
                new Dictionary<string, object?> { ["type"] = "direct", ["tag"] = "direct" },
            },
            ["route"] = new Dictionary<string, object?>
            {
                ["auto_detect_interface"] = true,
                ["default_domain_resolver"] = "cloudflare",
                ["final"] = "profile-0",
            },
        };

        await using var stream = File.Create(AppPaths.BootstrapConfigPath);
        await JsonSerializer.SerializeAsync(stream, config, JsonOptions, cancellationToken);
        return AppPaths.BootstrapConfigPath;
    }
    public async Task<string> WriteBootstrapDesktopConfigAsync(
        VpnProfile profile,
        string processName,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(processName))
        {
            throw new InvalidOperationException("Не найден процесс Telegram Desktop.");
        }

        AppPaths.EnsureDataDirectory();
        var dns = BuildDns();
        foreach (var server in (object[])dns["servers"]!)
        {
            ((Dictionary<string, object?>)server)["detour"] = "profile-0";
        }

        var config = new Dictionary<string, object?>
        {
            ["log"] = new Dictionary<string, object?>
            {
                ["level"] = "warn",
                ["timestamp"] = true,
                ["output"] = AppPaths.SingBoxLogPath,
            },
            ["dns"] = dns,
            ["inbounds"] = new object[]
            {
                BuildTunInbound(new[] { profile }),
                new Dictionary<string, object?>
                {
                    ["type"] = "mixed",
                    ["tag"] = "bootstrap-proxy",
                    ["listen"] = "127.0.0.1",
                    ["listen_port"] = SystemProxyService.BootstrapPort,
                },
            },
            ["outbounds"] = new object[]
            {
                BuildOutbound(profile, 0),
                new Dictionary<string, object?> { ["type"] = "direct", ["tag"] = "direct" },
            },
            ["route"] = new Dictionary<string, object?>
            {
                ["auto_detect_interface"] = true,
                ["default_domain_resolver"] = "cloudflare",
                ["rules"] = new object[]
                {
                    new Dictionary<string, object?> { ["protocol"] = "dns", ["action"] = "hijack-dns" },
                    new Dictionary<string, object?> { ["inbound"] = new[] { "bootstrap-proxy" }, ["outbound"] = "profile-0" },
                    new Dictionary<string, object?>
                    {
                        ["inbound"] = new[] { "tun-in" },
                        ["process_name"] = new[] { processName },
                        ["outbound"] = "profile-0",
                    },
                },
                ["final"] = "direct",
            },
        };

        await using var stream = File.Create(AppPaths.BootstrapConfigPath);
        await JsonSerializer.SerializeAsync(stream, config, JsonOptions, cancellationToken);
        return AppPaths.BootstrapConfigPath;
    }
    private static Dictionary<string, object?> BuildDns()
    {
        return new Dictionary<string, object?>
        {
            ["strategy"] = "prefer_ipv4",
            ["servers"] = new object[]
            {
                new Dictionary<string, object?>
                {
                    ["type"] = "https",
                    ["tag"] = "cloudflare",
                    ["server"] = "1.1.1.1",
                    ["server_port"] = 443,
                    ["path"] = "/dns-query",
                    ["headers"] = new Dictionary<string, string> { ["Host"] = "cloudflare-dns.com" },
                    ["tls"] = new Dictionary<string, object?>
                    {
                        ["enabled"] = true,
                        ["server_name"] = "cloudflare-dns.com"
                    }
                },
                new Dictionary<string, object?>
                {
                    ["type"] = "https",
                    ["tag"] = "google",
                    ["server"] = "8.8.8.8",
                    ["server_port"] = 443,
                    ["path"] = "/dns-query",
                    ["headers"] = new Dictionary<string, string> { ["Host"] = "dns.google" },
                    ["tls"] = new Dictionary<string, object?>
                    {
                        ["enabled"] = true,
                        ["server_name"] = "dns.google"
                    }
                }
            },
            ["final"] = "cloudflare"
        };
    }

    private static Dictionary<string, object?> BuildTunInbound(IReadOnlyList<VpnProfile>? profiles = null)
    {
        var inbound = new Dictionary<string, object?>
        {
            ["type"] = "tun",
            ["tag"] = "tun-in",
            ["interface_name"] = "CosmoNet",
            ["address"] = new[] { "172.19.0.1/30", "fdfe:dcba:9876::1/126" },
            ["mtu"] = 1400,
            ["auto_route"] = true,
            ["strict_route"] = true,
            ["stack"] = "mixed"
        };

        var routeExclusions = profiles
            ?.Select(profile => profile.Server)
            .Where(server => System.Net.IPAddress.TryParse(server, out _))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .Select(server => System.Net.IPAddress.Parse(server).AddressFamily == System.Net.Sockets.AddressFamily.InterNetwork ? $"{server}/32" : $"{server}/128")
            .ToArray() ?? [];

        if (routeExclusions.Length > 0)
        {
            inbound["route_exclude_address"] = routeExclusions;
        }

        return inbound;
    }

    private static Dictionary<string, object?> BuildOutbound(VpnProfile profile, int index)
    {
        var outbound = new Dictionary<string, object?>
        {
            ["type"] = "vless",
            ["tag"] = $"profile-{index}",
            ["server"] = profile.Server,
            ["server_port"] = profile.Port,
            ["uuid"] = profile.Uuid
        };

        var flow = profile.Query.GetValueOrDefault("flow", "");
        if (string.IsNullOrWhiteSpace(flow) &&
            profile.Security.Equals("reality", StringComparison.OrdinalIgnoreCase))
        {
            // Reality inbound CosmoNet uses Vision; older 3X-UI subscription
            // links may omit this query parameter.
            flow = "xtls-rprx-vision";
        }

        if (!string.IsNullOrWhiteSpace(flow))
        {
            outbound["flow"] = flow;
        }

        if (profile.Security.Equals("reality", StringComparison.OrdinalIgnoreCase) ||
            profile.Security.Equals("tls", StringComparison.OrdinalIgnoreCase))
        {
            outbound["tls"] = BuildTls(profile);
        }

        if (profile.Network.Equals("ws", StringComparison.OrdinalIgnoreCase))
        {
            outbound["transport"] = BuildWebSocketTransport(profile);
        }

        return outbound;
    }

    private static Dictionary<string, object?> BuildTls(VpnProfile profile)
    {
        var tls = new Dictionary<string, object?>
        {
            ["enabled"] = true,
            ["server_name"] = profile.Query.GetValueOrDefault("sni", profile.Server)
        };

        var fingerprint = profile.Query.GetValueOrDefault("fp", "");
        if (!string.IsNullOrWhiteSpace(fingerprint))
        {
            tls["utls"] = new Dictionary<string, object?> { ["enabled"] = true, ["fingerprint"] = fingerprint };
        }

        if (profile.Security.Equals("reality", StringComparison.OrdinalIgnoreCase))
        {
            tls["reality"] = new Dictionary<string, object?>
            {
                ["enabled"] = true,
                ["public_key"] = profile.Query.GetValueOrDefault("pbk", ""),
                ["short_id"] = profile.Query.GetValueOrDefault("sid", "")
            };
        }

        return tls;
    }

    private static Dictionary<string, object?> BuildWebSocketTransport(VpnProfile profile)
    {
        var transport = new Dictionary<string, object?>
        {
            ["type"] = "ws",
            ["path"] = profile.Query.GetValueOrDefault("path", "/")
        };

        var host = profile.Query.GetValueOrDefault("host", "");
        if (!string.IsNullOrWhiteSpace(host))
        {
            transport["headers"] = new Dictionary<string, object?> { ["Host"] = host };
        }

        return transport;
    }
}
