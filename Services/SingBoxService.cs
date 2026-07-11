using System.ComponentModel;
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

    public async Task StartAsync(
        string configPath,
        bool useTunMode,
        CancellationToken cancellationToken = default)
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

        var startElevated = useTunMode && !IsAdministrator;
        var startInfo = new ProcessStartInfo
        {
            FileName = AppPaths.BundledSingBoxPath,
            Arguments = $"run -c \"{configPath}\"",
            CreateNoWindow = true,
            UseShellExecute = startElevated,
            RedirectStandardError = !startElevated,
            RedirectStandardOutput = !startElevated,
            WorkingDirectory = Path.GetDirectoryName(AppPaths.BundledSingBoxPath)
        };

        if (startElevated)
        {
            startInfo.Verb = "runas";
        }

        try
        {
            _process = Process.Start(startInfo);
        }
        catch (Win32Exception exception) when (exception.NativeErrorCode == 1223)
        {
            throw new InvalidOperationException("Подключение отменено: подтвердите запрос Windows на запуск VPN.");
        }

        if (_process is null)
        {
            throw new InvalidOperationException("Не удалось запустить sing-box.");
        }

        var outputTask = startElevated
            ? null
            : _process.StandardOutput.ReadToEndAsync(cancellationToken);
        var errorTask = startElevated
            ? null
            : _process.StandardError.ReadToEndAsync(cancellationToken);
        await Task.Delay(TimeSpan.FromSeconds(1), cancellationToken);

        if (!_process.HasExited)
        {
            return;
        }

        var details = "";
        if (!startElevated)
        {
            var output = await outputTask!;
            var error = await errorTask!;
            details = string.Join(
                Environment.NewLine,
                new[] { output, error }.Where(text => !string.IsNullOrWhiteSpace(text))).Trim();
        }

        _process.Dispose();
        _process = null;
        throw new InvalidOperationException(
            string.IsNullOrWhiteSpace(details)
                ? "sing-box завершился сразу после запуска."
                : $"sing-box не запустился: {details}");
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
