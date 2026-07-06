using System.Diagnostics;
using System.Security.Principal;

namespace CosmoNet.App.Services;

public sealed class SingBoxService
{
    private Process? _process;

    public bool IsRunning => _process is { HasExited: false };
    public bool IsCoreAvailable => File.Exists(AppPaths.BundledSingBoxPath);

    public bool IsAdministrator
    {
        get
        {
            using var identity = WindowsIdentity.GetCurrent();
            var principal = new WindowsPrincipal(identity);
            return principal.IsInRole(WindowsBuiltInRole.Administrator);
        }
    }

    public void Start(string configPath, bool useTunMode)
    {
        if (IsRunning)
        {
            return;
        }

        if (!IsCoreAvailable)
        {
            throw new FileNotFoundException(
                "Не найден sing-box.exe. Положите ядро в Resources\\sing-box\\sing-box.exe.",
                AppPaths.BundledSingBoxPath);
        }

        if (useTunMode && !IsAdministrator)
        {
            throw new InvalidOperationException("Для TUN-режима запустите CosmoNet от имени администратора.");
        }

        _process = Process.Start(new ProcessStartInfo
        {
            FileName = AppPaths.BundledSingBoxPath,
            Arguments = $"run -c \"{configPath}\"",
            CreateNoWindow = true,
            UseShellExecute = false,
            RedirectStandardError = true,
            RedirectStandardOutput = true,
            WorkingDirectory = Path.GetDirectoryName(AppPaths.BundledSingBoxPath)
        });

        if (_process is null)
        {
            throw new InvalidOperationException("Не удалось запустить sing-box.");
        }
    }

    public void Stop()
    {
        if (!IsRunning)
        {
            return;
        }

        _process?.Kill(entireProcessTree: true);
        _process?.Dispose();
        _process = null;
    }
}
