namespace CosmoNet.App.Models;

public sealed record WindowsReleaseInfo(
    Version Version,
    Uri TelegramDeepLink,
    Uri TelegramWebLink);

public sealed record WindowsUpdateCheckResult(bool Succeeded, WindowsReleaseInfo? Update)
{
    public static WindowsUpdateCheckResult Failed { get; } = new(false, null);
}
