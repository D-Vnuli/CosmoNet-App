namespace CosmoNet.App.Models;

public sealed class BootstrapVpnProfile
{
    public string VlessUri { get; set; } = "";
    public DateTimeOffset ExpiresAt { get; set; }
}