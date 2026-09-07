using System.Text;
using CosmoNet.App.Models;

namespace CosmoNet.App.Services;

public sealed class MihomoConfigBuilder
{
    private static readonly string[] ChatGptDomainFallbacks =
    [
        "chatgpt.com",
        "openai.com",
        "oaistatic.com",
        "oaiusercontent.com"
    ];

    public async Task<string> WriteConfigAsync(
        IReadOnlyList<VpnProfile> profiles,
        TrafficMode trafficMode,
        IReadOnlyList<string> selectedProcessNames,
        IReadOnlyCollection<string>? selectedProcessPaths = null,
        CancellationToken cancellationToken = default,
        string? outputPath = null)
    {
        if (profiles.Count == 0)
        {
            throw new InvalidOperationException("Сначала обновите подписку.");
        }

        var processNames = selectedProcessNames
            .Where(name => !string.IsNullOrWhiteSpace(name))
            .Select(name => Path.GetFileName(name) ?? name)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .OrderBy(name => name, StringComparer.OrdinalIgnoreCase)
            .ToArray();

        var processPaths = (selectedProcessPaths ?? Array.Empty<string>())
            .Where(path => !string.IsNullOrWhiteSpace(path))
            .Select(Path.GetFullPath)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .OrderBy(path => path, StringComparer.OrdinalIgnoreCase)
            .ToArray();
        var useChatGptDomainFallback = trafficMode == TrafficMode.SelectedApps &&
            processNames.Any(name => string.Equals(name, "ChatGPT.exe", StringComparison.OrdinalIgnoreCase));

        if (trafficMode == TrafficMode.SelectedApps && processNames.Length == 0 && processPaths.Length == 0)
        {
            throw new InvalidOperationException("Выберите приложения для VPN.");
        }

        AppPaths.EnsureDataDirectory();
        var orderedProfiles = profiles
            .OrderBy(profile => profile.ConnectionPriority)
            .ThenBy(profile => profile.Name, StringComparer.OrdinalIgnoreCase)
            .ThenBy(profile => profile.Server, StringComparer.OrdinalIgnoreCase)
            .ToArray();

        var yaml = new StringBuilder();
        yaml.AppendLine("mixed-port: 20809");
        yaml.AppendLine("allow-lan: false");
        yaml.AppendLine("mode: rule");
        yaml.AppendLine("log-level: warning");
        yaml.AppendLine("find-process-mode: always");
        yaml.AppendLine($"log-file: {Quote(AppPaths.MihomoLogPath)}");
        yaml.AppendLine("ipv6: false");
        if (useChatGptDomainFallback)
        {
            AppendChatGptSniffer(yaml);
        }
        yaml.AppendLine("dns:");
        yaml.AppendLine("  enable: true");
        yaml.AppendLine("  enhanced-mode: redir-host");
        yaml.AppendLine("  nameserver:");
        yaml.AppendLine("    - https://1.1.1.1/dns-query");
        yaml.AppendLine("    - https://8.8.8.8/dns-query");
        yaml.AppendLine("proxies:");
        for (var index = 0; index < orderedProfiles.Length; index++)
        {
            AppendProxy(yaml, orderedProfiles[index], $"profile-{index}");
        }

        yaml.AppendLine("proxy-groups:");
        yaml.AppendLine("  - name: COSMONET");
        yaml.AppendLine("    type: url-test");
        yaml.AppendLine("    url: https://www.gstatic.com/generate_204");
        yaml.AppendLine("    interval: 300");
        yaml.AppendLine("    tolerance: 100");
        yaml.AppendLine("    proxies:");
        for (var index = 0; index < orderedProfiles.Length; index++)
        {
            yaml.AppendLine($"      - profile-{index}");
        }

        yaml.AppendLine("rules:");
        if (trafficMode == TrafficMode.SelectedApps)
        {
            foreach (var processPath in processPaths)
            {
                yaml.AppendLine($"  - {Quote($"PROCESS-PATH,{processPath},COSMONET")}");
            }
            foreach (var processName in processNames)
            {
                yaml.AppendLine($"  - PROCESS-NAME,{processName},COSMONET");
            }
            if (useChatGptDomainFallback)
            {
                foreach (var domain in ChatGptDomainFallbacks)
                {
                    yaml.AppendLine($"  - DOMAIN-SUFFIX,{domain},COSMONET");
                }
            }
            yaml.AppendLine("  - MATCH,DIRECT");
        }
        else
        {
            yaml.AppendLine("  - MATCH,COSMONET");
        }
        yaml.AppendLine("tun:");
        yaml.AppendLine("  enable: true");
        yaml.AppendLine("  stack: mixed");
        yaml.AppendLine("  auto-route: true");
        yaml.AppendLine("  auto-detect-interface: true");
        yaml.AppendLine("  strict-route: true");
        yaml.AppendLine("  dns-hijack:");
        yaml.AppendLine("    - any:53");


        var configPath = string.IsNullOrWhiteSpace(outputPath)
            ? AppPaths.MihomoConfigPath
            : outputPath;
        await File.WriteAllTextAsync(configPath, yaml.ToString(), new UTF8Encoding(false), cancellationToken);
        return configPath;
    }

    private static void AppendChatGptSniffer(StringBuilder yaml)
    {
        yaml.AppendLine("sniffer:");
        yaml.AppendLine("  enable: true");
        yaml.AppendLine("  force-dns-mapping: true");
        yaml.AppendLine("  parse-pure-ip: true");
        yaml.AppendLine("  sniff:");
        yaml.AppendLine("    TLS:");
        yaml.AppendLine("      ports: [443, 8443]");
        yaml.AppendLine("    QUIC:");
        yaml.AppendLine("      ports: [443, 8443]");
    }

    private static void AppendProxy(StringBuilder yaml, VpnProfile profile, string tag)
    {
        yaml.AppendLine($"  - name: {Quote(tag)}");
        yaml.AppendLine("    type: vless");
        yaml.AppendLine($"    server: {Quote(profile.Server)}");
        yaml.AppendLine($"    port: {profile.Port}");
        yaml.AppendLine($"    uuid: {Quote(profile.Uuid)}");
        yaml.AppendLine("    udp: true");

        var flow = profile.Query.GetValueOrDefault("flow", "");
        if (string.IsNullOrWhiteSpace(flow) && profile.Security.Equals("reality", StringComparison.OrdinalIgnoreCase))
        {
            flow = "xtls-rprx-vision";
        }
        if (!string.IsNullOrWhiteSpace(flow))
        {
            yaml.AppendLine($"    flow: {Quote(flow)}");
        }

        if (profile.Security.Equals("reality", StringComparison.OrdinalIgnoreCase) ||
            profile.Security.Equals("tls", StringComparison.OrdinalIgnoreCase))
        {
            yaml.AppendLine("    tls: true");
            yaml.AppendLine($"    servername: {Quote(profile.Query.GetValueOrDefault("sni", profile.Server) ?? profile.Server)}");
            var fingerprint = profile.Query.GetValueOrDefault("fp", "");
            if (!string.IsNullOrWhiteSpace(fingerprint))
            {
                yaml.AppendLine($"    client-fingerprint: {Quote(fingerprint)}");
            }

            if (profile.Security.Equals("reality", StringComparison.OrdinalIgnoreCase))
            {
                yaml.AppendLine("    reality-opts:");
                yaml.AppendLine($"      public-key: {Quote(profile.Query.GetValueOrDefault("pbk", ""))}");
                var shortId = profile.Query.GetValueOrDefault("sid", "");
                if (!string.IsNullOrWhiteSpace(shortId))
                {
                    yaml.AppendLine($"      short-id: {Quote(shortId)}");
                }
            }
        }

        if (profile.Network.Equals("ws", StringComparison.OrdinalIgnoreCase))
        {
            yaml.AppendLine("    network: ws");
            yaml.AppendLine("    ws-opts:");
            yaml.AppendLine($"      path: {Quote(profile.Query.GetValueOrDefault("path", "/"))}");
            var host = profile.Query.GetValueOrDefault("host", "");
            if (!string.IsNullOrWhiteSpace(host))
            {
                yaml.AppendLine("      headers:");
                yaml.AppendLine($"        Host: {Quote(host)}");
            }
        }
    }

    private static string Quote(string value) => $"\"{value.Replace("\\", "\\\\").Replace("\"", "\\\"")}\"";
}
