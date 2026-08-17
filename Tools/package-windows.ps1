[CmdletBinding()]
param(
    [string]$Version = "0.1.0",
    [string]$BuildDirectory,
    [string]$OutputDirectory
)

$ErrorActionPreference = "Stop"
$projectRoot = Split-Path -Parent $PSScriptRoot
$repositoryRoot = $projectRoot

if ([string]::IsNullOrWhiteSpace($BuildDirectory)) {
    $BuildDirectory = Join-Path $repositoryRoot "artifacts\standalone\windows"
}
if ([string]::IsNullOrWhiteSpace($OutputDirectory)) {
    $OutputDirectory = Join-Path $repositoryRoot "artifacts\standalone\packages"
}
if ($Version -notmatch '^\d+\.\d+\.\d+(?:[-+][0-9A-Za-z.-]+)?$') {
    throw "Version must be a SemVer-like value such as 0.1.0 or 0.1.0-preview.1."
}

$resolvedBuild = [System.IO.Path]::GetFullPath($BuildDirectory)
$resolvedOutput = [System.IO.Path]::GetFullPath($OutputDirectory)
$packageName = "UmaDesktopPet-v$Version-windows-x64"
$packageRootName = "UmaDesktopPet"
$packageDirectory = [System.IO.Path]::GetFullPath(
    (Join-Path $resolvedOutput $packageRootName))
$zipPath = [System.IO.Path]::GetFullPath(
    (Join-Path $resolvedOutput "$packageName.zip"))
$hashPath = "$zipPath.sha256"

if (-not (Test-Path -LiteralPath $resolvedBuild -PathType Container)) {
    throw "Build directory does not exist: $resolvedBuild"
}

$requiredBuildEntries = @(
    "UmaDesktopPet.exe",
    "UmaDesktopPet_Data",
    "MonoBleedingEdge",
    "UnityPlayer.dll"
)
foreach ($entry in $requiredBuildEntries) {
    if (-not (Test-Path -LiteralPath (Join-Path $resolvedBuild $entry))) {
        throw "The Unity player is incomplete. Missing: $entry"
    }
}

New-Item -ItemType Directory -Path $resolvedOutput -Force | Out-Null
$outputPrefix = $resolvedOutput.TrimEnd(
    [System.IO.Path]::DirectorySeparatorChar,
    [System.IO.Path]::AltDirectorySeparatorChar) +
    [System.IO.Path]::DirectorySeparatorChar
$buildPrefix = $resolvedBuild.TrimEnd(
    [System.IO.Path]::DirectorySeparatorChar,
    [System.IO.Path]::AltDirectorySeparatorChar) +
    [System.IO.Path]::DirectorySeparatorChar
if (-not $packageDirectory.StartsWith(
        $outputPrefix,
        [System.StringComparison]::OrdinalIgnoreCase)) {
    throw "Refusing to clean a package path outside the output directory: $packageDirectory"
}
if ($packageDirectory.StartsWith(
        $buildPrefix,
        [System.StringComparison]::OrdinalIgnoreCase)) {
    throw "Output directory cannot be inside the Unity build directory."
}

if (Test-Path -LiteralPath $packageDirectory) {
    Remove-Item -LiteralPath $packageDirectory -Recurse -Force
}
if (Test-Path -LiteralPath $zipPath) {
    Remove-Item -LiteralPath $zipPath -Force
}
if (Test-Path -LiteralPath $hashPath) {
    Remove-Item -LiteralPath $hashPath -Force
}
New-Item -ItemType Directory -Path $packageDirectory -Force | Out-Null

$sourcePrefix = $buildPrefix
$buildFiles = Get-ChildItem -LiteralPath $resolvedBuild -Recurse -File
foreach ($file in $buildFiles) {
    $relativePath = $file.FullName.Substring($sourcePrefix.Length)
    if ($file.Extension -ieq ".pdb") {
        continue
    }
    if ($relativePath -ieq "SMOKE_TEST.md") {
        continue
    }
    if ($relativePath -match '(^|[\\/])[^\\/]*_BurstDebugInformation_DoNotShip([\\/]|$)') {
        continue
    }

    $destination = Join-Path $packageDirectory $relativePath
    $destinationDirectory = Split-Path -Parent $destination
    New-Item -ItemType Directory -Path $destinationDirectory -Force | Out-Null
    Copy-Item -LiteralPath $file.FullName -Destination $destination -Force
}

$supportFiles = @(
    @{ Source = "README.md"; Destination = "README.md" },
    @{ Source = "LICENSE"; Destination = "LICENSE.txt" },
    @{ Source = "docs\TESTING.md"; Destination = "TESTING.md" },
    @{ Source = "THIRD_PARTY_NOTICES.txt"; Destination = "THIRD_PARTY_NOTICES.txt" },
    @{
        Source = "ThirdParty\SQLite3MultipleCiphers\LICENSE"
        Destination = "Licenses\SQLite3MultipleCiphers-MIT.txt"
    },
    @{
        Source = "ThirdParty\UniWindowController\LICENSE.md"
        Destination = "Licenses\UniWindowController-MIT.txt"
    }
)
foreach ($supportFile in $supportFiles) {
    $source = Join-Path $projectRoot $supportFile.Source
    if (-not (Test-Path -LiteralPath $source -PathType Leaf)) {
        throw "Required release support file is missing: $source"
    }

    $destination = Join-Path $packageDirectory $supportFile.Destination
    New-Item -ItemType Directory -Path (Split-Path -Parent $destination) -Force |
        Out-Null
    Copy-Item -LiteralPath $source -Destination $destination -Force
}

$unexpectedFiles = Get-ChildItem -LiteralPath $packageDirectory -Recurse -File |
    Where-Object {
        $_.Extension -ieq ".pdb" -or
        $_.FullName -match '(^|[\\/])[^\\/]*_BurstDebugInformation_DoNotShip([\\/]|$)'
    }
if ($unexpectedFiles) {
    throw "Debug-only files entered the package: $($unexpectedFiles.FullName -join ', ')"
}

Compress-Archive -LiteralPath $packageDirectory -DestinationPath $zipPath `
    -CompressionLevel Optimal
$hash = Get-FileHash -LiteralPath $zipPath -Algorithm SHA256
$hashLine = "$($hash.Hash.ToLowerInvariant())  $([System.IO.Path]::GetFileName($zipPath))"
[System.IO.File]::WriteAllText(
    $hashPath,
    $hashLine + [Environment]::NewLine,
    [System.Text.UTF8Encoding]::new($false))

Write-Host "Packaged: $packageDirectory"
Write-Host "Archive:  $zipPath"
Write-Host "SHA256:   $hashPath"
