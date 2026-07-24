namespace CosmoNet.App.Models;

public sealed class AppSubscriptionResult
{
    public string SubscriptionUrl { get; set; } = "";
    public SubscriptionSummary Subscription { get; set; } = new();
}
