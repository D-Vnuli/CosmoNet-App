using System.Text.Json;
using CosmoNet.App.Models;

namespace CosmoNet.App.Services;

public sealed class SingBoxConfigBuilder
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true
    };

    public async Task<string> WriteConfigAsync(
        IReadOnlyList<VpnProfile> profiles,
        TrafficMode trafficMode,
        IReadOnlyList<string> selectedProcessNames,
        CancellationToken cancellationToken = default)
    {
        if (profiles.Count == 0)
        {
            throw new InvalidOperationException("Сначала обновите подписку.");
        }

        if (trafficMode == TrafficMode.SelectedApps && selectedProcessNames.Count == 0)
        {
            throw new InvalidOperationException("Выберите приложения, которые должны работать через VPN.");
        }

        AppPaths.EnsureDataDirectory();

        var outbounds = profiles
            .Select((profile, index) => BuildOutbound(profile, index))
            .ToList<object>();

        outbounds.Add(new Dictionary<string, object?> { ["type"] = "direct", ["tag"] = "direct" });
        outbounds.Add(new Dictionary<string, object?> { ["type"] = "block", ["tag"] = "block" });

        var config = new Dictionary<string, object?>
        {
            ["log"] = new Dictionary<string, object?> { ["level"] = "info", ["timestamp"] = true },
            ["dns"] = BuildDns(),
            ["inbounds"] = new object[] { BuildTunInbound() },
            ["outbounds"] = outbounds,
            ["route"] = BuildRoute(trafficMode, selectedProcessNames)
        };

        await using var stream = File.Create(AppPaths.GeneratedConfigPath);
        await JsonSerializer.SerializeAsync(stream, config, JsonOptions, cancellationToken);
        return AppPaths.GeneratedConfigPath;
    }

    private static Dictionary<string, object?> BuildDns()
    {
        return new Dictionary<string, object?>
        {
            ["servers"] = new object[]
            {
                new Dictionary<string, object?> { ["tag"] = "cloudflare", ["address"] = "https://1.1.1.1/dns-query" },
                new Dictionary<string, object?> { ["tag"] = "google", ["address"] = "https://8.8.8.8/dns-query" }
            },
            ["final"] = "cloudflare"
        };
    }

    private static Dictionary<string, object?> BuildTunInbound()
    {
        return new Dictionary<string, object?>
        {
            ["type"] = "tun",
            ["tag"] = "tun-in",
            ["interface_name"] = "CosmoNet",
            ["address"] = new[] { "172.19.0.1/30" },
            ["auto_route"] = true,
            ["strict_route"] = true,
            ["stack"] = "system"
        };
    }

    private static Dictionary<string, object?> BuildRoute(
        TrafficMode trafficMode,
        IReadOnlyList<string> selectedProcessNames)
    {
        var route = new Dictionary<string, object?>
        {
            ["auto_detect_interface"] = true,
            ["final"] = trafficMode == TrafficMode.AllTraffic ? "profile-0" : "direct"
        };

        if (trafficMode == TrafficMode.SelectedApps)
        {
            route["rules"] = new object[]
            {
                new Dictionary<string, object?>
                {
                    ["process_name"] = selectedProcessNames
                        .Where(name => !string.IsNullOrWhiteSpace(name))
                        .Distinct(StringComparer.OrdinalIgnoreCase)
                        .OrderBy(name => name, StringComparer.OrdinalIgnoreCase)
                        .ToArray(),
                    ["outbound"] = "profile-0"
                }
            };
        }

        return route;
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
