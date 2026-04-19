using System.Net;
using System.Net.Http.Headers;
using System.Text.Json;

namespace FogSwitcher;

internal sealed class AvailableUpdate
{
    public required Version Version { get; init; }
    public required string VersionText { get; init; }
    public required string ReleasePageUrl { get; init; }
    public string? AutomaticInstallUrl { get; init; }
    public string? AutomaticInstallAssetName { get; init; }
    public bool CanInstallAutomatically => !string.IsNullOrWhiteSpace(AutomaticInstallUrl);
}

internal sealed class GitHubReleaseUpdateService : IDisposable
{
    private readonly HttpClient _httpClient;
    private readonly bool _ownsClient;

    public GitHubReleaseUpdateService(HttpClient? httpClient = null)
    {
        _ownsClient = httpClient is null;
        _httpClient = httpClient ?? new HttpClient
        {
            BaseAddress = new Uri("https://api.github.com/"),
            Timeout = TimeSpan.FromSeconds(10)
        };

        if (_httpClient.DefaultRequestHeaders.UserAgent.Count == 0)
        {
            _httpClient.DefaultRequestHeaders.UserAgent.Add(
                new ProductInfoHeaderValue("FogSwitcher", CurrentVersionText));
        }

        if (_httpClient.DefaultRequestHeaders.Accept.Count == 0)
        {
            _httpClient.DefaultRequestHeaders.Accept.Add(
                new MediaTypeWithQualityHeaderValue("application/vnd.github+json"));
        }
    }

    public static Version CurrentVersion => NormalizeVersion(typeof(GitHubReleaseUpdateService).Assembly.GetName().Version);

    public static string CurrentVersionText => FormatVersion(CurrentVersion);

    public async Task<AvailableUpdate?> GetAvailableUpdateAsync(CancellationToken cancellationToken = default)
    {
        var repository = UpdateChannel.TryGetRepository();
        if (repository is null)
        {
            return null;
        }

        using var response = await _httpClient
            .GetAsync($"repos/{repository.Value.Owner}/{repository.Value.Name}/releases/latest", cancellationToken)
            .ConfigureAwait(false);

        if (response.StatusCode is HttpStatusCode.Forbidden or HttpStatusCode.NotFound)
        {
            return null;
        }

        response.EnsureSuccessStatusCode();

        await using var stream = await response.Content.ReadAsStreamAsync(cancellationToken).ConfigureAwait(false);
        using var document = await JsonDocument.ParseAsync(stream, cancellationToken: cancellationToken).ConfigureAwait(false);
        var root = document.RootElement;

        if (IsTrue(root, "draft") || IsTrue(root, "prerelease"))
        {
            return null;
        }

        if (!root.TryGetProperty("tag_name", out var tagNameElement) ||
            !TryParseReleaseVersion(tagNameElement.GetString(), out var latestVersion, out var latestVersionText))
        {
            return null;
        }

        if (latestVersion.CompareTo(CurrentVersion) <= 0)
        {
            return null;
        }

        var releasePageUrl = root.TryGetProperty("html_url", out var htmlUrlElement)
            ? htmlUrlElement.GetString()
            : null;
        if (string.IsNullOrWhiteSpace(releasePageUrl))
        {
            return null;
        }

        var automaticInstall = FindAssetByName(root, UpdateChannel.PreferredAutomaticUpdateAssetName);

        return new AvailableUpdate
        {
            Version = latestVersion,
            VersionText = latestVersionText,
            ReleasePageUrl = releasePageUrl,
            AutomaticInstallUrl = automaticInstall?.Url,
            AutomaticInstallAssetName = automaticInstall?.Name
        };
    }

    private static bool IsTrue(JsonElement root, string propertyName)
    {
        return root.TryGetProperty(propertyName, out var property) && property.ValueKind == JsonValueKind.True;
    }

    private static ReleaseAsset? FindAssetByName(JsonElement root, string expectedAssetName)
    {
        if (!root.TryGetProperty("assets", out var assetsElement) || assetsElement.ValueKind != JsonValueKind.Array)
        {
            return null;
        }

        foreach (var asset in assetsElement.EnumerateArray())
        {
            if (!asset.TryGetProperty("name", out var nameElement) ||
                !asset.TryGetProperty("browser_download_url", out var urlElement))
            {
                continue;
            }

            var name = nameElement.GetString();
            var url = urlElement.GetString();
            if (string.IsNullOrWhiteSpace(name) || string.IsNullOrWhiteSpace(url))
            {
                continue;
            }

            if (!string.Equals(name, expectedAssetName, StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            return new ReleaseAsset
            {
                Name = name,
                Url = url
            };
        }

        return null;
    }

    private sealed class ReleaseAsset
    {
        public required string Name { get; init; }
        public required string Url { get; init; }
    }

    private static bool TryParseReleaseVersion(string? rawTag, out Version version, out string versionText)
    {
        version = new Version(0, 0, 0);
        versionText = string.Empty;

        if (string.IsNullOrWhiteSpace(rawTag))
        {
            return false;
        }

        versionText = rawTag.Trim();
        var normalized = versionText.TrimStart('v', 'V');
        var prereleaseSeparator = normalized.IndexOf('-');
        if (prereleaseSeparator >= 0)
        {
            normalized = normalized[..prereleaseSeparator];
        }

        if (!Version.TryParse(normalized, out var parsedVersion))
        {
            return false;
        }

        version = NormalizeVersion(parsedVersion);
        versionText = FormatVersion(version);
        return true;
    }

    private static Version NormalizeVersion(Version? version)
    {
        if (version is null)
        {
            return new Version(1, 0, 0);
        }

        return new Version(
            Math.Max(version.Major, 0),
            Math.Max(version.Minor, 0),
            version.Build >= 0 ? version.Build : 0);
    }

    private static string FormatVersion(Version version)
    {
        return $"{version.Major}.{version.Minor}.{version.Build}";
    }

    public void Dispose()
    {
        if (_ownsClient)
        {
            _httpClient.Dispose();
        }
    }
}
