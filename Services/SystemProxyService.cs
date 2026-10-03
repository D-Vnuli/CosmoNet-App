using System.Runtime.InteropServices;
using System.Text.Json;
using Microsoft.Win32;

namespace CosmoNet.App.Services;

public sealed class SystemProxyService
{
    public const int BootstrapPort = 20808;
    public const int VpnPort = 20809;
    private const string InternetSettingsPath = @"Software\Microsoft\Windows\CurrentVersion\Internet Settings";
    private static readonly string StatePath = Path.Combine(AppPaths.DataDirectory, "bootstrap-proxy-state.json");

    public void Restore()
    {
        if (!File.Exists(StatePath)) return;

        var state = JsonSerializer.Deserialize<ProxyState>(File.ReadAllText(StatePath));
        if (state is null) return;
        using var key = Registry.CurrentUser.CreateSubKey(InternetSettingsPath, writable: true);
        if (key is null) return;

        RestoreValue(key, "ProxyEnable", state.ProxyEnable, RegistryValueKind.DWord);
        RestoreValue(key, "ProxyServer", state.ProxyServer, RegistryValueKind.String);
        RestoreValue(key, "ProxyOverride", state.ProxyOverride, RegistryValueKind.String);
        File.Delete(StatePath);
    }

    [DllImport("wininet.dll", SetLastError = true)]
    private static extern bool InternetSetOption(IntPtr internetHandle, int option, IntPtr buffer, int bufferLength);

    private static void NotifyProxySettingsChanged()
    {
        const int InternetOptionSettingsChanged = 39;
        const int InternetOptionRefresh = 37;
        InternetSetOption(IntPtr.Zero, InternetOptionSettingsChanged, IntPtr.Zero, 0);
        InternetSetOption(IntPtr.Zero, InternetOptionRefresh, IntPtr.Zero, 0);
    }
    private static void RestoreValue(RegistryKey key, string name, object? value, RegistryValueKind kind)
    {
        if (value is null) key.DeleteValue(name, throwOnMissingValue: false);
        else key.SetValue(name, value, kind);
    }

    private sealed record ProxyState(int? ProxyEnable, string? ProxyServer, string? ProxyOverride);
}
