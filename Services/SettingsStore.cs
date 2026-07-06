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

        await using (var stream = File.Create(AppPaths.SettingsPath))
        {
            await JsonSerializer.SerializeAsync(stream, settings, JsonOptions, cancellationToken);
        }

        await _secretSettingsStore.SaveAsync(new SecretSettings
        {
            SubscriptionUrl = settings.SubscriptionUrl.Trim()
        }, cancellationToken);
    }

    private static async Task<AppSettings> LoadPublicSettingsAsync(CancellationToken cancellationToken)
    {
        if (!File.Exists(AppPaths.SettingsPath))
        {
            return new AppSettings();
        }

        await using var stream = File.OpenRead(AppPaths.SettingsPath);
        return await JsonSerializer.DeserializeAsync<AppSettings>(stream, JsonOptions, cancellationToken)
            ?? new AppSettings();
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
