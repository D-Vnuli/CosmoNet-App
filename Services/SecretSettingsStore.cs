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
    private readonly string _secretSettingsPath;

    internal Func<string, CancellationToken, Task>? OnTemporaryFileWrittenAsync { get; set; }

    public SecretSettingsStore(string? secretSettingsPath = null)
    {
        _secretSettingsPath = Path.GetFullPath(secretSettingsPath ?? AppPaths.SecretSettingsPath);
    }

    public async Task<SecretSettings> LoadAsync(CancellationToken cancellationToken = default)
    {
        EnsureStorageDirectory();

        if (!File.Exists(_secretSettingsPath))
        {
            return new SecretSettings();
        }

        var protectedBytes = await File.ReadAllBytesAsync(_secretSettingsPath, cancellationToken);
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
        EnsureStorageDirectory();
        var jsonBytes = JsonSerializer.SerializeToUtf8Bytes(settings, JsonOptions);
        var protectedBytes = ProtectedData.Protect(jsonBytes, Entropy, DataProtectionScope.CurrentUser);
        var temporaryPath = $"{_secretSettingsPath}.{Guid.NewGuid():N}.tmp";
        try
        {
            await using (var stream = new FileStream(
                temporaryPath,
                FileMode.CreateNew,
                FileAccess.Write,
                FileShare.None,
                bufferSize: 4096,
                FileOptions.Asynchronous | FileOptions.WriteThrough))
            {
                await stream.WriteAsync(protectedBytes, cancellationToken);
                await stream.FlushAsync(cancellationToken);
                stream.Flush(flushToDisk: true);
            }

            if (OnTemporaryFileWrittenAsync is { } onTemporaryFileWritten)
            {
                await onTemporaryFileWritten(temporaryPath, cancellationToken);
            }

            if (File.Exists(_secretSettingsPath))
            {
                File.Replace(temporaryPath, _secretSettingsPath, destinationBackupFileName: null);
            }
            else
            {
                File.Move(temporaryPath, _secretSettingsPath);
            }
        }
        finally
        {
            try
            {
                if (File.Exists(temporaryPath))
                {
                    File.Delete(temporaryPath);
                }
            }
            catch (IOException)
            {
                // A failed cleanup must not replace or damage the existing secrets file.
            }
            catch (UnauthorizedAccessException)
            {
                // A failed cleanup must not replace or damage the existing secrets file.
            }
        }
    }

    private void EnsureStorageDirectory()
    {
        var directory = Path.GetDirectoryName(_secretSettingsPath)
            ?? throw new InvalidOperationException("Не удалось определить каталог секретов.");
        Directory.CreateDirectory(directory);
    }
}
