Set-StrictMode -Version Latest
$ErrorActionPreference = "Stop"

$projectRoot = Split-Path -Parent $PSScriptRoot
$failures = New-Object System.Collections.Generic.List[string]

if (Get-Process -Name devenv -ErrorAction SilentlyContinue) {
    Write-Warning "Visual Studio appears to be running. Close it for a fully clean workspace."
}

$pathsToRemove = @(
    ".claude",
    ".dotnet",
    ".vs",
    "dist",
    "src\FogSwitcher\bin",
    "src\FogSwitcher\obj",
    "src\FogSwitcher\app.ico"
)

foreach ($relativePath in $pathsToRemove) {
    $fullPath = Join-Path $projectRoot $relativePath
    if (Test-Path -LiteralPath $fullPath) {
        try {
            Remove-Item -LiteralPath $fullPath -Recurse -Force
            Write-Output "Removed $relativePath"
        }
        catch {
            $failures.Add($relativePath)
            Write-Warning "Could not fully remove $relativePath : $($_.Exception.Message)"
        }
    }
}

$patterns = @("*.user", "*.pubxml.user", "*.suo", "*.log")
foreach ($pattern in $patterns) {
    Get-ChildItem -Path $projectRoot -Recurse -Force -File -Filter $pattern | ForEach-Object {
        try {
            Remove-Item -LiteralPath $_.FullName -Force
        }
        catch {
            $failures.Add($_.FullName)
            Write-Warning "Could not remove $($_.FullName) : $($_.Exception.Message)"
        }
    }
}

Write-Output "Workspace cleanup complete."
if ($failures.Count -gt 0) {
    Write-Warning "Some paths could not be removed because they are in use:"
    $failures | Sort-Object -Unique | ForEach-Object { Write-Warning " - $_" }
}
