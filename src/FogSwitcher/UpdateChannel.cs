namespace FogSwitcher;

internal static class UpdateChannel
{
    // Set this to "owner/repository" once the GitHub repo exists.
    public const string GitHubRepository = "LED-N/Fog-Switcher";
    public const string PreferredAssetName = "FogSwitcher.exe";

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
