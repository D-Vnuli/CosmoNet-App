namespace CosmoNet.App.Services;

public sealed class MihomoConfigFileStore
{
    private readonly string _configPath;

    public MihomoConfigFileStore(string? configPath = null)
    {
        _configPath = Path.GetFullPath(configPath ?? AppPaths.MihomoConfigPath);
    }

    public bool TryDelete()
    {
        try
        {
            File.Delete(_configPath);
            return true;
        }
        catch (IOException)
        {
            return false;
        }
        catch (UnauthorizedAccessException)
        {
            return false;
        }
    }
}
