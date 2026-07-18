using System.Collections.ObjectModel;
using System.Collections.Specialized;
using System.ComponentModel;
using System.Diagnostics;
using System.Globalization;
using System.Net.Sockets;
using System.Runtime.CompilerServices;
using System.Security.Cryptography;
using System.Windows.Input;
using System.Windows.Threading;
using Microsoft.Win32;
using CosmoNet.App.Models;
using CosmoNet.App.Services;

namespace CosmoNet.App.ViewModels;

public sealed class MainViewModel : INotifyPropertyChanged, IDisposable
{
    private readonly SettingsStore _settingsStore = new();
    private readonly SubscriptionService _subscriptionService = new();
    private readonly TelegramAuthApiClient _telegramAuthApiClient = new();
    private readonly FeedbackApiClient _feedbackApiClient = new();
    private readonly SubscriptionMetadataApiClient _subscriptionMetadataApiClient = new();
    private readonly SecretSettingsStore _secretSettingsStore = new();
    private readonly SingBoxConfigBuilder _configBuilder = new();
    private readonly SingBoxService _singBoxService = new();
    private readonly ApplicationIconService _applicationIconService = new();
    private readonly DispatcherTimer _serverAvailabilityTimer;
    private readonly DispatcherTimer _subscriptionRefreshTimer;
    private readonly VpnLogService _vpnLogService = new();

    private const string DefaultProbeHost = "45.151.69.119";
    private const int DefaultProbePort = 443;

    private string _subscriptionUrl = "";
    private string _authApiBaseUrl = AppSettings.DefaultAuthApiBaseUrl;
    private string _authDeviceId = "";
    private string _statusText = "Готов к настройке";
    private string _authStatus = "Войдите через Telegram, чтобы приложение могло получить вашу подписку.";
    private string _authSessionId = "";
    private string _diagnosticText = "Диагностика еще не запускалась.";
    private string _lastConfigPath = AppPaths.GeneratedConfigPath;
    private AccountSession _accountSession = new();
    private bool _isAuthMenuOpen;
    private bool _isBusy;
    private bool _isConnected;
    private bool _isServerAvailable;
    private bool _isCheckingServerAvailability;
    private bool _isRefreshingSubscription;
    private DateTimeOffset? _expiryWarningShownFor;
    private TrafficMode _trafficMode = TrafficMode.AllTraffic;
    private DateTimeOffset? _lastRefresh;
    private string _vpnLogText = "";
    private bool _isLogMonitoring;
    private string _feedbackName = "";
    private string _feedbackContacts = "";
    private string _feedbackMessage = "";
    private string _feedbackStatus = "";
    private bool _isSubmittingFeedback;

    private const int MaximumLogTextLength = 250_000;

    public MainViewModel()
    {
        AvailableApplications.CollectionChanged += OnAvailableApplicationsChanged;
        _vpnLogService.LogReceived += OnLogReceived;

        _serverAvailabilityTimer = new DispatcherTimer
        {
            Interval = TimeSpan.FromSeconds(15)
        };
        _serverAvailabilityTimer.Tick += async (_, _) => { await RefreshServerAvailabilityAsync(); RefreshSubscriptionStatus(); };
        _subscriptionRefreshTimer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(30) };
        _subscriptionRefreshTimer.Tick += async (_, _) => await RefreshSubscriptionInBackgroundAsync();

        RefreshCommand = new RelayCommand(RefreshAsync, () => !IsBusy && !IsConnected);
        PowerCommand = new RelayCommand(ToggleConnectionAsync);
        DisconnectCommand = new RelayCommand(DisconnectAsync, () => !IsBusy && IsConnected);
        SaveCommand = new RelayCommand(SaveAsync, () => !IsBusy);
        OpenDataFolderCommand = new RelayCommand(OpenDataFolderAsync);
        BrowseApplicationCommand = new RelayCommand(BrowseApplicationAsync, () => !IsBusy);
        ShowAuthMethodsCommand = new RelayCommand(ShowAuthMethods, () => !IsBusy && !AccountSession.IsAuthorized);
        LoginCommand = new RelayCommand(LoginAsync, () => !IsBusy && !AccountSession.IsAuthorized);
        LogoutCommand = new RelayCommand(LogoutAsync, () => !IsBusy && AccountSession.IsAuthorized);
        RunDiagnosticsCommand = new RelayCommand(RunDiagnosticsAsync, () => !IsBusy);
        EnableLogsCommand = new RelayCommand(EnableLogsAsync, () => !IsLogMonitoring);
        DisableLogsCommand = new RelayCommand(DisableLogsAsync, () => IsLogMonitoring);
        ClearLogsCommand = new RelayCommand(ClearLogsAsync);
        SaveLogsCommand = new RelayCommand(SaveLogsAsync);
        SubmitFeedbackCommand = new RelayCommand(SubmitFeedbackAsync, CanSubmitFeedback);
    }

    public event PropertyChangedEventHandler? PropertyChanged;
    public event EventHandler? SettingsSaved;
    public event EventHandler<string>? SubscriptionNotificationRequested;

    public ObservableCollection<VpnProfile> Profiles { get; } = new();
    public ObservableCollection<InstalledApplication> AvailableApplications { get; } = new();

    public ICommand RefreshCommand { get; }
    public ICommand PowerCommand { get; }
    public ICommand DisconnectCommand { get; }
    public ICommand SaveCommand { get; }
    public ICommand OpenDataFolderCommand { get; }
    public ICommand BrowseApplicationCommand { get; }
    public ICommand ShowAuthMethodsCommand { get; }
    public ICommand LoginCommand { get; }
    public ICommand LogoutCommand { get; }
    public ICommand RunDiagnosticsCommand { get; }
    public ICommand EnableLogsCommand { get; }
    public ICommand DisableLogsCommand { get; }
    public ICommand ClearLogsCommand { get; }
    public ICommand SaveLogsCommand { get; }
    public ICommand SubmitFeedbackCommand { get; }

    public string SubscriptionUrl
    {
        get => _subscriptionUrl;
        set => SetField(ref _subscriptionUrl, value);
    }

    public string AuthApiBaseUrl
    {
        get => _authApiBaseUrl;
        set => SetField(ref _authApiBaseUrl, value);
    }

    public string StatusText
    {
        get => _statusText;
        set => SetField(ref _statusText, value);
    }

    public string AuthStatus
    {
        get => _authStatus;
        set => SetField(ref _authStatus, value);
    }


    public string DiagnosticText
    {
        get => _diagnosticText;
        set => SetField(ref _diagnosticText, value);
    }

    public string VpnLogText
    {
        get => _vpnLogText;
        private set => SetField(ref _vpnLogText, value);
    }


    public string FeedbackName
    {
        get => _feedbackName;
        set
        {
            if (SetField(ref _feedbackName, value))
            {
                RaiseFeedbackCommandState();
            }
        }
    }

    public string FeedbackContacts
    {
        get => _feedbackContacts;
        set
        {
            if (SetField(ref _feedbackContacts, value))
            {
                RaiseFeedbackCommandState();
            }
        }
    }

    public string FeedbackMessage
    {
        get => _feedbackMessage;
        set
        {
            if (SetField(ref _feedbackMessage, value))
            {
                RaiseFeedbackCommandState();
            }
        }
    }

    public string FeedbackStatus
    {
        get => _feedbackStatus;
        private set => SetField(ref _feedbackStatus, value);
    }

    public bool IsSubmittingFeedback
    {
        get => _isSubmittingFeedback;
        private set
        {
            if (SetField(ref _isSubmittingFeedback, value))
            {
                RaiseFeedbackCommandState();
            }
        }
    }
    public bool IsLogMonitoring
    {
        get => _isLogMonitoring;
        private set
        {
            if (SetField(ref _isLogMonitoring, value))
            {
                RaiseLogCommandStates();
            }
        }
    }

    public string LastConfigPath
    {
        get => _lastConfigPath;
        set => SetField(ref _lastConfigPath, value);
    }

    public string SecretStorageText => $"Секреты: {AppPaths.SecretSettingsPath}";

    public AccountSession AccountSession
    {
        get => _accountSession;
        set
        {
            if (SetField(ref _accountSession, value))
            {
                RefreshSubscriptionStatus();
                OnPropertyChanged(nameof(AccountButtonText));
                OnPropertyChanged(nameof(IsAuthorized));
                RaiseCommandStates();
            }
        }
    }

    public SubscriptionSummary Subscription => AccountSession.Subscription ?? SubscriptionSummary.Empty;
    public string AccountButtonText => AccountSession.IsAuthorized
        ? (string.IsNullOrWhiteSpace(AccountSession.DisplayName) ? "CosmoNet" : AccountSession.DisplayName)
        : "Вход";
    public bool IsAuthorized => AccountSession.IsAuthorized;
    public bool IsAuthMenuOpen
    {
        get => _isAuthMenuOpen;
        set => SetField(ref _isAuthMenuOpen, value);
    }



    public string AccountDisplayText => AccountSession.IsAuthorized
        ? AccountSession.DisplayName
        : "Telegram не подключен";

    public bool IsBusy
    {
        get => _isBusy;
        set
        {
            if (SetField(ref _isBusy, value))
            {
                RaiseCommandStates();
                OnPropertyChanged(nameof(PowerButtonColor));
                OnPropertyChanged(nameof(ConnectionStateText));
            }
        }
    }

    public bool IsConnected
    {
        get => _isConnected;
        set
        {
            if (SetField(ref _isConnected, value))
            {
                RaiseCommandStates();
                OnPropertyChanged(nameof(PowerButtonColor));
                OnPropertyChanged(nameof(ConnectionStateText));
                OnPropertyChanged(nameof(ConnectionStatusTitle));
                OnPropertyChanged(nameof(ServerStatusText));
                OnPropertyChanged(nameof(ServerStatusColor));
                OnPropertyChanged(nameof(CurrentCountryName));
                OnPropertyChanged(nameof(CurrentCountryFlag));
                OnPropertyChanged(nameof(CurrentCountryCode));
            }
        }
    }

    public TrafficMode TrafficMode
    {
        get => _trafficMode;
        set
        {
            if (SetField(ref _trafficMode, value))
            {
                OnPropertyChanged(nameof(IsAllTrafficMode));
                OnPropertyChanged(nameof(IsSelectedAppsMode));
                OnPropertyChanged(nameof(TrafficModeText));
            }
        }
    }

    public bool IsAllTrafficMode
    {
        get => TrafficMode == TrafficMode.AllTraffic;
        set
        {
            if (value)
            {
                TrafficMode = TrafficMode.AllTraffic;
            }
        }
    }

    public bool IsSelectedAppsMode
    {
        get => TrafficMode == TrafficMode.SelectedApps;
        set
        {
            if (value)
            {
                TrafficMode = TrafficMode.SelectedApps;
            }
        }
    }

    public DateTimeOffset? LastRefresh
    {
        get => _lastRefresh;
        set
        {
            if (SetField(ref _lastRefresh, value))
            {
                OnPropertyChanged(nameof(LastRefreshText));
            }
        }
    }

    public string CoreStatus => _singBoxService.IsCoreAvailable
        ? "Ядро sing-box найдено"
        : "Нужно добавить Resources\\sing-box\\sing-box.exe";

    public string AdminStatus => _singBoxService.IsAdministrator
        ? "Запущено с правами администратора"
        : "Для подключения нужен запуск от администратора";

    public string SubscriptionExpiresText => Subscription.ExpiresAt is null
        ? (EffectiveSubscriptionStatus == SubscriptionStatus.Active ? "\u0411\u0435\u0437\u0433\u0440\u0430\u043d\u0438\u0447\u043d\u043e" : "\u041e\u0436\u0438\u0434\u0430\u0435\u0442 Telegram")
        : Subscription.ExpiresAt.Value.ToLocalTime().ToString("dd.MM.yyyy");

    public string SubscriptionStatusText => EffectiveSubscriptionStatus switch
    {
        SubscriptionStatus.Active => "Активна",
        SubscriptionStatus.ExpiringSoon => "Скоро закончится",
        SubscriptionStatus.Expired => "Истекла",
        SubscriptionStatus.Disabled => "Отключена",
        _ => "Ожидает данные"
    };

    public string SubscriptionTariffText => string.IsNullOrWhiteSpace(Subscription.TariffName)
        ? "Тариф неизвестен"
        : Subscription.TariffName;

    public string SubscriptionDevicesText => Subscription.DeviceLimit switch
    {
        > 0 => $"Устройств: до {Subscription.DeviceLimit}",
        0 => "Устройств: безгранично",
        _ => "Лимит устройств не указан"
    };

    public string SubscriptionTrafficText => Subscription.TrafficLimitBytes > 0
        ? $"Трафик: {FormatBytes(Subscription.TrafficUsedBytes)} / {FormatBytes(Subscription.TrafficLimitBytes)}"
        : $"Использовано: {FormatBytes(Subscription.TrafficUsedBytes)}";

    public string SubscriptionLastSyncText => Subscription.LastSyncedAt is null
        ? "Синхронизация еще не выполнялась"
        : $"Синхронизация: {Subscription.LastSyncedAt.Value.ToLocalTime():dd.MM.yyyy HH:mm}";

    public string SubscriptionModalStatusText => EffectiveSubscriptionStatus switch
    {
        SubscriptionStatus.Active => "Активна",
        SubscriptionStatus.ExpiringSoon => "Скоро закончится",
        _ => "Не активна"
    };

    public string SubscriptionModalStatusColor => EffectiveSubscriptionStatus switch
    {
        SubscriptionStatus.Active => "#35D587",
        SubscriptionStatus.ExpiringSoon => "#F0A33C",
        _ => "#E6585C"
    };

    public string SubscriptionModalExpiresText => Subscription.ExpiresAt is null
        ? (EffectiveSubscriptionStatus == SubscriptionStatus.Active ? "\u0411\u0435\u0437\u0433\u0440\u0430\u043d\u0438\u0447\u043d\u043e" : "\u041d\u0435 \u0443\u043a\u0430\u0437\u0430\u043d\u043e")
        : Subscription.ExpiresAt.Value.ToLocalTime().ToString("dd MMMM yyyy", CultureInfo.GetCultureInfo("ru-RU"));

    public string SubscriptionModalCountryText => $"{CurrentCountryFlag} {CurrentCountryName}";

    public string SubscriptionModalDeviceLimitText => Subscription.DeviceLimit switch
    {
        > 0 => Subscription.DeviceLimit.Value.ToString(CultureInfo.InvariantCulture),
        0 => "Безгранично",
        _ => "Не указано"
    };

    public string SubscriptionModalTrafficUsedText => FormatBytes(Subscription.TrafficUsedBytes);

    private SubscriptionStatus EffectiveSubscriptionStatus
    {
        get
        {
            if (Subscription.Status == SubscriptionStatus.Disabled)
            {
                return SubscriptionStatus.Disabled;
            }

            if (Subscription.ExpiresAt is null)
            {
                return Subscription.Status;
            }

            var today = DateOnly.FromDateTime(DateTime.Now);
            var expiresOn = DateOnly.FromDateTime(Subscription.ExpiresAt.Value.ToLocalTime().DateTime);
            if (today > expiresOn)
            {
                return SubscriptionStatus.Expired;
            }

            return today == expiresOn ? SubscriptionStatus.ExpiringSoon : Subscription.Status;
        }
    }
    public string ServerStatusText => _isServerAvailable
        ? "Сервер доступен"
        : "Сервер недоступен";

    public string ServerStatusColor => _isServerAvailable
        ? "#35D587"
        : "#E6585C";

    private VpnProfile? PreferredProfile => Profiles
        .OrderBy(profile => profile.ConnectionPriority)
        .FirstOrDefault();

    public string CurrentCountryName => PreferredProfile?.CountryName ?? "Нидерланды";
    public string CurrentCountryFlag => PreferredProfile?.CountryFlag ?? "🇳🇱";
    public string CurrentCountryCode => PreferredProfile?.CountryCode ?? "NL";

    public string ConnectionStatusTitle => IsConnected ? "Подключено" : "Не подключено";

    public string ConnectionStateText => IsBusy
        ? "Идет подключение"
        : IsConnected
            ? "VPN активен"
            : "VPN выключен";

    public string PowerButtonColor => IsBusy
        ? "#2F80ED"
        : IsConnected
            ? "#18A07A"
            : "#8B96A8";

    public string TrafficModeText => TrafficMode == TrafficMode.AllTraffic
        ? "Весь трафик через VPN"
        : "Через VPN только выбранные приложения";

    public string SelectedApplicationsText => $"Выбрано приложений: {GetSelectedProcessNames().Count}";

    public string LastRefreshText => LastRefresh?.ToString("dd.MM.yyyy HH:mm") ?? "Еще не обновлялась";

    public async Task InitializeAsync()
    {
        var settings = await _settingsStore.LoadAsync();
        var secrets = await _secretSettingsStore.LoadAsync();
        SubscriptionUrl = string.IsNullOrWhiteSpace(settings.SubscriptionUrl) ? secrets.SubscriptionUrl : settings.SubscriptionUrl;
        AuthApiBaseUrl = string.IsNullOrWhiteSpace(settings.AuthApiBaseUrl)
            ? AppSettings.DefaultAuthApiBaseUrl
            : settings.AuthApiBaseUrl;
        _authDeviceId = string.IsNullOrWhiteSpace(secrets.AuthDeviceId)
            ? Guid.NewGuid().ToString("N")
            : secrets.AuthDeviceId;
        if (secrets.AuthDeviceId != _authDeviceId)
        {
            secrets.AuthDeviceId = _authDeviceId;
            await _secretSettingsStore.SaveAsync(secrets);
        }
        AccountSession = settings.AccountSession ?? new AccountSession();
        TrafficMode = settings.TrafficMode;
        LastRefresh = settings.LastSubscriptionRefresh;
        LoadSavedApplications(settings.SelectedProcessNames, settings.SelectedApplicationPaths);

        OnPropertyChanged(nameof(CoreStatus));
        OnPropertyChanged(nameof(AdminStatus));
        RefreshDerivedStatus();

        await RefreshServerAvailabilityAsync();
        await RefreshSubscriptionInBackgroundAsync();
        _serverAvailabilityTimer.Start();
        _subscriptionRefreshTimer.Start();
    }

    public void Dispose()
    {
        _serverAvailabilityTimer.Stop();
        _vpnLogService.LogReceived -= OnLogReceived;
        _vpnLogService.Dispose();
    }

    private async Task EnableLogsAsync()
    {
        try
        {
            VpnLogText = TrimLogText(await _vpnLogService.StartAsync());
            IsLogMonitoring = true;
            StatusText = "Логирование включено";
        }
        catch (Exception error)
        {
            StatusText = $"Не удалось включить логирование: {error.Message}";
        }
    }

    private Task DisableLogsAsync()
    {
        _vpnLogService.Stop();
        IsLogMonitoring = false;
        StatusText = "Логирование отключено";
        return Task.CompletedTask;
    }

    private async Task ClearLogsAsync()
    {
        try
        {
            await _vpnLogService.ClearAsync();
            VpnLogText = "";
            StatusText = "Логи очищены";
        }
        catch (Exception error)
        {
            StatusText = $"Не удалось очистить логи: {error.Message}";
        }
    }

    private async Task SaveLogsAsync()
    {
        var dialog = new Microsoft.Win32.SaveFileDialog
        {
            Title = "Сохранить логи CosmoNet",
            Filter = "Текстовые файлы (*.txt)|*.txt",
            DefaultExt = ".txt",
            FileName = $"cosmonet-log-{DateTime.Now:yyyyMMdd-HHmmss}.txt"
        };

        if (dialog.ShowDialog() != true)
        {
            return;
        }

        try
        {
            await _vpnLogService.SaveAsAsync(dialog.FileName, VpnLogText);
            StatusText = "Логи сохранены";
        }
        catch (Exception error)
        {
            StatusText = $"Не удалось сохранить логи: {error.Message}";
        }
    }

    private async Task<SubscriptionLoadResult> LoadSubscriptionWithMetadataAsync()
    {
        var subscription = await _subscriptionService.LoadSubscriptionAsync(SubscriptionUrl);
        var subscriptionId = GetSubscriptionId(SubscriptionUrl);
        var clientId = subscription.Profiles.FirstOrDefault()?.Uuid;
        if (string.IsNullOrWhiteSpace(subscriptionId) && string.IsNullOrWhiteSpace(clientId))
        {
            return subscription;
        }

        try
        {
            var metadata = await _subscriptionMetadataApiClient.GetAsync(
                GetSubscriptionApiBaseUrl(),
                subscriptionId,
                clientId);
            subscription.Summary.DeviceLimit = metadata.DeviceLimit;
            subscription.Summary.ExpiresAt = metadata.ExpiresAt;
            subscription.Summary.Status = metadata.Status;
        }
        catch
        {
            // The VPN configuration remains usable when the metadata service is unavailable.
        }

        return subscription;
    }

    private string GetSubscriptionApiBaseUrl()
    {
        if (Uri.TryCreate(SubscriptionUrl.Trim(), UriKind.Absolute, out var subscriptionUri))
        {
            var metadataUri = new UriBuilder(Uri.UriSchemeHttp, subscriptionUri.Host, 8090);
            return metadataUri.Uri.GetLeftPart(UriPartial.Authority);
        }

        throw new InvalidOperationException("Subscription metadata service is unavailable.");
    }

    private static string? GetSubscriptionId(string subscriptionUrl)
    {
        if (!Uri.TryCreate(subscriptionUrl.Trim(), UriKind.Absolute, out var uri))
        {
            return null;
        }

        var segments = uri.AbsolutePath.Split('/', StringSplitOptions.RemoveEmptyEntries);
        for (var index = 0; index < segments.Length - 1; index++)
        {
            if (segments[index].Equals("sub", StringComparison.OrdinalIgnoreCase))
            {
                return segments[index + 1];
            }
        }

        return null;
    }
    public async Task RefreshSubscriptionInBackgroundAsync()
    {
        if (_isRefreshingSubscription || string.IsNullOrWhiteSpace(SubscriptionUrl))
        {
            return;
        }

        _isRefreshingSubscription = true;
        try
        {
            var subscription = await LoadSubscriptionWithMetadataAsync();
            ApplySubscription(subscription);
            LastRefresh = DateTimeOffset.Now;
            await SaveAsync(setStatus: false);
            RefreshDerivedStatus();
        }
        catch
        {
            // Keep the last successful subscription data visible while the endpoint is unavailable.
        }
        finally
        {
            _isRefreshingSubscription = false;
        }
    }
    private Task ShowAuthMethods() { IsAuthMenuOpen = true; return Task.CompletedTask; }

    private async Task LoginAsync()
    {
        IsAuthMenuOpen = false;
        if (string.IsNullOrWhiteSpace(AuthApiBaseUrl)) { AuthStatus = "Сервис авторизации временно недоступен."; StatusText = "Не задан адрес API авторизации."; return; }
        await RunBusyAsync(async () =>
        {
            var result = await _telegramAuthApiClient.StartAsync(AuthApiBaseUrl, _authDeviceId);
            _authSessionId = result.SessionId;
            AuthStatus = "Confirm sign-in in Telegram.";
            Process.Start(new ProcessStartInfo(result.TelegramDeepLink) { UseShellExecute = true });
            _ = PollAuthorizationAsync(result.SessionId, result.ExpiresAt);
        });
    }

    private async Task PollAuthorizationAsync(string sessionId, DateTimeOffset expiresAt)
    {
        while (_authSessionId == sessionId && DateTimeOffset.UtcNow < expiresAt)
        {
            await Task.Delay(TimeSpan.FromSeconds(2));
            var result = await _telegramAuthApiClient.GetStatusAsync(AuthApiBaseUrl, _authDeviceId, sessionId);
            AuthStatus = result.Message;
            if (!result.IsAuthorized) { if (!result.IsPending) _authSessionId = ""; continue; }
            var secrets = await _secretSettingsStore.LoadAsync();
            secrets.AuthToken = result.AuthToken; secrets.SubscriptionUrl = result.SubscriptionUrl;
            await _secretSettingsStore.SaveAsync(secrets);
            SubscriptionUrl = result.SubscriptionUrl;
            AccountSession = new AccountSession { IsAuthorized = true, DisplayName = result.DisplayName, AuthorizedAt = DateTimeOffset.Now, Subscription = result.Subscription ?? new SubscriptionSummary() };
            _authSessionId = "";
            await SaveAsync(setStatus: false);
            return;
        }
    }

    private async Task LogoutAsync()
    {
        await RunBusyAsync(async () =>
        {
            if (IsConnected) await DisconnectAsync();
            var secrets = await _secretSettingsStore.LoadAsync();
            if (!string.IsNullOrWhiteSpace(secrets.AuthToken) && !string.IsNullOrWhiteSpace(AuthApiBaseUrl))
                try { await _telegramAuthApiClient.LogoutAsync(AuthApiBaseUrl, secrets.AuthToken); } catch { }
            secrets.AuthToken = ""; secrets.SubscriptionUrl = "";
            await _secretSettingsStore.SaveAsync(secrets);
            SubscriptionUrl = ""; ReplaceProfiles([]); AccountSession = new AccountSession();
            await SaveAsync(setStatus: false);
        });
    }

    private bool CanSubmitFeedback()
    {
        return !IsBusy && !IsSubmittingFeedback &&
               !string.IsNullOrWhiteSpace(FeedbackName) &&
               !string.IsNullOrWhiteSpace(FeedbackContacts) &&
               !string.IsNullOrWhiteSpace(FeedbackMessage);
    }

    private async Task SubmitFeedbackAsync()
    {
        if (!CanSubmitFeedback())
        {
            FeedbackStatus = "Заполните имя, контакт и текст обращения.";
            return;
        }

        IsSubmittingFeedback = true;
        FeedbackStatus = "Отправляем обращение...";

        try
        {
            await _feedbackApiClient.SendAsync(
                GetFeedbackApiBaseUrl(),
                FeedbackName,
                FeedbackContacts,
                FeedbackMessage);

            FeedbackMessage = "";
            FeedbackStatus = "Обращение отправлено. Мы свяжемся с вами по указанному контакту.";
        }
        catch
        {
            FeedbackStatus = "Не удалось отправить обращение. Попробуйте позже.";
        }
        finally
        {
            IsSubmittingFeedback = false;
        }
    }


    private string GetFeedbackApiBaseUrl()
    {
        if (!string.IsNullOrWhiteSpace(AuthApiBaseUrl))
        {
            return AuthApiBaseUrl;
        }

        if (Uri.TryCreate(SubscriptionUrl.Trim(), UriKind.Absolute, out var subscriptionUri))
        {
            return subscriptionUri.GetLeftPart(UriPartial.Authority);
        }

        throw new InvalidOperationException("Сервис обратной связи недоступен.");
    }
    private async Task RefreshAsync()
    {
        await RunBusyAsync(async () =>
        {
            StatusText = "Обновляем подписку...";
            var subscription = await LoadSubscriptionWithMetadataAsync();
            ApplySubscription(subscription);
            LastRefresh = DateTimeOffset.Now;
            await SaveAsync(setStatus: false);
            StatusText = $"Подписка обновлена: профилей {Profiles.Count}";
            RefreshDerivedStatus();
        });
    }

    private async Task ToggleConnectionAsync()
    {
        if (IsBusy)
        {
            return;
        }

        if (IsConnected)
        {
            await DisconnectAsync();
            return;
        }

        await ConnectAsync();
    }

    private async Task ConnectAsync()
    {
        await RunBusyAsync(async () =>
        {
            if (Profiles.Count == 0)
            {
                StatusText = "Загружаем подписку...";
                ApplySubscription(await LoadSubscriptionWithMetadataAsync());
            }

            var selectedProcesses = GetSelectedProcessNames();
            AppendDiagnosticLog($"Запуск sing-box. Профили: {GetProfileTypes()}.");
            StatusText = "Готовим конфиг...";
            var configPath = await _configBuilder.WriteConfigAsync(Profiles, TrafficMode, selectedProcesses);
            LastConfigPath = configPath;
            StatusText = "Проверяем конфиг sing-box...";
            var diagnostic = await _singBoxService.CheckConfigAsync(configPath);
            ApplyDiagnosticResult(diagnostic);
            if (!diagnostic.Success)
            {
                AppendDiagnosticLog("Ошибка проверки конфигурации sing-box. Код: CONFIG_CHECK_FAILED.");
                throw new InvalidOperationException(diagnostic.Message);
            }

            await _singBoxService.StartAsync(configPath, useTunMode: true);
            AppendDiagnosticLog("sing-box запущен. Подключение успешно.");
            IsConnected = true;
            await RefreshServerAvailabilityAsync();
            LastRefresh ??= DateTimeOffset.Now;
            await SaveAsync(setStatus: false);
            StatusText = TrafficMode == TrafficMode.AllTraffic
                ? "Подключено: весь трафик идет через VPN"
                : $"Подключено: приложений через VPN {selectedProcesses.Count}";
            RefreshDerivedStatus();
        });
    }

    private Task DisconnectAsync()
    {
        AppendDiagnosticLog("Остановка процесса sing-box.");
        _singBoxService.Stop();
        IsConnected = false;
        StatusText = "Отключено";
        _ = RefreshServerAvailabilityAsync();
        RefreshDerivedStatus();
        return Task.CompletedTask;
    }

    private async Task RunDiagnosticsAsync()
    {
        await RunBusyAsync(async () =>
        {
            if (Profiles.Count == 0)
            {
                StatusText = "Для полной проверки сначала обновите подписку.";
                DiagnosticText = BuildBasicDiagnostic("Профили подписки пока не загружены.");
                return;
            }

            var selectedProcesses = GetSelectedProcessNames();
            StatusText = "Генерируем и проверяем конфиг...";
            var configPath = await _configBuilder.WriteConfigAsync(Profiles, TrafficMode, selectedProcesses);
            LastConfigPath = configPath;
            var diagnostic = await _singBoxService.CheckConfigAsync(configPath);
            ApplyDiagnosticResult(diagnostic);
            StatusText = diagnostic.Message;
        });
    }

    private async Task BrowseApplicationAsync()
    {
        var dialog = new Microsoft.Win32.OpenFileDialog
        {
            Title = "Выберите приложение",
            Filter = "Приложения Windows (*.exe)|*.exe"
        };

        if (dialog.ShowDialog() == true)
        {
            await AddApplicationAsync(dialog.FileName, dialog.FileName);
        }
    }

    private async Task AddApplicationAsync(string value, string path)
    {
        await RunBusyAsync(async () =>
        {
            var processName = NormalizeProcessName(value);
            if (string.IsNullOrWhiteSpace(processName))
            {
                StatusText = "Выберите приложение или введите имя процесса";
                return;
            }

            var fullPath = File.Exists(path) ? path : "";
            var existing = AvailableApplications.FirstOrDefault(
                app => app.ProcessName.Equals(processName, StringComparison.OrdinalIgnoreCase));

            if (existing is not null && string.IsNullOrWhiteSpace(fullPath))
            {
                existing.IsSelected = true;
            }
            else
            {
                if (existing is not null)
                {
                    AvailableApplications.Remove(existing);
                }

                AvailableApplications.Insert(0, new InstalledApplication
                {
                    DisplayName = Path.GetFileNameWithoutExtension(processName),
                    ProcessName = processName,
                    Path = fullPath,
                    Icon = _applicationIconService.GetIcon(fullPath),
                    IsSelected = true
                });
            }

            await SaveAsync(setStatus: false);
            StatusText = $"Добавлено приложение: {processName}";
            OnPropertyChanged(nameof(SelectedApplicationsText));
        });
    }

    public async Task RemoveApplicationAsync(InstalledApplication application)
    {
        if (!AvailableApplications.Contains(application))
        {
            return;
        }

        await RunBusyAsync(async () =>
        {
            AvailableApplications.Remove(application);
            await SaveAsync(setStatus: false);
            StatusText = $"Удалено приложение: {application.ProcessName}";
        });
    }

    private Task SaveAsync()
    {
        return SaveAsync(setStatus: true);
    }

    private async Task SaveAsync(bool setStatus)
    {
        await _settingsStore.SaveAsync(new AppSettings
        {
            SubscriptionUrl = SubscriptionUrl.Trim(),
            AuthApiBaseUrl = AuthApiBaseUrl.Trim(),
            TrafficMode = TrafficMode,
            LastSubscriptionRefresh = LastRefresh,
            SelectedProcessNames = GetSelectedProcessNames().ToList(),
            SelectedApplicationPaths = GetSelectedApplicationPaths(),
            AccountSession = AccountSession
        });

        if (setStatus)
        {
            StatusText = IsConnected
                ? "Настройки сохранены и будут применены после переподключения"
                : "Настройки сохранены";
            SettingsSaved?.Invoke(this, EventArgs.Empty);
        }

        OnPropertyChanged(nameof(SelectedApplicationsText));
    }

    private Task OpenDataFolderAsync()
    {
        AppPaths.EnsureDataDirectory();
        Process.Start(new ProcessStartInfo
        {
            FileName = AppPaths.DataDirectory,
            UseShellExecute = true
        });

        return Task.CompletedTask;
    }

    private void LoadSavedApplications(
        IEnumerable<string> selectedProcessNames,
        IReadOnlyDictionary<string, string>? selectedApplicationPaths)
    {
        AvailableApplications.Clear();

        foreach (var processName in selectedProcessNames.Select(NormalizeProcessName).Where(name => !string.IsNullOrWhiteSpace(name)).Distinct(StringComparer.OrdinalIgnoreCase))
        {
            var path = selectedApplicationPaths is not null &&
                selectedApplicationPaths.TryGetValue(processName, out var savedPath) &&
                File.Exists(savedPath)
                ? savedPath
                : "";

            AvailableApplications.Add(new InstalledApplication
            {
                DisplayName = Path.GetFileNameWithoutExtension(processName),
                ProcessName = processName,
                Path = path,
                Icon = _applicationIconService.GetIcon(path),
                IsSelected = true
            });
        }

        OnPropertyChanged(nameof(SelectedApplicationsText));
    }

    private List<string> GetSelectedProcessNames()
    {
        return AvailableApplications
            .Where(app => app.IsSelected)
            .Select(app => NormalizeProcessName(app.ProcessName))
            .Where(name => !string.IsNullOrWhiteSpace(name))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .OrderBy(name => name, StringComparer.OrdinalIgnoreCase)
            .ToList();
    }

    private Dictionary<string, string> GetSelectedApplicationPaths()
    {
        return AvailableApplications
            .Where(app => app.IsSelected && File.Exists(app.Path))
            .GroupBy(app => NormalizeProcessName(app.ProcessName), StringComparer.OrdinalIgnoreCase)
            .Where(group => !string.IsNullOrWhiteSpace(group.Key))
            .ToDictionary(group => group.Key, group => group.First().Path, StringComparer.OrdinalIgnoreCase);
    }

    private void OnAvailableApplicationsChanged(object? sender, NotifyCollectionChangedEventArgs e)
    {
        if (e.OldItems is not null)
        {
            foreach (InstalledApplication application in e.OldItems)
            {
                application.PropertyChanged -= OnApplicationPropertyChanged;
            }
        }

        if (e.NewItems is not null)
        {
            foreach (InstalledApplication application in e.NewItems)
            {
                application.PropertyChanged += OnApplicationPropertyChanged;
            }
        }
    }

    private void OnApplicationPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(InstalledApplication.IsSelected))
        {
            OnPropertyChanged(nameof(SelectedApplicationsText));
        }
    }

    private void OnLogReceived(object? sender, string content)
    {
        VpnLogText = TrimLogText(VpnLogText + content);
    }

    private void AppendDiagnosticLog(string message)
    {
        if (!IsLogMonitoring)
        {
            return;
        }

        VpnLogText = TrimLogText($"{VpnLogText}[{DateTimeOffset.Now:HH:mm:ss}] [CosmoNet] {message}{Environment.NewLine}");
    }

    private string GetProfileTypes()
    {
        var types = Profiles
            .OrderBy(profile => profile.ConnectionPriority)
            .Select(profile => profile.Security.Equals("reality", StringComparison.OrdinalIgnoreCase)
                ? "REALITY"
                : profile.Network.Equals("ws", StringComparison.OrdinalIgnoreCase)
                    ? "WS"
                    : "TCP")
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();

        return types.Count == 0 ? "не определены" : string.Join(" -> ", types);
    }

    private static string TrimLogText(string text)
    {
        return text.Length <= MaximumLogTextLength
            ? text
            : text[^MaximumLogTextLength..];
    }

    private static string NormalizeProcessName(string value)
    {
        var processName = value.Trim().Trim('"');
        if (string.IsNullOrWhiteSpace(processName))
        {
            return "";
        }

        processName = Path.GetFileName(processName);
        return processName.EndsWith(".exe", StringComparison.OrdinalIgnoreCase)
            ? processName
            : $"{processName}.exe";
    }

    private void ApplySubscription(SubscriptionLoadResult subscription)
    {
        var previousExpiresAt = Subscription.ExpiresAt;

        ReplaceProfiles(subscription.Profiles);
        _ = RefreshServerAvailabilityAsync();

        AccountSession = new AccountSession
        {
            IsAuthorized = AccountSession.IsAuthorized,
            DisplayName = AccountSession.IsAuthorized ? AccountSession.DisplayName : "Подписка загружена по ссылке",
            AuthorizedAt = AccountSession.AuthorizedAt,
            Subscription = subscription.Summary
        };

        NotifyAboutSubscriptionChange(previousExpiresAt, subscription.Summary.ExpiresAt);
    }

    private void NotifyAboutSubscriptionChange(DateTimeOffset? previousExpiresAt, DateTimeOffset? currentExpiresAt)
    {
        if (currentExpiresAt is null)
        {
            return;
        }

        var currentLocal = currentExpiresAt.Value.ToLocalTime();
        if (previousExpiresAt is { } previous && currentLocal > previous.ToLocalTime())
        {
            var extendedDays = Math.Max(1, (currentLocal.Date - previous.ToLocalTime().Date).Days);
            _expiryWarningShownFor = null;
            SubscriptionNotificationRequested?.Invoke(
                this,
                $"Подписка продлена на {extendedDays} дн. Новая дата: {currentLocal:dd.MM.yyyy}.");
            return;
        }

        var daysLeft = (currentLocal.Date - DateTime.Now.Date).Days;
        if (daysLeft is >= 0 and <= 3 && _expiryWarningShownFor != currentExpiresAt)
        {
            _expiryWarningShownFor = currentExpiresAt;
            SubscriptionNotificationRequested?.Invoke(
                this,
                "До окончания подписки осталось менее 3 дней. Продлите подписку, иначе VPN перестанет работать.");
        }
    }

    private void ReplaceProfiles(IEnumerable<VpnProfile> profiles)
    {
        Profiles.Clear();
        foreach (var profile in profiles)
        {
            Profiles.Add(profile);
        }
    }

    private async Task RefreshServerAvailabilityAsync()
    {
        if (_isCheckingServerAvailability)
        {
            return;
        }

        _isCheckingServerAvailability = true;
        try
        {
            var profile = PreferredProfile;
            var host = !string.IsNullOrWhiteSpace(profile?.Server)
                ? profile.Server
                : DefaultProbeHost;
            var port = profile?.Port > 0
                ? profile.Port
                : DefaultProbePort;

            var isAvailable = false;
            try
            {
                using var client = new TcpClient();
                using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(3));
                await client.ConnectAsync(host, port, timeout.Token);
                isAvailable = client.Connected;
            }
            catch
            {
                isAvailable = false;
            }

            if (_isServerAvailable != isAvailable)
            {
                _isServerAvailable = isAvailable;
                OnPropertyChanged(nameof(ServerStatusText));
                OnPropertyChanged(nameof(ServerStatusColor));
            }
        }
        finally
        {
            _isCheckingServerAvailability = false;
        }
    }

    private async Task RunBusyAsync(Func<Task> action)
    {
        try
        {
            IsBusy = true;
            await action();
        }
        catch (Exception error)
        {
            AppendDiagnosticLog($"Ошибка подключения. Код: 0x{error.HResult:X8}.");
            StatusText = error.Message;
        }
        finally
        {
            IsBusy = false;
        }
    }

    private void ApplyDiagnosticResult(CoreDiagnosticResult result)
    {
        DiagnosticText = string.IsNullOrWhiteSpace(result.Details)
            ? BuildBasicDiagnostic(result.Message)
            : $"{BuildBasicDiagnostic(result.Message)}{Environment.NewLine}{Environment.NewLine}{result.Details}";
    }

    private string BuildBasicDiagnostic(string headline)
    {
        return string.Join(Environment.NewLine,
        [
            headline,
            $"Ядро: {AppPaths.BundledSingBoxPath}",
            $"Конфиг: {LastConfigPath}",
            $"Администратор: {(_singBoxService.IsAdministrator ? "да" : "нет")}",
            $"Хранилище секретов: {AppPaths.SecretSettingsPath}",
            $"Режим трафика: {TrafficModeText}",
            SelectedApplicationsText
        ]);
    }

    private void RefreshDerivedStatus()
    {
        OnPropertyChanged(nameof(ServerStatusText));
        OnPropertyChanged(nameof(ServerStatusColor));
        OnPropertyChanged(nameof(CurrentCountryName));
        OnPropertyChanged(nameof(CurrentCountryFlag));
        OnPropertyChanged(nameof(CurrentCountryCode));
        OnPropertyChanged(nameof(ConnectionStatusTitle));
        OnPropertyChanged(nameof(ConnectionStateText));
        OnPropertyChanged(nameof(PowerButtonColor));
        OnPropertyChanged(nameof(TrafficModeText));
        OnPropertyChanged(nameof(SelectedApplicationsText));
        RefreshSubscriptionStatus();
    }

    private void RefreshSubscriptionStatus()
    {
        OnPropertyChanged(nameof(Subscription));
        OnPropertyChanged(nameof(AccountDisplayText));
        OnPropertyChanged(nameof(SubscriptionExpiresText));
        OnPropertyChanged(nameof(SubscriptionStatusText));
        OnPropertyChanged(nameof(SubscriptionTariffText));
        OnPropertyChanged(nameof(SubscriptionDevicesText));
        OnPropertyChanged(nameof(SubscriptionTrafficText));
        OnPropertyChanged(nameof(SubscriptionLastSyncText));
        OnPropertyChanged(nameof(SubscriptionModalStatusText));
        OnPropertyChanged(nameof(SubscriptionModalStatusColor));
        OnPropertyChanged(nameof(SubscriptionModalExpiresText));
        OnPropertyChanged(nameof(SubscriptionModalCountryText));
        OnPropertyChanged(nameof(SubscriptionModalDeviceLimitText));
        OnPropertyChanged(nameof(SubscriptionModalTrafficUsedText));
    }

    private static string FormatBytes(long bytes)
    {
        if (bytes <= 0)
        {
            return "0 Б";
        }

        string[] units = ["Б", "КБ", "МБ", "ГБ", "ТБ"];
        var value = (double)bytes;
        var unit = 0;

        while (value >= 1024 && unit < units.Length - 1)
        {
            value /= 1024;
            unit++;
        }

        return $"{value:0.#} {units[unit]}";
    }

    private void RaiseCommandStates()
    {
        ((RelayCommand)RefreshCommand).RaiseCanExecuteChanged();
        ((RelayCommand)DisconnectCommand).RaiseCanExecuteChanged();
        ((RelayCommand)SaveCommand).RaiseCanExecuteChanged();
        ((RelayCommand)BrowseApplicationCommand).RaiseCanExecuteChanged();
        ((RelayCommand)ShowAuthMethodsCommand).RaiseCanExecuteChanged();
        ((RelayCommand)LoginCommand).RaiseCanExecuteChanged();
        ((RelayCommand)LogoutCommand).RaiseCanExecuteChanged();
        ((RelayCommand)RunDiagnosticsCommand).RaiseCanExecuteChanged();
        RaiseFeedbackCommandState();
    }

    private void RaiseFeedbackCommandState()
    {
        ((RelayCommand)SubmitFeedbackCommand).RaiseCanExecuteChanged();
    }
    private void RaiseLogCommandStates()
    {
        ((RelayCommand)EnableLogsCommand).RaiseCanExecuteChanged();
        ((RelayCommand)DisableLogsCommand).RaiseCanExecuteChanged();
    }

    private bool SetField<T>(ref T field, T value, [CallerMemberName] string? propertyName = null)
    {
        if (EqualityComparer<T>.Default.Equals(field, value))
        {
            return false;
        }

        field = value;
        OnPropertyChanged(propertyName);
        return true;
    }

    private void OnPropertyChanged([CallerMemberName] string? propertyName = null)
    {
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
    }
}
