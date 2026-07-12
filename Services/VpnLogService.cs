using System.Text.RegularExpressions;
using System.Windows.Threading;

namespace CosmoNet.App.Services;

public sealed class VpnLogService : IDisposable
{
    private static readonly Regex IPv4Address = new(@"(?<![\d.])(\d{1,3})\.(\d{1,3})\.\d{1,3}\.\d{1,3}(?![\d.])", RegexOptions.Compiled);
    private static readonly Regex ConnectionDestination = new(@"(?i)(outbound connection (?:to|established)).*", RegexOptions.Compiled);
    private readonly DispatcherTimer _timer;
    private long _position;
    private bool _isReading;

    public VpnLogService()
    {
        _timer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(500) };
        _timer.Tick += OnTimerTick;
    }

    public event EventHandler<string>? LogReceived;

    public bool IsMonitoring => _timer.IsEnabled;

    public async Task<string> StartAsync()
    {
        AppPaths.EnsureDataDirectory();
        _position = 0;
        var content = await ReadNewContentAsync();
        _timer.Start();
        return Sanitize(content);
    }

    public void Stop()
    {
        _timer.Stop();
    }

    public async Task ClearAsync()
    {
        AppPaths.EnsureDataDirectory();
        await using var stream = new FileStream(
            AppPaths.SingBoxLogPath,
            FileMode.OpenOrCreate,
            FileAccess.Write,
            FileShare.ReadWrite);
        stream.SetLength(0);
        _position = 0;
    }

    public Task SaveAsAsync(string destinationPath, string diagnosticContent)
    {
        return File.WriteAllTextAsync(destinationPath, diagnosticContent);
    }

    private static string Sanitize(string content)
    {
        return string.Join('\n', content.Split('\n').Select(SanitizeLine));
    }

    private static string SanitizeLine(string line)
    {
        var sanitized = ConnectionDestination.Replace(line, "outbound connection established");
        return IPv4Address.Replace(sanitized, match => $"{match.Groups[1].Value}.{match.Groups[2].Value}.xxx.xxx");
    }
    public void Dispose()
    {
        _timer.Stop();
        _timer.Tick -= OnTimerTick;
    }

    private async void OnTimerTick(object? sender, EventArgs e)
    {
        if (_isReading)
        {
            return;
        }

        _isReading = true;
        try
        {
            var content = await ReadNewContentAsync();
            if (!string.IsNullOrEmpty(content))
            {
                LogReceived?.Invoke(this, Sanitize(content));
            }
        }
        catch (IOException)
        {
            // sing-box can briefly hold the file while it appends a record.
        }
        finally
        {
            _isReading = false;
        }
    }

    private async Task<string> ReadNewContentAsync()
    {
        await using var stream = new FileStream(
            AppPaths.SingBoxLogPath,
            FileMode.OpenOrCreate,
            FileAccess.Read,
            FileShare.ReadWrite);

        if (stream.Length < _position)
        {
            _position = 0;
        }

        stream.Seek(_position, SeekOrigin.Begin);
        using var reader = new StreamReader(stream);
        var content = await reader.ReadToEndAsync();
        _position = stream.Length;
        return Sanitize(content);
    }
}
