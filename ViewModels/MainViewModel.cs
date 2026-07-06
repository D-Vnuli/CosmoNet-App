using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Diagnostics;
using System.Runtime.CompilerServices;
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

    private string _subscriptionUrl = "";
    private string _statusText = "Готов к настройке";
    private bool _isBusy;
    private bool _isConnected;
    private bool _useTunMode = true;
    private DateTimeOffset? _lastRefresh;

    public MainViewModel()
    {
        RefreshCommand = new RelayCommand(RefreshAsync, () => !IsBusy && !IsConnected);
        ConnectCommand = new RelayCommand(ConnectAsync, () => !IsBusy && !IsConnected);
        DisconnectCommand = new RelayCommand(DisconnectAsync, () => !IsBusy && IsConnected);
        SaveCommand = new RelayCommand(SaveAsync, () => !IsBusy);
        OpenDataFolderCommand = new RelayCommand(OpenDataFolderAsync);
    }

    public event PropertyChangedEventHandler? PropertyChanged;

    public ObservableCollection<VpnProfile> Profiles { get; } = new();

    public ICommand RefreshCommand { get; }
    public ICommand ConnectCommand { get; }
    public ICommand DisconnectCommand { get; }
    public ICommand SaveCommand { get; }
    public ICommand OpenDataFolderCommand { get; }

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

    public bool IsBusy
    {
        get => _isBusy;
        set
        {
            if (SetField(ref _isBusy, value))
            {
                RaiseCommandStates();
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
            }
        }
    }

    public bool UseTunMode
    {
        get => _useTunMode;
        set => SetField(ref _useTunMode, value);
    }

    public DateTimeOffset? LastRefresh
    {
        get => _lastRefresh;
        set => SetField(ref _lastRefresh, value);
    }

    public string CoreStatus => _singBoxService.IsCoreAvailable
        ? "Ядро sing-box найдено"
        : "Нужно добавить Resources\\sing-box\\sing-box.exe";

    public string AdminStatus => _singBoxService.IsAdministrator
        ? "Запущено с правами администратора"
        : "TUN потребует запуск от администратора";

    public async Task InitializeAsync()
    {
        var settings = await _settingsStore.LoadAsync();
        SubscriptionUrl = settings.SubscriptionUrl;
        UseTunMode = settings.UseTunMode;
        LastRefresh = settings.LastSubscriptionRefresh;
        OnPropertyChanged(nameof(CoreStatus));
        OnPropertyChanged(nameof(AdminStatus));
    }

    private async Task RefreshAsync()
    {
        await RunBusyAsync(async () =>
        {
            StatusText = "Обновляем подписку...";
            var profiles = await _subscriptionService.LoadProfilesAsync(SubscriptionUrl);
            ReplaceProfiles(profiles);
            LastRefresh = DateTimeOffset.Now;
            await SaveAsync();
            StatusText = $"Подписка обновлена: профилей {Profiles.Count}";
        });
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

            StatusText = "Готовим конфиг...";
            var configPath = await _configBuilder.WriteConfigAsync(Profiles, UseTunMode);
            _singBoxService.Start(configPath, UseTunMode);
            IsConnected = true;
            StatusText = UseTunMode
                ? "Подключено через TUN"
                : "Локальный proxy запущен на 127.0.0.1:20808";
        });
    }

    private Task DisconnectAsync()
    {
        _singBoxService.Stop();
        IsConnected = false;
        StatusText = "Отключено";
        return Task.CompletedTask;
    }

    private async Task SaveAsync()
    {
        await _settingsStore.SaveAsync(new AppSettings
        {
            SubscriptionUrl = SubscriptionUrl.Trim(),
            UseTunMode = UseTunMode,
            LastSubscriptionRefresh = LastRefresh
        });

        StatusText = "Настройки сохранены";
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

    private void RaiseCommandStates()
    {
        ((RelayCommand)RefreshCommand).RaiseCanExecuteChanged();
        ((RelayCommand)ConnectCommand).RaiseCanExecuteChanged();
        ((RelayCommand)DisconnectCommand).RaiseCanExecuteChanged();
        ((RelayCommand)SaveCommand).RaiseCanExecuteChanged();
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
