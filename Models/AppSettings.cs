namespace CosmoNet.App.Models;

public sealed class AppSettings
{
    public string SubscriptionUrl { get; set; } = "";
    public bool UseTunMode { get; set; } = true;
    public bool StartMinimized { get; set; }
    public DateTimeOffset? LastSubscriptionRefresh { get; set; }
}
