using CosmoNet.App.Models;
using System.Xml.Linq;

var now = new DateTimeOffset(2026, 10, 3, 12, 0, 0, TimeSpan.Zero);
var passed = 0;

AssertStatus(SubscriptionStatus.Active, now.AddTicks(1), SubscriptionStatus.Active,
    "active subscription remains active before the exact instant");
AssertStatus(SubscriptionStatus.Active, now, SubscriptionStatus.Expired,
    "subscription expires at the exact instant");
AssertStatus(SubscriptionStatus.ExpiringSoon, now.AddHours(1), SubscriptionStatus.ExpiringSoon,
    "backend expiring-soon status is preserved");
AssertStatus(SubscriptionStatus.Expired, now.AddHours(1), SubscriptionStatus.Expired,
    "backend expired status is preserved");
AssertStatus(SubscriptionStatus.Disabled, now.AddHours(1), SubscriptionStatus.Disabled,
    "backend disabled status is preserved");
AssertStatus(SubscriptionStatus.NoSubscription, now.AddHours(1), SubscriptionStatus.NoSubscription,
    "backend no-subscription status is preserved");

AssertVpnAccess(SubscriptionStatus.Active, now.AddDays(1), true, false, "active");
AssertVpnAccess(SubscriptionStatus.ExpiringSoon, now.AddMinutes(1), true, false, "expiring soon");
AssertVpnAccess(SubscriptionStatus.Expired, null, false, true, "expired");
AssertVpnAccess(SubscriptionStatus.Disabled, null, false, true, "disabled");
AssertVpnAccess(SubscriptionStatus.NoSubscription, null, false, true, "confirmed no subscription");
Assert(!SubscriptionStateEvaluator.CanUseVpn(
           new SubscriptionSummary { Status = SubscriptionStatus.Unknown }, now),
    "unverified state must not allow a new VPN connection");
Assert(!SubscriptionStateEvaluator.IsSubscriptionBlocked(
           new SubscriptionSummary { Status = SubscriptionStatus.Unknown }, now),
    "unknown state must not be presented as a confirmed inactive subscription");

foreach (var status in new[]
         {
             SubscriptionStatus.Active,
             SubscriptionStatus.ExpiringSoon,
             SubscriptionStatus.Expired,
             SubscriptionStatus.Disabled,
             SubscriptionStatus.NoSubscription,
         })
{
    Assert(SubscriptionStateEvaluator.IsAuthoritative(new SubscriptionSummary { Status = status }),
        $"{status} must be an authoritative backend state");
}

Assert(!SubscriptionStateEvaluator.IsAuthoritative(null)
       && !SubscriptionStateEvaluator.IsAuthoritative(new SubscriptionSummary { Status = SubscriptionStatus.Unknown }),
    "missing or unknown responses must not replace verified data");
Assert(!SubscriptionStateEvaluator.CanApplyRefresh(new SubscriptionSummary { Status = SubscriptionStatus.Active }, ""),
    "active response without a subscription URL must be rejected");
Assert(SubscriptionStateEvaluator.CanApplyRefresh(new SubscriptionSummary { Status = SubscriptionStatus.Disabled }, "")
       && SubscriptionStateEvaluator.CanApplyRefresh(new SubscriptionSummary { Status = SubscriptionStatus.Expired }, "")
       && SubscriptionStateEvaluator.CanApplyRefresh(new SubscriptionSummary { Status = SubscriptionStatus.NoSubscription }, ""),
    "confirmed blocked responses may omit a subscription URL");
Assert(!SubscriptionStateEvaluator.CanApplyRefresh(null, null)
       && !SubscriptionStateEvaluator.CanApplyRefresh(
           new SubscriptionSummary { Status = SubscriptionStatus.Unknown }, "https://example.invalid/sub"),
    "timeout, HTTP error, and malformed refreshes must preserve last verified state");

var renewedSubscription = new SubscriptionSummary
{
    Status = SubscriptionStatus.Active,
    ExpiresAt = now.AddDays(30),
};
Assert(SubscriptionStateEvaluator.CanUseVpn(renewedSubscription, now)
       && SubscriptionStateEvaluator.CanUsePowerControl(renewedSubscription, now, isConnected: false),
    "renewal must restore VPN availability and the connect command");

var appRoot = Path.GetFullPath(Path.Combine(
    Directory.GetCurrentDirectory(), "..", ".."));
var xaml = File.ReadAllText(Path.Combine(appRoot, "MainWindow.xaml"));
var viewModel = File.ReadAllText(Path.Combine(appRoot, "ViewModels", "MainViewModel.cs"));
var apiClient = File.ReadAllText(Path.Combine(appRoot, "Services", "TelegramAuthApiClient.cs"));
_ = XDocument.Parse(xaml);
Assert(xaml.Contains("x:Name=\"SubscriptionBlockedProhibition\"", StringComparison.Ordinal)
       && xaml.Contains("Binding IsSubscriptionBlocked", StringComparison.Ordinal)
       && xaml.Contains("Binding SubscriptionBlockedMessage", StringComparison.Ordinal)
       && !xaml.Contains("Width=\"21\" Height=\"21\" Fill=\"#F15A64\"", StringComparison.Ordinal)
       && !xaml.Contains("M6.5,1 L12,11 H1 Z", StringComparison.Ordinal),
    "confirmed inactive state must render the prohibition overlay and banner");
Assert(xaml.Contains("Binding CanUsePowerButton", StringComparison.Ordinal)
       && viewModel.Contains("() => !IsBusy && CanUsePowerButton", StringComparison.Ordinal),
    "the planet command must be unavailable for blocked disconnected users");
var connectOffset = viewModel.IndexOf("private async Task ConnectAsync()", StringComparison.Ordinal);
var guardOffset = viewModel.IndexOf("if (!CanUseVpn)", connectOffset, StringComparison.Ordinal);
var busyOffset = viewModel.IndexOf("await RunBusyAsync", connectOffset, StringComparison.Ordinal);
Assert(connectOffset >= 0 && guardOffset > connectOffset && guardOffset < busyOffset,
    "ConnectAsync must reject blocked state before entering Connecting");
Assert(viewModel.Contains("OnPropertyChanged(nameof(CanUseVpn));", StringComparison.Ordinal)
       && viewModel.Contains("OnPropertyChanged(nameof(IsSubscriptionBlocked));", StringComparison.Ordinal)
       && viewModel.Contains("RaiseCommandStates();", StringComparison.Ordinal),
    "subscription refresh must update UI bindings and command availability");
Assert(apiClient.Contains("Incomplete subscription response.", StringComparison.Ordinal)
       && apiClient.Contains("SubscriptionStatus.NoSubscription", StringComparison.Ordinal),
    "only an explicit backend status 0 may become confirmed no-subscription");

Console.WriteLine($"CosmoNet desktop subscription-state tests: {passed} passed");

void AssertStatus(
    SubscriptionStatus backendStatus,
    DateTimeOffset expiresAt,
    SubscriptionStatus expected,
    string scenario)
{
    Assert(SubscriptionStateEvaluator.Resolve(
            new SubscriptionSummary { Status = backendStatus, ExpiresAt = expiresAt }, now) == expected,
        scenario);
}

void AssertVpnAccess(
    SubscriptionStatus status,
    DateTimeOffset? expiresAt,
    bool expectedAccess,
    bool expectedBlocked,
    string scenario)
{
    var subscription = new SubscriptionSummary { Status = status, ExpiresAt = expiresAt };
    Assert(SubscriptionStateEvaluator.CanUseVpn(subscription, now) == expectedAccess,
        $"{scenario}: incorrect CanUseVpn");
    Assert(SubscriptionStateEvaluator.IsSubscriptionBlocked(subscription, now) == expectedBlocked,
        $"{scenario}: incorrect blocked state");
    Assert(SubscriptionStateEvaluator.CanUsePowerControl(subscription, now, isConnected: false) == expectedAccess,
        $"{scenario}: disconnected power control must match CanUseVpn");
    Assert(SubscriptionStateEvaluator.CanUsePowerControl(subscription, now, isConnected: true),
        $"{scenario}: connected user must retain safe disconnect control");
}

void Assert(bool condition, string message)
{
    if (!condition)
    {
        throw new InvalidOperationException(message);
    }

    passed++;
}
