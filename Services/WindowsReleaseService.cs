using System.Diagnostics;
using System.Net.Http;
using System.Net.Http.Json;
using System.Text.Json;
using CosmoNet.App.Models;

namespace CosmoNet.App.Services;

public sealed class WindowsReleaseService
{
    private readonly HttpClient _httpClient;

    public WindowsReleaseService(HttpClient? httpClient = null)
    {
        _httpClient = httpClient ?? new HttpClient { Timeout = TimeSpan.FromSeconds(8) };
    }

    public async Task<WindowsUpdateCheckResult> CheckForUpdateAsync(
        string baseUrl,
        Version currentVersion,
        CancellationToken cancellationToken = default)
    {
        try
        {
            var responseUri = new Uri(BaseUri(baseUrl), "api/app/windows-release");
            using var response = await _httpClient.GetAsync(responseUri, cancellationToken);
            if (!response.IsSuccessStatusCode)
            {
                return WindowsUpdateCheckResult.Failed;
            }

            var payload = await response.Content.ReadFromJsonAsync<WindowsReleaseResponse>(
                new JsonSerializerOptions(JsonSerializerDefaults.Web), cancellationToken);
            if (payload is null
                || !Version.TryParse(payload.Version, out var remoteVersion)
                || remoteVersion is null
                || !HasThreeVersionParts(payload.Version)
                || !TryGetTrustedLinks(payload, out var release))
            {
                return WindowsUpdateCheckResult.Failed;
            }

            return remoteVersion > currentVersion
                ? new WindowsUpdateCheckResult(true, release with { Version = remoteVersion })
                : new WindowsUpdateCheckResult(true, null);
        }
        catch (Exception exception) when (exception is HttpRequestException
                                         or TaskCanceledException
                                         or JsonException
                                         or InvalidOperationException)
        {
            return WindowsUpdateCheckResult.Failed;
        }
    }

    private static Uri BaseUri(string value)
    {
        var uri = SecurityPolicy.RequireCosmoNetApi(value);
        var builder = new UriBuilder(uri);
        if (!builder.Path.EndsWith('/')) builder.Path += "/";
        return builder.Uri;
    }

    private static bool HasThreeVersionParts(string? value)
    {
        return value?.Split('.').Length == 3
               && value.Split('.').All(part => int.TryParse(part, out _));
    }

    private static bool TryGetTrustedLinks(WindowsReleaseResponse payload, out WindowsReleaseInfo release)
    {
        release = default!;
        if (!Version.TryParse(payload.Version, out var version) || version is null)
        {
            return false;
        }

        try
        {
            release = new WindowsReleaseInfo(
                version,
                SecurityPolicy.RequireTelegramDeepLink(payload.TelegramDeepLink ?? ""),
                SecurityPolicy.RequireTelegramWebLink(payload.TelegramWebLink ?? ""));
            return true;
        }
        catch (InvalidOperationException)
        {
            return false;
        }
    }

    private sealed class WindowsReleaseResponse
    {
        public string? Version { get; init; }
        public string? TelegramDeepLink { get; init; }
        public string? TelegramWebLink { get; init; }
    }
}

public sealed class TelegramLinkLauncher
{
    private readonly Func<Uri, bool> _openLink;

    public TelegramLinkLauncher(Func<Uri, bool>? openLink = null)
    {
        _openLink = openLink ?? OpenWithShell;
    }

    public bool TryOpen(WindowsReleaseInfo release)
    {
        try
        {
            if (_openLink(release.TelegramDeepLink))
            {
                return true;
            }
        }
        catch
        {
        }

        try
        {
            return _openLink(release.TelegramWebLink);
        }
        catch
        {
            return false;
        }
    }

    private static bool OpenWithShell(Uri link)
    {
        Process.Start(new ProcessStartInfo(link.AbsoluteUri) { UseShellExecute = true });
        return true;
    }
}

public sealed class WindowsUpdateCheckGate
{
    private int _running;

    public bool TryEnter() => Interlocked.CompareExchange(ref _running, 1, 0) == 0;

    public void Exit() => Volatile.Write(ref _running, 0);
}

public sealed class WindowsUpdateState
{
    public WindowsReleaseInfo? AvailableUpdate { get; private set; }
    public bool IsUpdateAvailable => AvailableUpdate is not null;

    public bool ApplyVerifiedResult(WindowsUpdateCheckResult result)
    {
        return result.Succeeded && SetVerifiedUpdate(result.Update);
    }

    public bool SetVerifiedUpdate(WindowsReleaseInfo? update)
    {
        if (ReferenceEquals(AvailableUpdate, update))
        {
            return false;
        }

        AvailableUpdate = update;
        return true;
    }
}
