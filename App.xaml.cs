using System.Threading;
using System.Windows;

namespace CosmoNet.App;

public partial class App : System.Windows.Application
{
    private Mutex? _singleInstanceMutex;
    private bool _ownsSingleInstanceMutex;

    protected override void OnStartup(StartupEventArgs e)
    {
        var isFirstInstance = false;
        _singleInstanceMutex = new Mutex(true, @"Local\CosmoNet.App.SingleInstance", out isFirstInstance);
        if (!isFirstInstance)
        {
            System.Windows.MessageBox.Show("CosmoNet уже запущен.", "CosmoNet", MessageBoxButton.OK, MessageBoxImage.Information);
            Shutdown();
            return;
        }

        _ownsSingleInstanceMutex = true;
        base.OnStartup(e);
        var mainWindow = new MainWindow();
        MainWindow = mainWindow;
        mainWindow.Show();
    }

    protected override void OnExit(ExitEventArgs e)
    {
        if (_ownsSingleInstanceMutex)
        {
            _singleInstanceMutex?.ReleaseMutex();
        }
        _singleInstanceMutex?.Dispose();
        base.OnExit(e);
    }
}