param(
    [string] $ProjectPath = "src\FogSwitcher\FogSwitcher.csproj",
    [string] $RuntimeIdentifier = "win-x64",
    [string] $OutputRoot = "dist\release-assets"
)

Set-StrictMode -Version Latest
$ErrorActionPreference = "Stop"

$projectRoot = Split-Path -Parent $PSScriptRoot
$resolvedProjectPath = Join-Path $projectRoot $ProjectPath
$resolvedOutputRoot = Join-Path $projectRoot $OutputRoot

if (!(Test-Path -LiteralPath $resolvedProjectPath)) {
    throw "Project file not found: $resolvedProjectPath"
}

$projectName = [System.IO.Path]::GetFileNameWithoutExtension($resolvedProjectPath)
$frameworkDependentOutput = Join-Path $resolvedOutputRoot "framework-dependent"
$selfContainedOutput = Join-Path $resolvedOutputRoot "self-contained"

if (Test-Path -LiteralPath $frameworkDependentOutput) {
    Remove-Item -LiteralPath $frameworkDependentOutput -Recurse -Force
}

if (Test-Path -LiteralPath $selfContainedOutput) {
    Remove-Item -LiteralPath $selfContainedOutput -Recurse -Force
}

New-Item -ItemType Directory -Force -Path $frameworkDependentOutput | Out-Null
New-Item -ItemType Directory -Force -Path $selfContainedOutput | Out-Null

$frameworkDependentPublishArgs = @(
    "publish",
    $resolvedProjectPath,
    "-c", "Release",
    "-r", $RuntimeIdentifier,
    "-p:DebugType=None",
    "-p:DebugSymbols=false",
    "-p:NuGetAudit=false"
)

Write-Output "Publishing framework-dependent build..."
dotnet @frameworkDependentPublishArgs --self-contained false -o $frameworkDependentOutput

Write-Output "Publishing self-contained single-file build..."
$selfContainedPublishArgs = @(
    "publish",
    $resolvedProjectPath,
    "-c", "Release",
    "-r", $RuntimeIdentifier,
    "--self-contained", "true",
    "-p:PublishSingleFile=true",
    "-p:DebugType=None",
    "-p:DebugSymbols=false",
    "-p:NuGetAudit=false",
    "-o", $selfContainedOutput
)
dotnet @selfContainedPublishArgs

if (!(Test-Path -LiteralPath (Join-Path $frameworkDependentOutput "$projectName.exe"))) {
    throw "Framework-dependent executable not found: $(Join-Path $frameworkDependentOutput "$projectName.exe")"
}

$frameworkDependentAsset = Join-Path $resolvedOutputRoot "$projectName-$RuntimeIdentifier-framework-dependent.zip"
$selfContainedAsset = Join-Path $resolvedOutputRoot "$projectName-$RuntimeIdentifier-self-contained.exe"

if (Test-Path -LiteralPath $frameworkDependentAsset) {
    Remove-Item -LiteralPath $frameworkDependentAsset -Force
}

if (Test-Path -LiteralPath $selfContainedAsset) {
    Remove-Item -LiteralPath $selfContainedAsset -Force
}

Compress-Archive -Path (Join-Path $frameworkDependentOutput "*") -DestinationPath $frameworkDependentAsset -CompressionLevel Optimal

$selfContainedSource = Join-Path $selfContainedOutput "$projectName.exe"

if (!(Test-Path -LiteralPath $selfContainedSource)) {
    throw "Self-contained executable not found: $selfContainedSource"
}

Copy-Item -LiteralPath $selfContainedSource -Destination $selfContainedAsset -Force

Write-Output ""
Write-Output "Release assets ready:"
Write-Output " - $frameworkDependentAsset"
Write-Output " - $selfContainedAsset"
