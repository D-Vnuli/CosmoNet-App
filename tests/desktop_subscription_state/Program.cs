using CosmoNet.App.Models;
using CosmoNet.App.Services;
using System.Xml.Linq;
using System.Collections.Concurrent;
using System.Net;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;

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
var mainWindow = File.ReadAllText(Path.Combine(appRoot, "MainWindow.xaml.cs"));
var viewModel = File.ReadAllText(Path.Combine(appRoot, "ViewModels", "MainViewModel.cs"));
var apiClient = File.ReadAllText(Path.Combine(appRoot, "Services", "TelegramAuthApiClient.cs"));
var mainWindowDocument = XDocument.Parse(xaml);
XNamespace presentation = "http://schemas.microsoft.com/winfx/2006/xaml/presentation";
XNamespace xamlNamespace = "http://schemas.microsoft.com/winfx/2006/xaml";
Assert(xaml.Contains("x:Name=\"SubscriptionBlockedProhibition\"", StringComparison.Ordinal)
       && xaml.Contains("Binding IsSubscriptionBlocked", StringComparison.Ordinal)
       && xaml.Contains("Binding SubscriptionBlockedMessage", StringComparison.Ordinal)
       && !xaml.Contains("Width=\"21\" Height=\"21\" Fill=\"#F15A64\"", StringComparison.Ordinal)
       && !xaml.Contains("M6.5,1 L12,11 H1 Z", StringComparison.Ordinal),
    "confirmed inactive state must render the prohibition overlay and banner");
Assert(xaml.Contains("Binding CanUsePowerButton", StringComparison.Ordinal)
       && viewModel.Contains("() => !IsBusy && CanUsePowerButton", StringComparison.Ordinal),
    "the planet command must be unavailable for blocked disconnected users");
Assert(xaml.Contains("Content=\"&#x414;&#x43E;&#x441;&#x442;&#x443;&#x43F;&#x43D;&#x43E; &#x43E;&#x431;&#x43D;&#x43E;&#x432;&#x43B;&#x435;&#x43D;&#x438;&#x435;\"", StringComparison.Ordinal)
       && xaml.Contains("Command=\"{Binding OpenWindowsUpdateCommand}\"", StringComparison.Ordinal)
       && xaml.Contains("Visibility=\"{Binding IsWindowsUpdateAvailable", StringComparison.Ordinal),
    "the update button remains bound to verified update state");
var updateButton = mainWindowDocument
    .Descendants(presentation + "Button")
    .Single(button => button.Attribute("Content")?.Value == "Доступно обновление");
var updateOverlay = updateButton.Parent as XElement;
var accountActions = mainWindowDocument
    .Descendants(presentation + "Grid")
    .SingleOrDefault(grid => grid.Attribute(xamlNamespace + "Name")?.Value == "HeaderAccountActions");
Assert(updateOverlay is not null
       && updateButton.Attribute(xamlNamespace + "Name")?.Value == "WindowsUpdateButton"
       && updateButton.Attribute("Panel.ZIndex")?.Value == "2"
       && updateButton.Attribute("VerticalAlignment")?.Value == "Top"
       && accountActions is not null
       && accountActions.Descendants(presentation + "Button")
           .Any(button => button.Attribute(xamlNamespace + "Name")?.Value == "AuthButton")
       && accountActions.Descendants(presentation + "Button")
           .Any(button => button.Attribute("Content")?.Value == "Выйти")
       && !accountActions.Descendants(presentation + "Button")
           .Any(button => button.Attribute("Content")?.Value == "Доступно обновление"),
    "the update button must be a visible root overlay, isolated below account controls");
Assert(updateButton.Descendants(presentation + "DataTrigger")
           .Any(trigger => trigger.Attribute("Binding")?.Value == "{Binding IsAuthorized}"
                           && trigger.Elements(presentation + "Setter")
                               .Any(setter => setter.Attribute("Property")?.Value == "Width"
                                              && setter.Attribute("Value")?.Value == "{Binding ActualWidth, ElementName=HeaderAccountActions}")),
    "an authorized update button must exactly match the visible account-and-logout row width");
var outerHeaderRow = accountActions?.Ancestors(presentation + "Grid")
    .FirstOrDefault(grid => grid.Attribute("Margin")?.Value == "22,48,22,18");
Assert(outerHeaderRow?.Elements(presentation + "Grid.RowDefinitions")
           .Elements(presentation + "RowDefinition")
           .FirstOrDefault()?
           .Attribute("Height")?.Value == "44",
    "the optional update row must not resize the main layout");
Assert(updateButton.Attribute("Margin")?.Value == "0,98,22,0",
    "the update overlay must remain below the account row without changing the main layout");
Assert(xaml.Contains("Title=\"CosmoNet VPN\"", StringComparison.Ordinal)
       && xaml.Contains("Text=\"CosmoNet VPN\"", StringComparison.Ordinal),
    "the main window and visible header use the CosmoNet VPN title");
Assert(!xaml.Contains("Content=\"−\"", StringComparison.Ordinal)
       && !xaml.Contains("OnMinimizeClick", StringComparison.Ordinal)
       && !mainWindow.Contains("OnMinimizeClick", StringComparison.Ordinal),
    "the custom minimize control and its unused handler are removed");
Assert(mainWindow.Contains("e.Cancel = true;", StringComparison.Ordinal)
       && mainWindow.Contains("HideToTray();", StringComparison.Ordinal)
       && mainWindow.Contains("private void OnHideToTrayClick", StringComparison.Ordinal),
    "window close and the remaining title-bar X preserve close-to-tray behavior");
Assert(xaml.Contains("ResizeMode=\"CanMinimize\"", StringComparison.Ordinal)
       && xaml.Contains("WindowStyle=\"SingleBorderWindow\"", StringComparison.Ordinal)
       && xaml.Contains("shell:WindowChrome.WindowChrome", StringComparison.Ordinal)
       && xaml.Contains("UseAeroCaptionButtons=\"False\"", StringComparison.Ordinal)
       && !mainWindow.Contains("StateChanged += OnWindowStateChanged", StringComparison.Ordinal)
       && !mainWindow.Contains("OnWindowStateChanged", StringComparison.Ordinal)
       && !mainWindow.Contains("SetWindowRgn", StringComparison.Ordinal),
    "native taskbar minimization must retain standard window styles without hiding to tray or applying a region");
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

var lifecycle = new VpnConnectionLifecycle();
lifecycle.BeginConnection();
Assert(lifecycle.Phase == VpnConnectionPhase.Connecting,
    "a valid subscription starts in Connecting, not Connected");
lifecycle.BeginVerification();
Assert(lifecycle.Phase == VpnConnectionPhase.Verifying,
    "opened local Mihomo port must remain Verifying until an external probe succeeds");
Assert(!lifecycle.TryMarkConnected(processRunning: true, connectivityVerified: false)
       && lifecycle.Phase == VpnConnectionPhase.Error,
    "a failed or timed-out VPN probe must never create a Connected state");
lifecycle.BeginConnection();
lifecycle.BeginVerification();
Assert(lifecycle.TryMarkConnected(processRunning: true, connectivityVerified: true)
       && lifecycle.Phase == VpnConnectionPhase.Connected,
    "only a live Mihomo process and a successful VPN probe may create Connected");
Assert(lifecycle.TryHandleUnexpectedExit() && lifecycle.Phase == VpnConnectionPhase.Error,
    "unexpected Mihomo exit after Connected must clear the connected phase");
Assert(!lifecycle.TryHandleUnexpectedExit(),
    "duplicate process-exit callbacks must be ignored");
lifecycle.BeginConnection();
lifecycle.BeginVerification();
Assert(!lifecycle.TryMarkConnected(processRunning: false, connectivityVerified: true)
       && lifecycle.Phase == VpnConnectionPhase.Error,
    "Mihomo exit during verification must not create Connected");
lifecycle.BeginConnection();
lifecycle.BeginVerification();
Assert(lifecycle.TryMarkConnected(processRunning: true, connectivityVerified: true),
    "failed verification must leave lifecycle able to retry");
lifecycle.BeginDisconnect();
Assert(!lifecycle.TryHandleUnexpectedExit(),
    "intentional Stop must not be reported as a Mihomo crash");
lifecycle.MarkDisconnected();
Assert(lifecycle.Phase == VpnConnectionPhase.Disconnected,
    "normal Stop must finish in Disconnected");

var raceLifecycle = new VpnConnectionLifecycle();
raceLifecycle.BeginConnection();
raceLifecycle.BeginVerification();
Assert(raceLifecycle.TryMarkConnected(processRunning: true, connectivityVerified: true),
    "race test requires an established connection");
var handledExitCallbacks = new ConcurrentBag<bool>();
Parallel.For(0, 16, _ => handledExitCallbacks.Add(raceLifecycle.TryHandleUnexpectedExit()));
Assert(handledExitCallbacks.Count(result => result) == 1,
    "concurrent duplicate exit notifications must be handled exactly once");

var legacySettings = JsonSerializer.Deserialize<AppSettings>(
    "{\"StartMinimized\":true,\"TrafficMode\":0,\"AuthApiBaseUrl\":\"https://api.example/\"}");
Assert(legacySettings is not null
       && legacySettings.TrafficMode == TrafficMode.AllTraffic
       && legacySettings.AuthApiBaseUrl == "https://api.example/",
    "settings JSON with removed StartMinimized remains readable");

var singBoxBuilderSource = File.ReadAllText(Path.Combine(appRoot, "Services", "SingBoxConfigBuilder.cs"));
var proxyServiceSource = File.ReadAllText(Path.Combine(appRoot, "Services", "SystemProxyService.cs"));
Assert(!singBoxBuilderSource.Contains("WriteConfigAsync", StringComparison.Ordinal)
       && singBoxBuilderSource.Contains("WriteBootstrapConfigAsync", StringComparison.Ordinal)
       && singBoxBuilderSource.Contains("WriteBootstrapDesktopConfigAsync", StringComparison.Ordinal),
    "only the obsolete main sing-box config path is removed; Telegram bootstrap paths remain");
Assert(proxyServiceSource.Contains("public void Restore()", StringComparison.Ordinal)
       && !proxyServiceSource.Contains("EnableVpnProxy", StringComparison.Ordinal)
       && !proxyServiceSource.Contains("EnableBootstrapProxy", StringComparison.Ordinal),
    "legacy proxy enable methods are removed while Restore remains available");

var currentVersion = new Version(0, 2, 19);
var updateService = new WindowsReleaseService(new HttpClient(new StubHttpHandler(_ =>
    new HttpResponseMessage(HttpStatusCode.OK) { Content = JsonContent.Create(new
    {
        version = "0.2.20",
        telegramDeepLink = "tg://resolve?domain=CosmoNetBot&start=desktop_update",
        telegramWebLink = "https://web.telegram.org/k/#?tgaddr=tg%3A%2F%2Fresolve%3Fdomain%3DCosmoNetBot%26start%3Ddesktop_update",
    }) })));
var availableUpdate = await updateService.CheckForUpdateAsync("https://api.cosmonet.shop:18443/", currentVersion);
Assert(availableUpdate.Succeeded && availableUpdate.Update?.Version == new Version(0, 2, 20),
    "0.2.20 is a visible update for 0.2.19");
var updateState = new WindowsUpdateState();
Assert(!updateState.IsUpdateAvailable && updateState.ApplyVerifiedResult(availableUpdate)
       && updateState.IsUpdateAvailable,
    "a newer verified release makes the update-button state visible");

var sameVersion = await CreateReleaseCheckAsync("0.2.19", currentVersion);
Assert(sameVersion.Succeeded && sameVersion.Update is null,
    "equal semantic versions do not show an update");
Assert(updateState.ApplyVerifiedResult(sameVersion) && !updateState.IsUpdateAvailable,
    "a verified current release collapses the update-button state");
var olderVersion = await CreateReleaseCheckAsync("0.2.9", new Version(0, 2, 10));
Assert(olderVersion.Succeeded && olderVersion.Update is null,
    "0.2.9 is not newer than 0.2.10");
var malformedVersion = await CreateReleaseCheckAsync("not-a-version", currentVersion);
Assert(!malformedVersion.Succeeded && malformedVersion.Update is null,
    "malformed release versions are ignored");

var httpFailure = await new WindowsReleaseService(new HttpClient(new StubHttpHandler(_ =>
    new HttpResponseMessage(HttpStatusCode.ServiceUnavailable)))).CheckForUpdateAsync(
        "https://api.cosmonet.shop:18443/", currentVersion);
Assert(!httpFailure.Succeeded && httpFailure.Update is null,
    "HTTP errors leave normal application state unchanged");
updateState.ApplyVerifiedResult(availableUpdate);
Assert(!updateState.ApplyVerifiedResult(httpFailure) && updateState.IsUpdateAvailable,
    "a failed refresh preserves a previously verified update-button state");

using (var timeout = new CancellationTokenSource(TimeSpan.FromMilliseconds(20)))
{
    var timeoutResult = await new WindowsReleaseService(new HttpClient(new AsyncStubHttpHandler(async (_, cancellationToken) =>
    {
        await Task.Delay(Timeout.InfiniteTimeSpan, cancellationToken);
        return new HttpResponseMessage(HttpStatusCode.OK);
    }))).CheckForUpdateAsync("https://api.cosmonet.shop:18443/", currentVersion, timeout.Token);
    Assert(!timeoutResult.Succeeded && timeoutResult.Update is null,
        "API timeouts leave normal application state unchanged");
}

var launchAttempts = new List<Uri>();
var launcher = new TelegramLinkLauncher(uri =>
{
    launchAttempts.Add(uri);
    return launchAttempts.Count == 2;
});
Assert(availableUpdate.Update is not null && launcher.TryOpen(availableUpdate.Update)
       && launchAttempts.Count == 2
       && launchAttempts[0].Scheme == "tg"
       && launchAttempts[1].Host == "web.telegram.org",
    "update launch tries Telegram deep link before the trusted web fallback");

var updateGate = new WindowsUpdateCheckGate();
var entered = new ConcurrentBag<bool>();
Parallel.For(0, 8, _ => entered.Add(updateGate.TryEnter()));
Assert(entered.Count(value => value) == 1,
    "repeated periodic update checks cannot overlap");
updateGate.Exit();
Assert(updateGate.TryEnter(), "the update check gate is released after a completed request");
updateGate.Exit();

var temporaryDirectory = Path.Combine(Path.GetTempPath(), $"CosmoNet-tests-{Guid.NewGuid():N}");
Directory.CreateDirectory(temporaryDirectory);
try
{
    var generatedConfigPath = Path.Combine(temporaryDirectory, "mihomo.yaml");
    var configStore = new MihomoConfigFileStore(generatedConfigPath);
    await File.WriteAllTextAsync(generatedConfigPath, "sensitive-vpn-config");
    Assert(File.Exists(generatedConfigPath),
        "generated Mihomo config exists while a runtime session requires it");
    Assert(configStore.TryDelete() && !File.Exists(generatedConfigPath),
        "normal disconnect cleanup removes the generated Mihomo config");
    Assert(configStore.TryDelete(),
        "cleanup of a missing generated Mihomo config is harmless");

    await File.WriteAllTextAsync(generatedConfigPath, "locked-config");
    using (var heldConfig = new FileStream(generatedConfigPath, FileMode.Open, FileAccess.Read, FileShare.Read))
    {
        Assert(!configStore.TryDelete() && File.Exists(generatedConfigPath),
            "config deletion failure is harmless and does not crash cleanup");
    }
    Assert(configStore.TryDelete(),
        "generated config remains removable after a transient deletion failure");

    await File.WriteAllTextAsync(generatedConfigPath, "failed-startup-config");
    Assert(configStore.TryDelete() && !File.Exists(generatedConfigPath),
        "failed Mihomo startup cleanup removes the generated config");
    await File.WriteAllTextAsync(generatedConfigPath, "failed-verification-config");
    Assert(configStore.TryDelete() && !File.Exists(generatedConfigPath),
        "failed VPN verification cleanup removes the generated config");
    await File.WriteAllTextAsync(generatedConfigPath, "stale-config");
    Assert(configStore.TryDelete() && !File.Exists(generatedConfigPath),
        "stale generated Mihomo config is removed at application startup");

    var secretPath = Path.Combine(temporaryDirectory, "secrets.dat");
    var secrets = new SecretSettingsStore(secretPath);
    var originalSecrets = new SecretSettings
    {
        SubscriptionUrl = "https://subscription.example/test-secret",
        AuthToken = "test-auth-token",
        AuthDeviceId = "device-id"
    };
    await secrets.SaveAsync(originalSecrets);
    var existingSecrets = await new SecretSettingsStore(secretPath).LoadAsync();
    Assert(existingSecrets.AuthToken == originalSecrets.AuthToken
           && existingSecrets.SubscriptionUrl == originalSecrets.SubscriptionUrl,
        "existing DPAPI-protected secrets remain readable");

    var temporaryFileWasProtected = false;
    secrets.OnTemporaryFileWrittenAsync = async (temporaryPath, cancellationToken) =>
    {
        var temporaryBytes = await File.ReadAllBytesAsync(temporaryPath, cancellationToken);
        temporaryFileWasProtected = !Encoding.UTF8.GetString(temporaryBytes)
            .Contains("test-auth-token", StringComparison.Ordinal);
        throw new IOException("simulated interrupted atomic save");
    };
    try
    {
        await secrets.SaveAsync(new SecretSettings { AuthToken = "replacement-token" });
        throw new InvalidOperationException("simulated interrupted atomic save must fail");
    }
    catch (IOException)
    {
    }

    Assert(temporaryFileWasProtected,
        "temporary secrets file contains DPAPI-protected bytes, not plaintext");
    var preservedSecrets = await new SecretSettingsStore(secretPath).LoadAsync();
    Assert(preservedSecrets.AuthToken == originalSecrets.AuthToken,
        "an interrupted save preserves the previous readable secrets file");
    Assert(!Directory.EnumerateFiles(temporaryDirectory, "secrets.dat.*.tmp").Any(),
        "failed atomic save cleans its protected temporary file");

    secrets.OnTemporaryFileWrittenAsync = null;
    await secrets.SaveAsync(new SecretSettings { AuthToken = "latest-token" });
    var latestSecrets = await new SecretSettingsStore(secretPath).LoadAsync();
    Assert(latestSecrets.AuthToken == "latest-token",
        "atomic replacement preserves the latest successful secrets data");
}
finally
{
    Directory.Delete(temporaryDirectory, recursive: true);
}

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

async Task<WindowsUpdateCheckResult> CreateReleaseCheckAsync(string version, Version localVersion)
{
    var service = new WindowsReleaseService(new HttpClient(new StubHttpHandler(_ =>
        new HttpResponseMessage(HttpStatusCode.OK) { Content = JsonContent.Create(new
        {
            version,
            telegramDeepLink = "tg://resolve?domain=CosmoNetBot&start=desktop_update",
            telegramWebLink = "https://web.telegram.org/k/#?tgaddr=tg%3A%2F%2Fresolve%3Fdomain%3DCosmoNetBot%26start%3Ddesktop_update",
        }) })));
    return await service.CheckForUpdateAsync("https://api.cosmonet.shop:18443/", localVersion);
}

sealed class StubHttpHandler(Func<HttpRequestMessage, HttpResponseMessage> responseFactory) : HttpMessageHandler
{
    protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        => Task.FromResult(responseFactory(request));
}

sealed class AsyncStubHttpHandler(Func<HttpRequestMessage, CancellationToken, Task<HttpResponseMessage>> responseFactory) : HttpMessageHandler
{
    protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        => responseFactory(request, cancellationToken);
}
