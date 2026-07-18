using System.Text.Json.Serialization;

namespace CosmoNet.App.Models;

public sealed class AppSettings
{
    public const string DefaultAuthApiBaseUrl = "https://api.cosmonet.shop:18443/";
    [JsonIgnore]
    public string SubscriptionUrl { get; set; } = "";

    public string AuthApiBaseUrl { get; set; } = DefaultAuthApiBaseUrl;
    public TrafficMode TrafficMode { get; set; } = TrafficMode.AllTraffic;
    public bool StartMinimized { get; set; }
    public DateTimeOffset? LastSubscriptionRefresh { get; set; }
    public List<string> SelectedProcessNames { get; set; } = [];
    public Dictionary<string, string> SelectedApplicationPaths { get; set; } = new(StringComparer.OrdinalIgnoreCase);
    public AccountSession AccountSession { get; set; } = new();
}
