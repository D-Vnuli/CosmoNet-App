namespace CosmoNet.App.Models;

public sealed class SecretSettings
{
    public string SubscriptionUrl { get; set; } = "";
    public string AuthToken { get; set; } = "";
    public string AuthDeviceId { get; set; } = "";
}
