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

    public string CountryCode => ResolveCountry().Code;
    public string CountryName => ResolveCountry().Name;
    public string CountryFlag => ResolveCountry().Flag;

    private (string Code, string Name, string Flag) ResolveCountry()
    {
        var explicitCountry = Query.GetValueOrDefault("country")
            ?? Query.GetValueOrDefault("countryName")
            ?? Query.GetValueOrDefault("location");
        var explicitCode = Query.GetValueOrDefault("countryCode")
            ?? Query.GetValueOrDefault("cc");

        var source = string.Join(' ', new[] { explicitCountry, explicitCode, Name }.Where(value => !string.IsNullOrWhiteSpace(value)));
        var normalized = source.ToLowerInvariant();

        if (Server.Equals("45.151.69.119", StringComparison.OrdinalIgnoreCase)
            || normalized.Contains("netherlands")
            || normalized.Contains("нидерланд")
            || normalized.Contains(" holland")
            || normalized.Contains(" nl"))
        {
            return ("NL", "Нидерланды", "🇳🇱");
        }

        if (normalized.Contains("germany") || normalized.Contains("германи") || normalized.Contains(" de"))
        {
            return ("DE", "Германия", "🇩🇪");
        }

        if (normalized.Contains("finland") || normalized.Contains("финлянд") || normalized.Contains(" fi"))
        {
            return ("FI", "Финляндия", "🇫🇮");
        }

        if (normalized.Contains("sweden") || normalized.Contains("швец") || normalized.Contains(" se"))
        {
            return ("SE", "Швеция", "🇸🇪");
        }

        if (normalized.Contains("france") || normalized.Contains("франц") || normalized.Contains(" fr"))
        {
            return ("FR", "Франция", "🇫🇷");
        }

        if (normalized.Contains("united kingdom") || normalized.Contains("great britain") || normalized.Contains("великобритан") || normalized.Contains(" uk") || normalized.Contains(" gb"))
        {
            return ("GB", "Великобритания", "🇬🇧");
        }

        if (normalized.Contains("united states") || normalized.Contains("usa") || normalized.Contains("сша") || normalized.Contains(" us"))
        {
            return ("US", "США", "🇺🇸");
        }

        return ("", "Страна не указана", "🌐");
    }
    public string Security => Query.GetValueOrDefault("security", "");
    public string Network => Query.GetValueOrDefault("type", "tcp");

    public int ConnectionPriority => Security.Equals("reality", StringComparison.OrdinalIgnoreCase)
        ? 0
        : Network.Equals("ws", StringComparison.OrdinalIgnoreCase)
            ? 1
            : Network.Equals("tcp", StringComparison.OrdinalIgnoreCase)
                ? 2
                : 3;
}
