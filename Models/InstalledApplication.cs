namespace CosmoNet.App.Models;

public sealed class InstalledApplication
{
    public string DisplayName { get; init; } = "";
    public string ProcessName { get; init; } = "";
    public string Path { get; init; } = "";
    public bool IsSelected { get; set; }

    public string RouteLabel => string.IsNullOrWhiteSpace(Path)
        ? ProcessName
        : $"{ProcessName} - {Path}";
}
