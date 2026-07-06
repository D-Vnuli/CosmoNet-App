namespace CosmoNet.App.Models;

public sealed class VpnProfile
{
    public string Name { get; init; } = "CosmoNet";
    public string Protocol { get; init; } = "";
    public string Uuid { get; init; } = "";
    public string Server { get; init; } = "";
    public int Port { get; init; }
    public Dictionary<string, string> Query { get; init; } = new(StringComparer.OrdinalIgnoreCase);

    public string DisplayName => $"{Name} - {Server}:{Port}";
    public string Security => Query.GetValueOrDefault("security", "");
    public string Network => Query.GetValueOrDefault("type", "tcp");
}
