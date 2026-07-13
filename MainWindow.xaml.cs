using System.ComponentModel;
using System.Diagnostics;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media.Animation;
using CosmoNet.App.Models;
using CosmoNet.App.ViewModels;
using Forms = System.Windows.Forms;

namespace CosmoNet.App;

public partial class MainWindow : Window, INotifyPropertyChanged
{
    private readonly MainViewModel _viewModel = new();
    private bool _isMenuOpen;
    private bool _isSubscriptionDialogOpen;
    private bool _exitRequested;
    private CancellationTokenSource? _toastCancellation;
    private readonly Forms.NotifyIcon _trayIcon;

    public event PropertyChangedEventHandler? PropertyChanged;

    public ICommand ToggleMenuCommand { get; }

    public bool IsMenuOpen
    {
        get => _isMenuOpen;
        private set
        {
            if (_isMenuOpen == value)
            {
                return;
            }

            _isMenuOpen = value;
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(IsMenuOpen)));
        }
    }

    public bool IsSubscriptionDialogOpen
    {
        get => _isSubscriptionDialogOpen;
        private set
        {
            if (_isSubscriptionDialogOpen == value)
            {
                return;
            }

            _isSubscriptionDialogOpen = value;
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(IsSubscriptionDialogOpen)));
        }
    }

    public MainWindow()
    {
        ToggleMenuCommand = new RelayCommand(ToggleMenuAsync);
        InitializeComponent();
        DataContext = _viewModel;
        _viewModel.SettingsSaved += OnSettingsSaved;
        Loaded += OnLoaded;
        Closing += OnWindowClosing;
        Closed += OnWindowClosed;
        _trayIcon = CreateTrayIcon();
    }

    private async void OnLoaded(object sender, RoutedEventArgs e)
    {
        await _viewModel.InitializeAsync();
    }

    private Task ToggleMenuAsync()
    {
        IsMenuOpen = !IsMenuOpen;
        return Task.CompletedTask;
    }

    private void OnMenuBackdropMouseDown(object sender, MouseButtonEventArgs e)
    {
        IsMenuOpen = false;
    }

    private void OnMenuSelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (sender is System.Windows.Controls.ListBox menu && menu.SelectedIndex >= 0)
        {
            MainViews.SelectedIndex = menu.SelectedIndex;
            IsMenuOpen = false;
        }
    }

    private void OnMenuNavigationMouseUp(object sender, MouseButtonEventArgs e)
    {
        IsMenuOpen = false;
    }

    private void OnSubscriptionCardClick(object sender, RoutedEventArgs e)
    {
        IsSubscriptionDialogOpen = true;
    }

    private void OnCloseSubscriptionDialogClick(object sender, RoutedEventArgs e)
    {
        IsSubscriptionDialogOpen = false;
    }

    private void OnSubscriptionDialogBackdropMouseDown(object sender, MouseButtonEventArgs e)
    {
        IsSubscriptionDialogOpen = false;
    }

    private void OnSubscriptionDialogMouseDown(object sender, MouseButtonEventArgs e)
    {
        e.Handled = true;
    }

    private void OnWindowPreviewKeyDown(object sender, System.Windows.Input.KeyEventArgs e)
    {
        if (e.Key == Key.Escape && IsSubscriptionDialogOpen)
        {
            IsSubscriptionDialogOpen = false;
            e.Handled = true;
        }
    }
    private async void OnRemoveApplicationClick(object sender, RoutedEventArgs e)
    {
        if (sender is FrameworkElement { DataContext: InstalledApplication application })
        {
            await _viewModel.RemoveApplicationAsync(application);
        }
    }

    private Forms.NotifyIcon CreateTrayIcon()
    {
        var menu = new Forms.ContextMenuStrip();
        menu.Items.Add("Открыть", null, (_, _) => Dispatcher.Invoke(ShowFromTray));
        menu.Items.Add("Выход", null, (_, _) => Dispatcher.Invoke(ExitApplication));

        var iconPath = Process.GetCurrentProcess().MainModule?.FileName;
        return new Forms.NotifyIcon
        {
            Icon = string.IsNullOrWhiteSpace(iconPath)
                ? System.Drawing.SystemIcons.Application
                : System.Drawing.Icon.ExtractAssociatedIcon(iconPath) ?? System.Drawing.SystemIcons.Application,
            Text = "CosmoNet",
            ContextMenuStrip = menu,
            Visible = true
        };
    }

    private void OnWindowClosing(object? sender, CancelEventArgs e)
    {
        if (_exitRequested)
        {
            return;
        }

        e.Cancel = true;
        HideToTray();
    }

    private void OnWindowClosed(object? sender, EventArgs e)
    {
        _toastCancellation?.Cancel();
        _trayIcon.Dispose();
        _viewModel.DisconnectCommand.Execute(null);
        _viewModel.Dispose();
    }

    private void OnVpnLogTextChanged(object sender, TextChangedEventArgs e)
    {
        VpnLogBox.ScrollToEnd();
    }

    private void OnTitleBarMouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        if (e.ButtonState == MouseButtonState.Pressed)
        {
            DragMove();
        }
    }

    private void OnMinimizeClick(object sender, RoutedEventArgs e)
    {
        WindowState = WindowState.Minimized;
    }

    private void OnHideToTrayClick(object sender, RoutedEventArgs e)
    {
        HideToTray();
    }

    private void HideToTray()
    {
        Hide();
        _trayIcon.ShowBalloonTip(1200, "CosmoNet", "Приложение продолжает работать в трее.", Forms.ToolTipIcon.Info);
    }

    private void ShowFromTray()
    {
        Show();
        WindowState = WindowState.Normal;
        Activate();
    }

    private void ExitApplication()
    {
        _exitRequested = true;
        Close();
    }

    private async void OnSettingsSaved(object? sender, EventArgs e)
    {
        _toastCancellation?.Cancel();
        _toastCancellation = new CancellationTokenSource();
        var cancellationToken = _toastCancellation.Token;

        SaveToast.BeginAnimation(OpacityProperty, null);
        SaveToast.Opacity = 1;
        SaveToast.Visibility = Visibility.Visible;

        try
        {
            await Task.Delay(TimeSpan.FromSeconds(2), cancellationToken);
        }
        catch (OperationCanceledException)
        {
            return;
        }

        var fadeOut = new DoubleAnimation(1, 0, TimeSpan.FromMilliseconds(350));
        fadeOut.Completed += (_, _) =>
        {
            if (!cancellationToken.IsCancellationRequested)
            {
                SaveToast.Visibility = Visibility.Collapsed;
            }
        };
        SaveToast.BeginAnimation(OpacityProperty, fadeOut);
    }
}
