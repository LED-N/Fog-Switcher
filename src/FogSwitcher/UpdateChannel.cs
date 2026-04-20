namespace FogSwitcher;

internal static class UpdateChannel
{
    public const string GitHubRepository = "LED-N/Fog-Switcher";
    public const string PreferredAutomaticUpdateAssetName = "FogSwitcher.exe";
    public const string LegacyAutomaticUpdateAssetName = "FogSwitcher-win-x64-self-contained.exe";

    public static (string Owner, string Name)? TryGetRepository()
    {
        var parts = GitHubRepository.Split('/', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        if (parts.Length != 2)
        {
            return null;
        }

        return (parts[0], parts[1]);
    }
}
