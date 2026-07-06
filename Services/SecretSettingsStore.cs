using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using CosmoNet.App.Models;

namespace CosmoNet.App.Services;

public sealed class SecretSettingsStore
{
    private static readonly byte[] Entropy = Encoding.UTF8.GetBytes("CosmoNet.Windows.App.v1");
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = false
    };

    public async Task<SecretSettings> LoadAsync(CancellationToken cancellationToken = default)
    {
        AppPaths.EnsureDataDirectory();

        if (!File.Exists(AppPaths.SecretSettingsPath))
        {
            return new SecretSettings();
        }

        var protectedBytes = await File.ReadAllBytesAsync(AppPaths.SecretSettingsPath, cancellationToken);
        if (protectedBytes.Length == 0)
        {
            return new SecretSettings();
        }

        try
        {
            var jsonBytes = ProtectedData.Unprotect(protectedBytes, Entropy, DataProtectionScope.CurrentUser);
            return JsonSerializer.Deserialize<SecretSettings>(jsonBytes, JsonOptions) ?? new SecretSettings();
        }
        catch (CryptographicException)
        {
            return new SecretSettings();
        }
    }

    public async Task SaveAsync(SecretSettings settings, CancellationToken cancellationToken = default)
    {
        AppPaths.EnsureDataDirectory();
        var jsonBytes = JsonSerializer.SerializeToUtf8Bytes(settings, JsonOptions);
        var protectedBytes = ProtectedData.Protect(jsonBytes, Entropy, DataProtectionScope.CurrentUser);
        await File.WriteAllBytesAsync(AppPaths.SecretSettingsPath, protectedBytes, cancellationToken);
    }
}
