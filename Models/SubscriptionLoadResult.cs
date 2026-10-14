namespace CosmoNet.App.Models;

public sealed class SubscriptionLoadResult
{
    public IReadOnlyList<VpnProfile> Profiles { get; init; } = [];
    public SubscriptionSummary Summary { get; init; } = new();
}
