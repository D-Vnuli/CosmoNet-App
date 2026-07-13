namespace CosmoNet.App.Models;

public sealed class XuiSubscriptionMetadata
{
    public int DeviceLimit { get; init; }
    public bool IsEnabled { get; init; }
    public DateTimeOffset? ExpiresAt { get; init; }

    public SubscriptionStatus Status
    {
        get
        {
            if (!IsEnabled)
            {
                return SubscriptionStatus.Disabled;
            }

            if (ExpiresAt is null)
            {
                return SubscriptionStatus.Active;
            }

            var now = DateTimeOffset.Now;
            if (ExpiresAt <= now)
            {
                return SubscriptionStatus.Expired;
            }

            return ExpiresAt <= now.AddDays(3)
                ? SubscriptionStatus.ExpiringSoon
                : SubscriptionStatus.Active;
        }
    }
}