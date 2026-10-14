using System.Text.Json;
using CosmoNet.App.Models;

namespace CosmoNet.App.Services;

public sealed class SettingsStore
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true
    };

    private readonly SecretSettingsStore _secretSettingsStore = new();

    public async Task<AppSettings> LoadAsync(CancellationToken cancellationToken = default)
    {
        AppPaths.EnsureDataDirectory();

        var settings = await LoadPublicSettingsAsync(cancellationToken);
        var secrets = await _secretSettingsStore.LoadAsync(cancellationToken);
        settings.SubscriptionUrl = !string.IsNullOrWhiteSpace(secrets.SubscriptionUrl)
            ? secrets.SubscriptionUrl
            : await TryReadLegacySubscriptionUrlAsync(cancellationToken);

        return settings;
    }

    public async Task SaveAsync(AppSettings settings, CancellationToken cancellationToken = default)
    {
        AppPaths.EnsureDataDirectory();
        settings.HasExplicitTrafficModeChoice = true;
        settings.TrafficModeConfigurationVersion = AppSettings.CurrentTrafficModeConfigurationVersion;

        var temporaryPath = $"{AppPaths.SettingsPath}.{Guid.NewGuid():N}.tmp";
        try
        {
            await using (var stream = new FileStream(
                temporaryPath,
                FileMode.CreateNew,
                FileAccess.Write,
                FileShare.None,
                bufferSize: 4096,
                useAsync: true))
            {
                await JsonSerializer.SerializeAsync(stream, settings, JsonOptions, cancellationToken);
                await stream.FlushAsync(cancellationToken);
            }

            if (File.Exists(AppPaths.SettingsPath))
            {
                File.Replace(temporaryPath, AppPaths.SettingsPath, destinationBackupFileName: null);
            }
            else
            {
                File.Move(temporaryPath, AppPaths.SettingsPath);
            }
        }
        finally
        {
            if (File.Exists(temporaryPath))
            {
                File.Delete(temporaryPath);
            }
        }

        var secrets = await _secretSettingsStore.LoadAsync(cancellationToken);
        secrets.SubscriptionUrl = settings.SubscriptionUrl.Trim();
        await _secretSettingsStore.SaveAsync(secrets, cancellationToken);
    }

    private static async Task<AppSettings> LoadPublicSettingsAsync(CancellationToken cancellationToken)
    {
        if (!File.Exists(AppPaths.SettingsPath))
        {
            return new AppSettings();
        }

        AppSettings settings;
        try
        {
            await using var stream = File.OpenRead(AppPaths.SettingsPath);
            settings = await JsonSerializer.DeserializeAsync<AppSettings>(stream, JsonOptions, cancellationToken)
                ?? new AppSettings();
        }
        catch (JsonException)
        {
            PreserveCorruptSettings();
            return new AppSettings();
        }

        if (settings.TrafficModeConfigurationVersion < AppSettings.CurrentTrafficModeConfigurationVersion)
        {
            settings.TrafficMode = TrafficMode.AllTraffic;
        }

        return settings;
    }

    private static void PreserveCorruptSettings()
    {
        try
        {
            var backupPath = Path.Combine(
                AppPaths.DataDirectory,
                $"settings.corrupt-{DateTimeOffset.UtcNow:yyyyMMddHHmmss}-{Guid.NewGuid():N}.json");
            File.Move(AppPaths.SettingsPath, backupPath);
        }
        catch (IOException)
        {
            // A subsequent save will replace the invalid file if it could not be archived now.
        }
        catch (UnauthorizedAccessException)
        {
            // Settings recovery must not prevent the application from starting.
        }
    }

    private static async Task<string> TryReadLegacySubscriptionUrlAsync(CancellationToken cancellationToken)
    {
        if (!File.Exists(AppPaths.SettingsPath))
        {
            return "";
        }

        try
        {
            await using var stream = File.OpenRead(AppPaths.SettingsPath);
            using var document = await JsonDocument.ParseAsync(stream, cancellationToken: cancellationToken);
            return document.RootElement.TryGetProperty("SubscriptionUrl", out var property)
                ? property.GetString() ?? ""
                : "";
        }
        catch (JsonException)
        {
            return "";
        }
    }
}
