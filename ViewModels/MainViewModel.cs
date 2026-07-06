using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Diagnostics;
using System.Runtime.CompilerServices;
using System.Security.Cryptography;
using System.Windows.Input;
using CosmoNet.App.Models;
using CosmoNet.App.Services;

namespace CosmoNet.App.ViewModels;

public sealed class MainViewModel : INotifyPropertyChanged
{
    private readonly SettingsStore _settingsStore = new();
    private readonly SubscriptionService _subscriptionService = new();
    private readonly SingBoxConfigBuilder _configBuilder = new();
    private readonly SingBoxService _singBoxService = new();
    private readonly InstalledApplicationsService _applicationsService = new();

    private string _subscriptionUrl = "";
    private string _statusText = "Готов к настройке";
    private string _authStatus = "Войдите через Telegram, чтобы приложение могло получить вашу подписку.";
    private string _loginCode = "------";
    private string _manualProcessName = "";
    private string _diagnosticText = "Диагностика еще не запускалась.";
    private string _lastConfigPath = AppPaths.GeneratedConfigPath;
    private AccountSession _accountSession = new();
    private bool _isBusy;
    private bool _isConnected;
    private TrafficMode _trafficMode = TrafficMode.AllTraffic;
    private DateTimeOffset? _lastRefresh;

    public MainViewModel()
    {
        RefreshCommand = new RelayCommand(RefreshAsync, () => !IsBusy && !IsConnected);
        PowerCommand = new RelayCommand(ToggleConnectionAsync);
        DisconnectCommand = new RelayCommand(DisconnectAsync, () => !IsBusy && IsConnected);
        SaveCommand = new RelayCommand(SaveAsync, () => !IsBusy);
        OpenDataFolderCommand = new RelayCommand(OpenDataFolderAsync);
        LoadApplicationsCommand = new RelayCommand(LoadApplicationsAsync, () => !IsBusy);
        AddManualApplicationCommand = new RelayCommand(AddManualApplicationAsync, () => !IsBusy);
        GenerateLoginCodeCommand = new RelayCommand(GenerateLoginCodeAsync, () => !IsBusy);
        RunDiagnosticsCommand = new RelayCommand(RunDiagnosticsAsync, () => !IsBusy);
    }

    public event PropertyChangedEventHandler? PropertyChanged;

    public ObservableCollection<VpnProfile> Profiles { get; } = new();
    public ObservableCollection<InstalledApplication> AvailableApplications { get; } = new();

    public ICommand RefreshCommand { get; }
    public ICommand PowerCommand { get; }
    public ICommand DisconnectCommand { get; }
    public ICommand SaveCommand { get; }
    public ICommand OpenDataFolderCommand { get; }
    public ICommand LoadApplicationsCommand { get; }
    public ICommand AddManualApplicationCommand { get; }
    public ICommand GenerateLoginCodeCommand { get; }
    public ICommand RunDiagnosticsCommand { get; }

    public string SubscriptionUrl
    {
        get => _subscriptionUrl;
        set => SetField(ref _subscriptionUrl, value);
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

    public string LoginCode
    {
        get => _loginCode;
        set => SetField(ref _loginCode, value);
    }

    public string ManualProcessName
    {
        get => _manualProcessName;
        set => SetField(ref _manualProcessName, value);
    }

    public string DiagnosticText
    {
        get => _diagnosticText;
        set => SetField(ref _diagnosticText, value);
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
            }
        }
    }

    public SubscriptionSummary Subscription => AccountSession.Subscription ?? SubscriptionSummary.Empty;

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
        ? "Ожидает Telegram"
        : Subscription.ExpiresAt.Value.ToLocalTime().ToString("dd.MM.yyyy");

    public string SubscriptionStatusText => Subscription.Status switch
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

    public string SubscriptionDevicesText => Subscription.DeviceLimit > 0
        ? $"Устройств: до {Subscription.DeviceLimit}"
        : "Лимит устройств появится после синхронизации";

    public string SubscriptionTrafficText => Subscription.TrafficLimitBytes > 0
        ? $"Трафик: {FormatBytes(Subscription.TrafficUsedBytes)} / {FormatBytes(Subscription.TrafficLimitBytes)}"
        : $"Использовано: {FormatBytes(Subscription.TrafficUsedBytes)}";

    public string SubscriptionLastSyncText => Subscription.LastSyncedAt is null
        ? "Синхронизация еще не выполнялась"
        : $"Синхронизация: {Subscription.LastSyncedAt.Value.ToLocalTime():dd.MM.yyyy HH:mm}";

    public string ServerStatusText => Profiles.Count > 0
        ? $"Доступно профилей: {Profiles.Count}"
        : "Ожидает подписку";

    public string CurrentServerText => Profiles.FirstOrDefault()?.DisplayName ?? "Сервер не выбран";

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
        SubscriptionUrl = settings.SubscriptionUrl;
        AccountSession = settings.AccountSession ?? new AccountSession();
        TrafficMode = settings.TrafficMode;
        LastRefresh = settings.LastSubscriptionRefresh;
        LoadSavedApplications(settings.SelectedProcessNames);

        OnPropertyChanged(nameof(CoreStatus));
        OnPropertyChanged(nameof(AdminStatus));
        RefreshDerivedStatus();
    }

    private async Task GenerateLoginCodeAsync()
    {
        await RunBusyAsync(() =>
        {
            LoginCode = RandomNumberGenerator.GetInt32(100000, 1000000).ToString();
            AccountSession = new AccountSession
            {
                IsAuthorized = false,
                DisplayName = "Ожидает подтверждение Telegram",
                Subscription = new SubscriptionSummary
                {
                    Status = SubscriptionStatus.Unknown,
                    TariffName = "Ожидает подтверждение",
                    LastSyncedAt = DateTimeOffset.Now
                }
            };
            AuthStatus = "Отправьте этот код нашему Telegram-боту. После подтверждения приложение сможет получить подписку.";
            StatusText = "Код Telegram-авторизации создан";
            return Task.CompletedTask;
        });
    }

    private async Task RefreshAsync()
    {
        await RunBusyAsync(async () =>
        {
            StatusText = "Обновляем подписку...";
            var profiles = await _subscriptionService.LoadProfilesAsync(SubscriptionUrl);
            ReplaceProfiles(profiles);
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
                ReplaceProfiles(await _subscriptionService.LoadProfilesAsync(SubscriptionUrl));
            }

            var selectedProcesses = GetSelectedProcessNames();
            StatusText = "Готовим конфиг...";
            var configPath = await _configBuilder.WriteConfigAsync(Profiles, TrafficMode, selectedProcesses);
            LastConfigPath = configPath;
            StatusText = "Проверяем конфиг sing-box...";
            var diagnostic = await _singBoxService.CheckConfigAsync(configPath);
            ApplyDiagnosticResult(diagnostic);
            if (!diagnostic.Success)
            {
                throw new InvalidOperationException(diagnostic.Message);
            }

            _singBoxService.Start(configPath, useTunMode: true);
            IsConnected = true;
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
        _singBoxService.Stop();
        IsConnected = false;
        StatusText = "Отключено";
        RefreshDerivedStatus();
        return Task.CompletedTask;
    }

    private Task LoadApplicationsAsync()
    {
        return LoadApplicationsAsync(GetSelectedProcessNames());
    }

    private async Task LoadApplicationsAsync(IEnumerable<string> selectedProcessNames)
    {
        await RunBusyAsync(async () =>
        {
            StatusText = "Обновляем список приложений...";
            var apps = await _applicationsService.LoadApplicationsAsync(selectedProcessNames);
            AvailableApplications.Clear();

            foreach (var app in apps)
            {
                AvailableApplications.Add(app);
            }

            StatusText = $"Найдено приложений: {AvailableApplications.Count}";
            OnPropertyChanged(nameof(SelectedApplicationsText));
        });
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

    private async Task AddManualApplicationAsync()
    {
        await RunBusyAsync(async () =>
        {
            var processName = NormalizeProcessName(ManualProcessName);
            if (string.IsNullOrWhiteSpace(processName))
            {
                StatusText = "Введите имя процесса, например discord.exe";
                return;
            }

            var existing = AvailableApplications.FirstOrDefault(
                app => app.ProcessName.Equals(processName, StringComparison.OrdinalIgnoreCase));

            if (existing is not null)
            {
                existing.IsSelected = true;
            }
            else
            {
                AvailableApplications.Insert(0, new InstalledApplication
                {
                    DisplayName = Path.GetFileNameWithoutExtension(processName),
                    ProcessName = processName,
                    IsSelected = true
                });
            }

            ManualProcessName = "";
            await SaveAsync(setStatus: false);
            StatusText = $"Добавлено приложение: {processName}";
            OnPropertyChanged(nameof(SelectedApplicationsText));
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
            TrafficMode = TrafficMode,
            LastSubscriptionRefresh = LastRefresh,
            SelectedProcessNames = GetSelectedProcessNames().ToList(),
            AccountSession = AccountSession
        });

        if (setStatus)
        {
            StatusText = "Настройки сохранены";
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

    private void LoadSavedApplications(IEnumerable<string> selectedProcessNames)
    {
        AvailableApplications.Clear();

        foreach (var processName in selectedProcessNames.Select(NormalizeProcessName).Where(name => !string.IsNullOrWhiteSpace(name)).Distinct(StringComparer.OrdinalIgnoreCase))
        {
            AvailableApplications.Add(new InstalledApplication
            {
                DisplayName = Path.GetFileNameWithoutExtension(processName),
                ProcessName = processName,
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

    private void ReplaceProfiles(IEnumerable<VpnProfile> profiles)
    {
        Profiles.Clear();
        foreach (var profile in profiles)
        {
            Profiles.Add(profile);
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
        OnPropertyChanged(nameof(CurrentServerText));
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
        ((RelayCommand)LoadApplicationsCommand).RaiseCanExecuteChanged();
        ((RelayCommand)AddManualApplicationCommand).RaiseCanExecuteChanged();
        ((RelayCommand)GenerateLoginCodeCommand).RaiseCanExecuteChanged();
        ((RelayCommand)RunDiagnosticsCommand).RaiseCanExecuteChanged();
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


