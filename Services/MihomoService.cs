using System.ComponentModel;
using System.Diagnostics;
using System.Security.Principal;
using CosmoNet.App.Models;

namespace CosmoNet.App.Services;

public sealed class MihomoService
{
    private Process? _process;

    public bool IsRunning => _process is { HasExited: false };
    public bool IsCoreAvailable => File.Exists(AppPaths.BundledMihomoPath);

    public bool IsAdministrator
    {
        get
        {
            using var identity = WindowsIdentity.GetCurrent();
            var principal = new WindowsPrincipal(identity);
            return principal.IsInRole(WindowsBuiltInRole.Administrator);
        }
    }

    public FileStream LockConfigForExecution(string configPath) => new(configPath, FileMode.Open, FileAccess.Read, FileShare.Read);

    public async Task<CoreDiagnosticResult> CheckConfigAsync(string configPath, CancellationToken cancellationToken = default)
    {
        if (!IsCoreAvailable)
        {
            return CoreDiagnosticResult.Fail("Ядро Mihomo не найдено.", AppPaths.BundledMihomoPath);
        }

        if (!File.Exists(configPath))
        {
            return CoreDiagnosticResult.Fail("Сгенерированный конфиг не найден.", configPath);
        }

        using var process = new Process
        {
            StartInfo = new ProcessStartInfo
            {
                FileName = AppPaths.BundledMihomoPath,
                Arguments = $"-t -f \"{configPath}\"",
                CreateNoWindow = true,
                UseShellExecute = false,
                RedirectStandardError = true,
                RedirectStandardOutput = true,
                WorkingDirectory = Path.GetDirectoryName(AppPaths.BundledMihomoPath)
            }
        };
        process.Start();

        var outputTask = process.StandardOutput.ReadToEndAsync(cancellationToken);
        var errorTask = process.StandardError.ReadToEndAsync(cancellationToken);
        var exitTask = process.WaitForExitAsync(cancellationToken);
        var timeoutTask = Task.Delay(TimeSpan.FromSeconds(12), cancellationToken);
        if (await Task.WhenAny(exitTask, timeoutTask) == timeoutTask)
        {
            try { process.Kill(entireProcessTree: true); } catch (InvalidOperationException) { }
            return CoreDiagnosticResult.Fail("Проверка конфигурации Mihomo не завершилась вовремя.", configPath);
        }

        var details = string.Join(Environment.NewLine, new[] { await outputTask, await errorTask }
            .Where(text => !string.IsNullOrWhiteSpace(text))).Trim();
        return process.ExitCode == 0
            ? CoreDiagnosticResult.Ok("Конфигурация Mihomo прошла проверку.", details)
            : CoreDiagnosticResult.Fail("Конфигурация Mihomo не прошла проверку.", details);
    }

    public async Task StartAsync(string configPath, bool useTunMode, CancellationToken cancellationToken = default)
    {
        if (IsRunning) return;
        if (!IsCoreAvailable)
        {
            throw new FileNotFoundException("Не найден mihomo.exe.", AppPaths.BundledMihomoPath);
        }

        var startElevated = useTunMode && !IsAdministrator;
        var startInfo = new ProcessStartInfo
        {
            FileName = AppPaths.BundledMihomoPath,
            Arguments = $"-f \"{configPath}\"",
            CreateNoWindow = true,
            WindowStyle = ProcessWindowStyle.Hidden,
            UseShellExecute = startElevated,
            RedirectStandardError = !startElevated,
            RedirectStandardOutput = !startElevated,
            WorkingDirectory = Path.GetDirectoryName(AppPaths.BundledMihomoPath)
        };
        if (startElevated) startInfo.Verb = "runas";

        try { _process = Process.Start(startInfo); }
        catch (Win32Exception exception) when (exception.NativeErrorCode == 1223)
        {
            throw new InvalidOperationException("Подключение отменено: подтвердите запрос Windows на запуск VPN.");
        }
        if (_process is null) throw new InvalidOperationException("Не удалось запустить Mihomo.");

        var outputTask = startElevated ? null : _process.StandardOutput.ReadToEndAsync(cancellationToken);
        var errorTask = startElevated ? null : _process.StandardError.ReadToEndAsync(cancellationToken);
        await Task.Delay(TimeSpan.FromSeconds(1), cancellationToken);
        if (!_process.HasExited) return;

        var details = "";
        if (!startElevated)
        {
            details = string.Join(Environment.NewLine, new[] { await outputTask!, await errorTask! }
                .Where(text => !string.IsNullOrWhiteSpace(text))).Trim();
        }
        _process.Dispose();
        _process = null;
        throw new InvalidOperationException(string.IsNullOrWhiteSpace(details)
            ? "Mihomo завершился сразу после запуска."
            : $"Mihomo не запустился: {details}");
    }

    public void Stop()
    {
        var process = _process;
        _process = null;
        if (process is null) return;
        try
        {
            if (!process.HasExited)
            {
                process.Kill(entireProcessTree: true);
                process.WaitForExit(5000);
            }
        }
        finally { process.Dispose(); }
    }
}