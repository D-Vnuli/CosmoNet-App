namespace CosmoNet.App.Models;

public sealed class TelegramAuthStartResult
{
    public string SessionId { get; set; } = "";
    public DateTimeOffset ExpiresAt { get; set; }
    public string TelegramDeepLink { get; set; } = "";
    public string TelegramWebLink { get; set; } = "";
    public BootstrapVpnProfile? BootstrapVpn { get; set; }
    public List<BootstrapVpnProfile> BootstrapVpns { get; set; } = [];
}
