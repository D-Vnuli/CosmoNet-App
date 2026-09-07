using System.ComponentModel;
using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Threading;
using CosmoNet.App.Models;
using CosmoNet.App.ViewModels;
using Forms = System.Windows.Forms;

namespace CosmoNet.App;

public partial class MainWindow : Window, INotifyPropertyChanged
{
    private const int RoundedWindowCornerRadius = 18;
    private readonly MainViewModel _viewModel = new();
    private bool _isMenuOpen;
    private bool _isSubscriptionDialogOpen;
    private bool _exitRequested;
    private CancellationTokenSource? _toastCancellation;
    private bool _isSubscriptionNotificationOpen;
    private int _selectedTariffPrice = 50;
    private string _selectedTariffName = "Promo";
    private string _selectedTariffDevices = "1 \u0443\u0441\u0442\u0440\u043e\u0439\u0441\u0442\u0432\u043e";
    private string _subscriptionNotificationText = "";
    private readonly Forms.NotifyIcon _trayIcon;
    private readonly DispatcherTimer _starfieldTimer = new(DispatcherPriority.Background);
    private readonly Random _starfieldRandom = new();
    private bool _spawnSecondStar;

    public event PropertyChangedEventHandler? PropertyChanged;

    public ICommand ToggleMenuCommand { get; }

    public string AppVersion => GetType().Assembly.GetName().Version?.ToString(3) ?? "0.2.8";

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
        StateChanged += OnWindowStateChanged;
        _starfieldTimer.Tick += OnStarfieldTimerTick;
        _trayIcon = CreateTrayIcon();
    }

    private async void OnLoaded(object sender, RoutedEventArgs e)
    {
        ApplyRoundedWindowRegion();
        AnimateWindowEntrance();
        UpdateStarfieldState();
        await _viewModel.InitializeAsync();
    }

    private void OnMainViewsSelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (ReferenceEquals(e.Source, MainViews))
        {
            UpdateStarfieldState();
        }
    }

    private void UpdateStarfieldState()
    {
        if (IsLoaded && IsVisible && WindowState != WindowState.Minimized && HomeTab.IsSelected)
        {
            if (!_starfieldTimer.IsEnabled)
            {
                ScheduleNextStarBurst();
            }

            return;
        }

        StopStarfield();
    }

    private void OnStarfieldTimerTick(object? sender, EventArgs e)
    {
        _starfieldTimer.Stop();
        SpawnShootingStar();

        if (_spawnSecondStar)
        {
            _spawnSecondStar = false;
            ScheduleNextStarBurst();
            return;
        }

        if (_starfieldRandom.NextDouble() < 0.35)
        {
            _spawnSecondStar = true;
            _starfieldTimer.Interval = TimeSpan.FromMilliseconds(_starfieldRandom.Next(650, 1251));
            _starfieldTimer.Start();
            return;
        }

        ScheduleNextStarBurst();
    }

    private void ScheduleNextStarBurst()
    {
        if (!IsVisible || WindowState == WindowState.Minimized || !HomeTab.IsSelected)
        {
            return;
        }

        _spawnSecondStar = false;
        _starfieldTimer.Interval = TimeSpan.FromMilliseconds(_starfieldRandom.Next(5000, 10001));
        _starfieldTimer.Start();
    }

    private void SpawnShootingStar()
    {
        var width = StarfieldCanvas.ActualWidth;
        var height = StarfieldCanvas.ActualHeight;
        if (width < 100 || height < 100)
        {
            ScheduleNextStarBurst();
            return;
        }

        var tailLength = _starfieldRandom.Next(34, 57);
        var travelX = _starfieldRandom.Next(110, 181);
        var travelY = _starfieldRandom.Next(48, 96);
        var duration = TimeSpan.FromMilliseconds(_starfieldRandom.Next(900, 1451));
        var startingX = _starfieldRandom.NextDouble() * Math.Max(1, width - tailLength - travelX / 2);
        var startingY = 20 + _starfieldRandom.NextDouble() * Math.Max(1, height * 0.72 - 40);

        var meteor = new Grid
        {
            Width = tailLength,
            Height = 8,
            Opacity = 0,
            RenderTransformOrigin = new System.Windows.Point(0.5, 0.5),
            IsHitTestVisible = false
        };
        var tail = new System.Windows.Shapes.Rectangle
        {
            Width = tailLength,
            Height = 1.4,
            HorizontalAlignment = System.Windows.HorizontalAlignment.Stretch,
            VerticalAlignment = System.Windows.VerticalAlignment.Center,
            Fill = new LinearGradientBrush(
                System.Windows.Media.Color.FromArgb(0, 154, 207, 255),
                System.Windows.Media.Color.FromArgb(235, 242, 251, 255),
                new System.Windows.Point(0, 0.5),
                new System.Windows.Point(1, 0.5))
        };
        var head = new System.Windows.Shapes.Ellipse
        {
            Width = 3.4,
            Height = 3.4,
            Fill = new SolidColorBrush(System.Windows.Media.Color.FromRgb(245, 252, 255)),
            HorizontalAlignment = System.Windows.HorizontalAlignment.Right,
            VerticalAlignment = System.Windows.VerticalAlignment.Center
        };
        meteor.Children.Add(tail);
        meteor.Children.Add(head);

        Canvas.SetLeft(meteor, startingX);
        Canvas.SetTop(meteor, startingY);
        StarfieldCanvas.Children.Add(meteor);

        var translate = new TranslateTransform();
        var transforms = new TransformGroup();
        transforms.Children.Add(new RotateTransform(Math.Atan2(travelY, travelX) * 180 / Math.PI));
        transforms.Children.Add(translate);
        meteor.RenderTransform = transforms;

        var opacity = new DoubleAnimationUsingKeyFrames();
        opacity.KeyFrames.Add(new DiscreteDoubleKeyFrame(0, KeyTime.FromTimeSpan(TimeSpan.Zero)));
        opacity.KeyFrames.Add(new LinearDoubleKeyFrame(0.88, KeyTime.FromTimeSpan(TimeSpan.FromMilliseconds(110))));
        opacity.KeyFrames.Add(new LinearDoubleKeyFrame(0.68, KeyTime.FromTimeSpan(TimeSpan.FromMilliseconds(duration.TotalMilliseconds * 0.68))));
        opacity.KeyFrames.Add(new LinearDoubleKeyFrame(0, KeyTime.FromTimeSpan(duration)));
        opacity.Completed += (_, _) => StarfieldCanvas.Children.Remove(meteor);

        meteor.BeginAnimation(OpacityProperty, opacity);
        translate.BeginAnimation(TranslateTransform.XProperty, new DoubleAnimation(0, travelX, duration));
        translate.BeginAnimation(TranslateTransform.YProperty, new DoubleAnimation(0, travelY, duration));
    }

    private void StopStarfield()
    {
        _starfieldTimer.Stop();
        _spawnSecondStar = false;
        StarfieldCanvas?.Children.Clear();
    }

    private void ApplyRoundedWindowRegion()
    {
        var handle = new System.Windows.Interop.WindowInteropHelper(this).Handle;
        if (handle == IntPtr.Zero)
        {
            return;
        }

        var dpi = VisualTreeHelper.GetDpi(this);
        var width = (int)Math.Ceiling(ActualWidth * dpi.DpiScaleX);
        var height = (int)Math.Ceiling(ActualHeight * dpi.DpiScaleY);
        var diameter = (int)Math.Ceiling(RoundedWindowCornerRadius * 2 * dpi.DpiScaleX);
        var region = CreateRoundRectRgn(0, 0, width + 1, height + 1, diameter, diameter);

        if (region != IntPtr.Zero && SetWindowRgn(handle, region, true) == 0)
        {
            DeleteObject(region);
        }
    }

    [DllImport("gdi32.dll")]
    private static extern IntPtr CreateRoundRectRgn(int left, int top, int right, int bottom, int width, int height);

    [DllImport("user32.dll")]
    private static extern int SetWindowRgn(IntPtr handle, IntPtr region, bool redraw);

    [DllImport("gdi32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool DeleteObject(IntPtr objectHandle);

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

    private void OnTariffSelectionClick(object sender, RoutedEventArgs e)
    {
        if (sender is not System.Windows.Controls.RadioButton { IsChecked: true } tariff)
        {
            return;
        }

        (_selectedTariffName, _selectedTariffPrice, _selectedTariffDevices) = (tariff.Tag as string) switch
        {
            "Lite" => ("Lite", 129, "1 \u0443\u0441\u0442\u0440\u043e\u0439\u0441\u0442\u0432\u043e"),
            "Standard" => ("Standard", 199, "3 \u0443\u0441\u0442\u0440\u043e\u0439\u0441\u0442\u0432\u0430"),
            "Family" => ("Family", 279, "5 \u0443\u0441\u0442\u0440\u043e\u0439\u0441\u0442\u0432"),
            _ => ("Promo", 50, "1 \u0443\u0441\u0442\u0440\u043e\u0439\u0441\u0442\u0432\u043e")
        };
        UpdatePurchaseSummary();
    }

    private void UpdatePurchaseSummary()
    {
        if (TotalPriceText is null || TotalDetailsText is null)
        {
            return;
        }

        TotalPriceText.Text = $"{_selectedTariffPrice} \u20BD";
        TotalDetailsText.Text = $"{_selectedTariffName} \u00B7 {_selectedTariffDevices}";
    }


    private async void OnPurchaseClick(object sender, RoutedEventArgs e)
    {
        PurchaseButton.IsChecked = false;
        await _viewModel.StartYooKassaPaymentAsync(_selectedTariffName);
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
                Dispatcher.BeginInvoke(ToggleTrayVisibility);
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
        StopStarfield();
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

    private void OnWindowStateChanged(object? sender, EventArgs e)
    {
        if (WindowState == WindowState.Minimized)
        {
            HideToTray(showNotification: false);
        }
    }

    private void OnHideToTrayClick(object sender, RoutedEventArgs e)
    {
        HideToTray();
    }

    private void HideToTray(bool showNotification = true)
    {
        StopStarfield();
        ShowInTaskbar = false;
        Hide();
        if (showNotification)
        {
            _trayIcon.ShowBalloonTip(1200, "CosmoNet", "Приложение продолжает работать в трее.", Forms.ToolTipIcon.Info);
        }
    }

    private void ShowFromTray()
    {
        ShowInTaskbar = true;
        Show();
        WindowState = WindowState.Normal;
        Activate();
        UpdateStarfieldState();
    }

    private void ToggleTrayVisibility()
    {
        if (!IsVisible || WindowState == WindowState.Minimized)
        {
            ShowFromTray();
            return;
        }

        HideToTray();
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
