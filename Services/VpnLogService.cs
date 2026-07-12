using System.Windows.Threading;

namespace CosmoNet.App.Services;

public sealed class VpnLogService : IDisposable
{
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
        return content;
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

    public async Task SaveAsAsync(string destinationPath)
    {
        AppPaths.EnsureDataDirectory();
        await using var source = new FileStream(
            AppPaths.SingBoxLogPath,
            FileMode.OpenOrCreate,
            FileAccess.Read,
            FileShare.ReadWrite);
        await using var destination = new FileStream(destinationPath, FileMode.Create, FileAccess.Write, FileShare.None);
        await source.CopyToAsync(destination);
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
                LogReceived?.Invoke(this, content);
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
        return content;
    }
}
