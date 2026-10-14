namespace CosmoNet.App.Models;

public static class SubscriptionStateEvaluator
{
    public static SubscriptionStatus Resolve(
        SubscriptionSummary subscription,
        DateTimeOffset now)
    {
        if (subscription.Status is SubscriptionStatus.Disabled or SubscriptionStatus.Expired)
        {
            return subscription.Status;
        }

        return subscription.ExpiresAt is { } expiresAt && expiresAt <= now
            ? SubscriptionStatus.Expired
            : subscription.Status;
    }

    public static bool IsAuthoritative(SubscriptionSummary? subscription)
    {
        return subscription?.Status is SubscriptionStatus.Active
            or SubscriptionStatus.ExpiringSoon
            or SubscriptionStatus.Expired
            or SubscriptionStatus.Disabled
            or SubscriptionStatus.NoSubscription;
    }

    public static bool CanApplyRefresh(
        SubscriptionSummary? subscription,
        string? subscriptionUrl)
    {
        if (!IsAuthoritative(subscription))
        {
            return false;
        }

        return subscription!.Status is SubscriptionStatus.Expired
            or SubscriptionStatus.Disabled
            or SubscriptionStatus.NoSubscription
            || !string.IsNullOrWhiteSpace(subscriptionUrl);
    }

    public static bool IsExpired(SubscriptionSummary subscription, DateTimeOffset now)
    {
        return Resolve(subscription, now) == SubscriptionStatus.Expired;
    }

    public static bool CanUseVpn(SubscriptionSummary subscription, DateTimeOffset now)
    {
        return Resolve(subscription, now) is SubscriptionStatus.Active or SubscriptionStatus.ExpiringSoon;
    }

    public static bool IsSubscriptionBlocked(SubscriptionSummary subscription, DateTimeOffset now)
    {
        return Resolve(subscription, now) is SubscriptionStatus.Expired
            or SubscriptionStatus.Disabled
            or SubscriptionStatus.NoSubscription;
    }

    public static bool CanUsePowerControl(
        SubscriptionSummary subscription,
        DateTimeOffset now,
        bool isConnected)
    {
        return isConnected || CanUseVpn(subscription, now);
    }
}
