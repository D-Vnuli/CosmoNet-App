using System.Text.Json.Serialization;

namespace CosmoNet.App.Models;

public sealed class AppSettings
{
    public const string DefaultAuthApiBaseUrl = "https://api.cosmonet.shop:18443/";
    [JsonIgnore]
    public string SubscriptionUrl { get; set; } = "";

    public string AuthApiBaseUrl { get; set; } = DefaultAuthApiBaseUrl;
    public TrafficMode TrafficMode { get; set; } = TrafficMode.AllTraffic;
    // Older builds stored a selected-apps mode that was also used by the
    // temporary Telegram VPN. Treat it as a legacy default once, rather than
    // silently leaving a newly authorized user with all traffic routed direct.
    public bool HasExplicitTrafficModeChoice { get; set; }
    public const int CurrentTrafficModeConfigurationVersion = 1;
    public int TrafficModeConfigurationVersion { get; set; }
    public bool StartMinimized { get; set; }
    public DateTimeOffset? LastSubscriptionRefresh { get; set; }
    public List<string> SelectedProcessNames { get; set; } = [];
    public Dictionary<string, string> SelectedApplicationPaths { get; set; } = new(StringComparer.OrdinalIgnoreCase);
    public AccountSession AccountSession { get; set; } = new();
}
