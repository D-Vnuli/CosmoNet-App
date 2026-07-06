using System.Text.Json.Serialization;

namespace CosmoNet.App.Models;

public sealed class AppSettings
{
    [JsonIgnore]
    public string SubscriptionUrl { get; set; } = "";

    public TrafficMode TrafficMode { get; set; } = TrafficMode.AllTraffic;
    public bool StartMinimized { get; set; }
    public DateTimeOffset? LastSubscriptionRefresh { get; set; }
    public List<string> SelectedProcessNames { get; set; } = [];
    public AccountSession AccountSession { get; set; } = new();
}
