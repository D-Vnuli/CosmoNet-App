namespace CosmoNet.App.Models;

public sealed class SubscriptionSummary
{
    public SubscriptionStatus Status { get; set; } = SubscriptionStatus.Unknown;
    public string TariffName { get; set; } = "Неизвестно";
    public DateTimeOffset? ExpiresAt { get; set; }
    public int? DeviceLimit { get; set; }
    public long TrafficUsedBytes { get; set; }
    public long TrafficLimitBytes { get; set; }
    public DateTimeOffset? LastSyncedAt { get; set; }

    public static SubscriptionSummary Empty { get; } = new();
}
