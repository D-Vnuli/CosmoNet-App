namespace CosmoNet.App.Models;

public sealed class InstalledApplication : System.ComponentModel.INotifyPropertyChanged
{
    private bool _isSelected;

    public event System.ComponentModel.PropertyChangedEventHandler? PropertyChanged;

    public string DisplayName { get; init; } = "";
    public string ProcessName { get; init; } = "";
    public string Path { get; init; } = "";
    public System.Windows.Media.ImageSource? Icon { get; init; }
    public bool IsSelected
    {
        get => _isSelected;
        set
        {
            if (_isSelected == value)
            {
                return;
            }

            _isSelected = value;
            PropertyChanged?.Invoke(this, new System.ComponentModel.PropertyChangedEventArgs(nameof(IsSelected)));
        }
    }

    public string RouteLabel => string.IsNullOrWhiteSpace(Path)
        ? ProcessName
        : $"{ProcessName} - {Path}";
}
