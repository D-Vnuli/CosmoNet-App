using System.Diagnostics;
using System.Security.Principal;
using CosmoNet.App.Models;

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

    public async Task<CoreDiagnosticResult> CheckConfigAsync(
        string configPath,
        CancellationToken cancellationToken = default)
    {
        if (!IsCoreAvailable)
        {
            return CoreDiagnosticResult.Fail(
                "Ядро sing-box не найдено.",
                AppPaths.BundledSingBoxPath);
        }

        if (!File.Exists(configPath))
        {
            return CoreDiagnosticResult.Fail(
                "Сгенерированный конфиг не найден.",
                configPath);
        }

        using var process = new Process
        {
            StartInfo = new ProcessStartInfo
            {
                FileName = AppPaths.BundledSingBoxPath,
                Arguments = $"check -c \"{configPath}\"",
                CreateNoWindow = true,
                UseShellExecute = false,
                RedirectStandardError = true,
                RedirectStandardOutput = true,
                WorkingDirectory = Path.GetDirectoryName(AppPaths.BundledSingBoxPath)
            }
        };

        process.Start();

        var outputTask = process.StandardOutput.ReadToEndAsync(cancellationToken);
        var errorTask = process.StandardError.ReadToEndAsync(cancellationToken);
        var exitTask = process.WaitForExitAsync(cancellationToken);
        var timeoutTask = Task.Delay(TimeSpan.FromSeconds(12), cancellationToken);

        if (await Task.WhenAny(exitTask, timeoutTask) == timeoutTask)
        {
            try
            {
                process.Kill(entireProcessTree: true);
            }
            catch (InvalidOperationException)
            {
            }

            return CoreDiagnosticResult.Fail(
                "Проверка sing-box зависла и была остановлена.",
                configPath);
        }

        var output = await outputTask;
        var error = await errorTask;
        var details = string.Join(Environment.NewLine, new[] { output, error }.Where(text => !string.IsNullOrWhiteSpace(text))).Trim();

        return process.ExitCode == 0
            ? CoreDiagnosticResult.Ok("Конфиг sing-box прошел проверку.", details)
            : CoreDiagnosticResult.Fail("Конфиг sing-box не прошел проверку.", details);
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
