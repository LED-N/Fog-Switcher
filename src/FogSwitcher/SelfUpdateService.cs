using System.Diagnostics;
using System.Net.Http.Headers;
using System.Text;

namespace FogSwitcher;

internal sealed class PreparedSelfUpdate
{
    public required string VersionText { get; init; }
    public required string DownloadedFilePath { get; init; }
    public required string ScriptPath { get; init; }
}

internal sealed class SelfUpdateService : IDisposable
{
    private readonly HttpClient _httpClient;
    private readonly bool _ownsClient;

    public SelfUpdateService(HttpClient? httpClient = null)
    {
        _ownsClient = httpClient is null;
        _httpClient = httpClient ?? new HttpClient
        {
            Timeout = TimeSpan.FromMinutes(10)
        };

        if (_httpClient.DefaultRequestHeaders.UserAgent.Count == 0)
        {
            _httpClient.DefaultRequestHeaders.UserAgent.Add(
                new ProductInfoHeaderValue("FogSwitcher", GitHubReleaseUpdateService.CurrentVersionText));
        }
    }

    public async Task<PreparedSelfUpdate> PrepareUpdateAsync(
        AvailableUpdate update,
        CancellationToken cancellationToken = default)
    {
        if (!update.CanInstallAutomatically || string.IsNullOrWhiteSpace(update.AutomaticInstallUrl))
        {
            throw new InvalidOperationException("This release does not provide a supported automatic update package.");
        }

        var currentExecutablePath = Application.ExecutablePath;
        if (string.IsNullOrWhiteSpace(currentExecutablePath) ||
            !string.Equals(Path.GetExtension(currentExecutablePath), ".exe", StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidOperationException("Fog Switcher could not determine the current executable path.");
        }

        var updateDirectory = Path.Combine(
            Path.GetTempPath(),
            "FogSwitcher",
            "updates",
            $"{SanitizePathSegment(update.VersionText)}-{DateTime.UtcNow:yyyyMMddHHmmss}");
        Directory.CreateDirectory(updateDirectory);

        var downloadedFilePath = Path.Combine(
            updateDirectory,
            string.IsNullOrWhiteSpace(update.AutomaticInstallAssetName)
                ? "FogSwitcher-update.exe"
                : update.AutomaticInstallAssetName);

        using var response = await _httpClient
            .GetAsync(update.AutomaticInstallUrl, HttpCompletionOption.ResponseHeadersRead, cancellationToken)
            .ConfigureAwait(false);

        response.EnsureSuccessStatusCode();

        await using (var responseStream = await response.Content.ReadAsStreamAsync(cancellationToken).ConfigureAwait(false))
        await using (var fileStream = new FileStream(downloadedFilePath, FileMode.Create, FileAccess.Write, FileShare.None, 81920, useAsync: true))
        {
            await responseStream.CopyToAsync(fileStream, cancellationToken).ConfigureAwait(false);
        }

        var scriptPath = Path.Combine(updateDirectory, "apply-update.ps1");
        File.WriteAllText(
            scriptPath,
            BuildInstallerScript(),
            new UTF8Encoding(encoderShouldEmitUTF8Identifier: false));

        return new PreparedSelfUpdate
        {
            VersionText = update.VersionText,
            DownloadedFilePath = downloadedFilePath,
            ScriptPath = scriptPath
        };
    }

    public void LaunchInstaller(PreparedSelfUpdate preparedUpdate, string releasePageUrl)
    {
        var startInfo = new ProcessStartInfo
        {
            FileName = "powershell.exe",
            UseShellExecute = false,
            CreateNoWindow = true,
            WindowStyle = ProcessWindowStyle.Hidden
        };

        startInfo.ArgumentList.Add("-ExecutionPolicy");
        startInfo.ArgumentList.Add("Bypass");
        startInfo.ArgumentList.Add("-File");
        startInfo.ArgumentList.Add(preparedUpdate.ScriptPath);
        startInfo.ArgumentList.Add("-ProcessId");
        startInfo.ArgumentList.Add(Environment.ProcessId.ToString());
        startInfo.ArgumentList.Add("-SourcePath");
        startInfo.ArgumentList.Add(preparedUpdate.DownloadedFilePath);
        startInfo.ArgumentList.Add("-TargetPath");
        startInfo.ArgumentList.Add(Application.ExecutablePath);
        startInfo.ArgumentList.Add("-VersionText");
        startInfo.ArgumentList.Add(preparedUpdate.VersionText);
        startInfo.ArgumentList.Add("-ReleasePageUrl");
        startInfo.ArgumentList.Add(releasePageUrl);

        using var process = Process.Start(startInfo);
        if (process is null)
        {
            throw new InvalidOperationException("Fog Switcher could not start the update installer helper.");
        }
    }

    private static string SanitizePathSegment(string value)
    {
        var invalidChars = Path.GetInvalidFileNameChars();
        var builder = new StringBuilder(value.Length);

        foreach (var ch in value)
        {
            builder.Append(invalidChars.Contains(ch) ? '_' : ch);
        }

        return builder.ToString();
    }

    private static string BuildInstallerScript()
    {
        return """
param(
    [int] $ProcessId,
    [string] $SourcePath,
    [string] $TargetPath,
    [string] $VersionText,
    [string] $ReleasePageUrl
)

$ErrorActionPreference = "Stop"
Add-Type -AssemblyName System.Windows.Forms

for ($attempt = 0; $attempt -lt 120; $attempt++) {
    try {
        Get-Process -Id $ProcessId -ErrorAction Stop | Out-Null
        Start-Sleep -Milliseconds 500
    }
    catch {
        break
    }
}

$copied = $false
for ($attempt = 0; $attempt -lt 60 -and -not $copied; $attempt++) {
    try {
        Copy-Item -LiteralPath $SourcePath -Destination $TargetPath -Force
        $copied = $true
    }
    catch {
        Start-Sleep -Milliseconds 500
    }
}

if (-not $copied) {
    [System.Windows.Forms.MessageBox]::Show(
        "Fog Switcher could not replace the executable automatically.`n`nPlease download the update manually from the release page.",
        "Update failed",
        [System.Windows.Forms.MessageBoxButtons]::OK,
        [System.Windows.Forms.MessageBoxIcon]::Error
    ) | Out-Null

    if (![string]::IsNullOrWhiteSpace($ReleasePageUrl)) {
        Start-Process -FilePath $ReleasePageUrl | Out-Null
    }

    exit 1
}

try {
    Start-Process -FilePath $TargetPath | Out-Null
}
catch {
    [System.Windows.Forms.MessageBox]::Show(
        "Fog Switcher updated to version $VersionText, but the app could not be restarted automatically.`n`nPlease launch it manually from:`n$TargetPath",
        "Restart required",
        [System.Windows.Forms.MessageBoxButtons]::OK,
        [System.Windows.Forms.MessageBoxIcon]::Information
    ) | Out-Null
}

try {
    Remove-Item -LiteralPath $SourcePath -Force -ErrorAction SilentlyContinue
}
catch {
}
""";
    }

    public void Dispose()
    {
        if (_ownsClient)
        {
            _httpClient.Dispose();
        }
    }
}
