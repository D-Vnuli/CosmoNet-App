namespace CosmoNet.App.Models;

public sealed class AccountSession
{
    public bool IsAuthorized { get; set; }
    public string DisplayName { get; set; } = "Telegram не подключен";
    public DateTimeOffset? AuthorizedAt { get; set; }
    public SubscriptionSummary Subscription { get; set; } = new();

    public static AccountSession Empty { get; } = new();
}
