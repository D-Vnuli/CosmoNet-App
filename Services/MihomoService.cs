using System.ComponentModel;
using System.Diagnostics;
using System.Net;
using System.Net.Sockets;
using System.Security.Principal;
using CosmoNet.App.Models;

namespace CosmoNet.App.Services;

public sealed class MihomoService
{
    private const int MixedProxyPort = 20809;
    private static readonly TimeSpan CoreStartTimeout = TimeSpan.FromSeconds(10);
    private static readonly TimeSpan ProcessStartTimeout = TimeSpan.FromSeconds(8);
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
        if (await IsLocalPortOpenAsync(cancellationToken))
        {
            throw new InvalidOperationException($"Локальный VPN-порт {MixedProxyPort} уже занят другим приложением.");
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

        Process? process;
        try { process = await StartProcessAsync(startInfo, cancellationToken); }
        catch (Win32Exception exception) when (exception.NativeErrorCode == 1223)
        {
            throw new InvalidOperationException("Подключение отменено: подтвердите запрос Windows на запуск VPN.");
        }
        if (process is null) throw new InvalidOperationException("Не удалось запустить Mihomo.");

        var outputTask = startElevated ? null : process.StandardOutput.ReadToEndAsync(cancellationToken);
        var errorTask = startElevated ? null : process.StandardError.ReadToEndAsync(cancellationToken);

        var processHandled = false;
        try
        {
            if (await WaitForProxyPortAsync(process, cancellationToken))
            {
                _process = process;
                return;
            }

            var details = await StopAndReadDetailsAsync(process, outputTask, errorTask);
            processHandled = true;
            throw new InvalidOperationException(string.IsNullOrWhiteSpace(details)
                ? $"Mihomo не открыл локальный VPN-порт {MixedProxyPort} за {CoreStartTimeout.TotalSeconds:0} сек."
                : $"Mihomo не запустился: {details}");
        }
        catch
        {
            if (!processHandled && !ReferenceEquals(_process, process))
            {
                await StopAndReadDetailsAsync(process, outputTask, errorTask);
            }

            throw;
        }
    }

    private static async Task<Process?> StartProcessAsync(ProcessStartInfo startInfo, CancellationToken cancellationToken)
    {
        var startTask = Task.Run(() => Process.Start(startInfo), CancellationToken.None);
        var timeoutTask = Task.Delay(ProcessStartTimeout, cancellationToken);
        if (await Task.WhenAny(startTask, timeoutTask) != startTask)
        {
            _ = startTask.ContinueWith(task =>
            {
                if (task.Status == TaskStatus.RanToCompletion && task.Result is { HasExited: false } lateProcess)
                {
                    try { lateProcess.Kill(entireProcessTree: true); } catch { }
                    lateProcess.Dispose();
                }
            }, TaskScheduler.Default);
            cancellationToken.ThrowIfCancellationRequested();
            throw new TimeoutException("Windows не ответила на запрос запуска VPN-ядра.");
        }

        return await startTask;
    }

    private static async Task<bool> WaitForProxyPortAsync(Process process, CancellationToken cancellationToken)
    {
        var stopwatch = Stopwatch.StartNew();
        while (stopwatch.Elapsed < CoreStartTimeout)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (process.HasExited) return false;
            if (await IsLocalPortOpenAsync(cancellationToken)) return true;
            await Task.Delay(200, cancellationToken);
        }

        return false;
    }

    private static async Task<bool> IsLocalPortOpenAsync(CancellationToken cancellationToken)
    {
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeout.CancelAfter(TimeSpan.FromMilliseconds(500));
        using var client = new TcpClient();
        try
        {
            await client.ConnectAsync(IPAddress.Loopback, MixedProxyPort, timeout.Token);
            return true;
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            return false;
        }
        catch (SocketException)
        {
            return false;
        }
    }

    private static async Task<string> StopAndReadDetailsAsync(Process process, Task<string>? outputTask, Task<string>? errorTask)
    {
        try
        {
            if (!process.HasExited)
            {
                process.Kill(entireProcessTree: true);
                await process.WaitForExitAsync();
            }

            if (outputTask is null || errorTask is null) return "";
            return string.Join(Environment.NewLine, new[] { await outputTask, await errorTask }
                .Where(text => !string.IsNullOrWhiteSpace(text))).Trim();
        }
        catch
        {
            return "";
        }
        finally
        {
            process.Dispose();
        }
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
