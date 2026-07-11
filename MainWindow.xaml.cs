using System.ComponentModel;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using CosmoNet.App.ViewModels;

namespace CosmoNet.App;

public partial class MainWindow : Window, INotifyPropertyChanged
{
    private readonly MainViewModel _viewModel = new();
    private bool _isMenuOpen;

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

    public MainWindow()
    {
        ToggleMenuCommand = new RelayCommand(ToggleMenuAsync);
        InitializeComponent();
        DataContext = _viewModel;
        Loaded += OnLoaded;
        Closed += (_, _) => _viewModel.DisconnectCommand.Execute(null);
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
        if (sender is ListBox menu && menu.SelectedIndex >= 0)
        {
            MainViews.SelectedIndex = menu.SelectedIndex;
            IsMenuOpen = false;
        }
    }

    private void OnMenuNavigationMouseUp(object sender, MouseButtonEventArgs e)
    {
        IsMenuOpen = false;
    }
}
