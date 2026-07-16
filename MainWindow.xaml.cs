using System.ComponentModel;
using System.Diagnostics;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
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
    private bool _isSubscriptionNotificationOpen;
    private string _subscriptionNotificationText = "";
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
            AnimateMenu(value);
        }
    }

    public bool IsSubscriptionNotificationOpen
    {
        get => _isSubscriptionNotificationOpen;
        private set
        {
            if (_isSubscriptionNotificationOpen == value)
            {
                return;
            }

            _isSubscriptionNotificationOpen = value;
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(IsSubscriptionNotificationOpen)));
            AnimateSubscriptionNotification(value);
        }
    }

    public string SubscriptionNotificationText
    {
        get => _subscriptionNotificationText;
        private set
        {
            if (_subscriptionNotificationText == value)
            {
                return;
            }

            _subscriptionNotificationText = value;
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(SubscriptionNotificationText)));
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
            AnimateSubscriptionDialog(value);
        }
    }

    public MainWindow()
    {
        ToggleMenuCommand = new RelayCommand(ToggleMenuAsync);
        InitializeComponent();
        DataContext = _viewModel;
        _viewModel.SettingsSaved += OnSettingsSaved;
        _viewModel.SubscriptionNotificationRequested += OnSubscriptionNotificationRequested;
        Loaded += OnLoaded;
        Closing += OnWindowClosing;
        Closed += OnWindowClosed;
        _trayIcon = CreateTrayIcon();
    }

    private async void OnLoaded(object sender, RoutedEventArgs e)
    {
        AnimateWindowEntrance();
        await _viewModel.InitializeAsync();
    }

    private void AnimateWindowEntrance()
    {
        BeginAnimation(OpacityProperty, new DoubleAnimation(0, 1, TimeSpan.FromMilliseconds(360))
        {
            EasingFunction = new CubicEase { EasingMode = EasingMode.EaseOut }
        });
    }

    private void AnimateMenu(bool show)
    {
        if (show)
        {
            MenuBackdrop.Visibility = Visibility.Visible;
            MenuPanel.Visibility = Visibility.Visible;
            MenuBackdrop.BeginAnimation(OpacityProperty, new DoubleAnimation(0, 1, TimeSpan.FromMilliseconds(180)));
            MenuPanel.Opacity = 0;
            ((TranslateTransform)MenuPanel.RenderTransform).X = -18;
            MenuPanel.BeginAnimation(OpacityProperty, new DoubleAnimation(0, 1, TimeSpan.FromMilliseconds(240))
            {
                EasingFunction = new CubicEase { EasingMode = EasingMode.EaseOut }
            });
            ((TranslateTransform)MenuPanel.RenderTransform).BeginAnimation(TranslateTransform.XProperty, new DoubleAnimation(-18, 0, TimeSpan.FromMilliseconds(240))
            {
                EasingFunction = new CubicEase { EasingMode = EasingMode.EaseOut }
            });
            return;
        }

        var menuFade = new DoubleAnimation(MenuPanel.Opacity, 0, TimeSpan.FromMilliseconds(160));
        menuFade.Completed += (_, _) =>
        {
            if (!IsMenuOpen)
            {
                MenuPanel.Visibility = Visibility.Collapsed;
                MenuBackdrop.Visibility = Visibility.Collapsed;
            }
        };
        MenuPanel.BeginAnimation(OpacityProperty, menuFade);
        MenuBackdrop.BeginAnimation(OpacityProperty, new DoubleAnimation(MenuBackdrop.Opacity, 0, TimeSpan.FromMilliseconds(160)));
    }

    private void AnimateSubscriptionDialog(bool show)
    {
        AnimateOverlay(
            SubscriptionDialogOverlay,
            SubscriptionDialogBackdrop,
            SubscriptionDialogSurface,
            show,
            () => IsSubscriptionDialogOpen,
            0.96,
            12);
    }

    private void AnimateSubscriptionNotification(bool show)
    {
        AnimateOverlay(
            SubscriptionNotificationOverlay,
            SubscriptionNotificationBackdrop,
            SubscriptionNotificationSurface,
            show,
            () => IsSubscriptionNotificationOpen,
            0.94,
            10);
    }

    private static void AnimateOverlay(
        Grid overlay,
        Border backdrop,
        Border surface,
        bool show,
        Func<bool> isStillOpen,
        double scaleFrom,
        double offsetY)
    {
        var transforms = (TransformGroup)surface.RenderTransform;
        var scale = (ScaleTransform)transforms.Children[0];
        var translate = (TranslateTransform)transforms.Children[1];

        if (show)
        {
            overlay.Visibility = Visibility.Visible;
            backdrop.Opacity = 0;
            surface.Opacity = 0;
            scale.ScaleX = scaleFrom;
            scale.ScaleY = scaleFrom;
            translate.Y = offsetY;
            backdrop.BeginAnimation(OpacityProperty, new DoubleAnimation(0, 1, TimeSpan.FromMilliseconds(180)));
            surface.BeginAnimation(OpacityProperty, new DoubleAnimation(0, 1, TimeSpan.FromMilliseconds(230))
            {
                EasingFunction = new CubicEase { EasingMode = EasingMode.EaseOut }
            });
            scale.BeginAnimation(ScaleTransform.ScaleXProperty, new DoubleAnimation(scaleFrom, 1, TimeSpan.FromMilliseconds(230))
            {
                EasingFunction = new CubicEase { EasingMode = EasingMode.EaseOut }
            });
            scale.BeginAnimation(ScaleTransform.ScaleYProperty, new DoubleAnimation(scaleFrom, 1, TimeSpan.FromMilliseconds(230))
            {
                EasingFunction = new CubicEase { EasingMode = EasingMode.EaseOut }
            });
            translate.BeginAnimation(TranslateTransform.YProperty, new DoubleAnimation(offsetY, 0, TimeSpan.FromMilliseconds(230))
            {
                EasingFunction = new CubicEase { EasingMode = EasingMode.EaseOut }
            });
            return;
        }

        var surfaceFade = new DoubleAnimation(surface.Opacity, 0, TimeSpan.FromMilliseconds(150));
        surfaceFade.Completed += (_, _) =>
        {
            if (!isStillOpen())
            {
                overlay.Visibility = Visibility.Collapsed;
            }
        };
        surface.BeginAnimation(OpacityProperty, surfaceFade);
        backdrop.BeginAnimation(OpacityProperty, new DoubleAnimation(backdrop.Opacity, 0, TimeSpan.FromMilliseconds(150)));
        scale.BeginAnimation(ScaleTransform.ScaleXProperty, new DoubleAnimation(scale.ScaleX, scaleFrom, TimeSpan.FromMilliseconds(150)));
        scale.BeginAnimation(ScaleTransform.ScaleYProperty, new DoubleAnimation(scale.ScaleY, scaleFrom, TimeSpan.FromMilliseconds(150)));
        translate.BeginAnimation(TranslateTransform.YProperty, new DoubleAnimation(translate.Y, offsetY, TimeSpan.FromMilliseconds(150)));
    }

    private void AnimateCurrentView()
    {
        Dispatcher.BeginInvoke(() =>
        {
            if (MainViews.SelectedContent is not UIElement content)
            {
                return;
            }

            content.Opacity = 0;
            content.RenderTransform = new TranslateTransform(0, 10);
            content.BeginAnimation(OpacityProperty, new DoubleAnimation(0, 1, TimeSpan.FromMilliseconds(220))
            {
                EasingFunction = new CubicEase { EasingMode = EasingMode.EaseOut }
            });
            ((TranslateTransform)content.RenderTransform).BeginAnimation(TranslateTransform.YProperty, new DoubleAnimation(10, 0, TimeSpan.FromMilliseconds(220))
            {
                EasingFunction = new CubicEase { EasingMode = EasingMode.EaseOut }
            });
        });
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
            AnimateCurrentView();
        }
    }

    private void OnMenuNavigationMouseUp(object sender, MouseButtonEventArgs e)
    {
        IsMenuOpen = false;
    }

    private async void OnSubscriptionCardClick(object sender, RoutedEventArgs e)
    {
        IsSubscriptionDialogOpen = true;
        await _viewModel.RefreshSubscriptionInBackgroundAsync();
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
        var trayIcon = new Forms.NotifyIcon
        {
            Icon = string.IsNullOrWhiteSpace(iconPath)
                ? System.Drawing.SystemIcons.Application
                : System.Drawing.Icon.ExtractAssociatedIcon(iconPath) ?? System.Drawing.SystemIcons.Application,
            Text = "CosmoNet",
            ContextMenuStrip = menu,
            Visible = true
        };
        trayIcon.MouseClick += (_, args) =>
        {
            if (args.Button == Forms.MouseButtons.Left)
            {
                Dispatcher.BeginInvoke(ShowFromTray);
            }
        };
        return trayIcon;
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

    private void OnSubscriptionNotificationRequested(object? sender, string message)
    {
        if (!IsVisible || WindowState == WindowState.Minimized)
        {
            ShowFromTray();
        }

        SubscriptionNotificationText = message;
        IsSubscriptionNotificationOpen = true;
    }

    private void OnCloseSubscriptionNotificationClick(object sender, RoutedEventArgs e)
    {
        IsSubscriptionNotificationOpen = false;
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
