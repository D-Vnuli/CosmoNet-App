namespace CosmoNet.App.Models;

public sealed class TelegramAuthStatusResult
{
    public bool IsAuthorized { get; set; }
    public bool IsPending { get; set; } = true;
    public string DisplayName { get; set; } = "";
    public string AuthToken { get; set; } = "";
    public string SubscriptionUrl { get; set; } = "";
    public SubscriptionSummary Subscription { get; set; } = new();
    public string Message { get; set; } = "";
}
