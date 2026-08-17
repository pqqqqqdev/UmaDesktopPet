[CmdletBinding()]
param()

$ErrorActionPreference = "Stop"
$standaloneRoot = Split-Path -Parent $PSScriptRoot
$sqliteVersion = "2.3.6"
$sqliteArchiveName = "sqlite3mc-2.3.6-sqlite-3.53.3-win64.zip"
$sqliteUri = "https://github.com/utelle/SQLite3MultipleCiphers/releases/download/v2.3.6/$sqliteArchiveName"
$expectedSha256 = "789ee3d846a21d01045fb027286735447b17cccf3fb07979fbef354a815defd7"
$temporaryRoot = Join-Path ([System.IO.Path]::GetTempPath()) ("UmaDesktopPet-deps-" + [Guid]::NewGuid().ToString("N"))
$archivePath = Join-Path $temporaryRoot $sqliteArchiveName
$extractPath = Join-Path $temporaryRoot "sqlite3mc"
$pluginDirectory = Join-Path $standaloneRoot "Assets/Plugins/x86_64"
$licenseDirectory = Join-Path $standaloneRoot "ThirdParty/SQLite3MultipleCiphers"

try {
    New-Item $temporaryRoot -ItemType Directory -Force | Out-Null
    Invoke-WebRequest $sqliteUri -OutFile $archivePath
    $actualSha256 = (Get-FileHash $archivePath -Algorithm SHA256).Hash.ToLowerInvariant()
    if ($actualSha256 -ne $expectedSha256) {
        throw "SQLite3MC checksum mismatch. Expected $expectedSha256, got $actualSha256."
    }

    Expand-Archive $archivePath -DestinationPath $extractPath -Force
    $nativeLibrary = Get-ChildItem $extractPath -Filter "sqlite3mc_x64.dll" -File -Recurse | Select-Object -First 1
    $license = Get-ChildItem $extractPath -Filter "LICENSE" -File -Recurse | Select-Object -First 1
    $spdx = Get-ChildItem $extractPath -Filter "LICENSE.spdx" -File -Recurse | Select-Object -First 1
    if (-not $nativeLibrary -or -not $license) {
        throw "The verified SQLite3MC archive did not contain the expected DLL and license."
    }

    New-Item $pluginDirectory -ItemType Directory -Force | Out-Null
    New-Item $licenseDirectory -ItemType Directory -Force | Out-Null
    Copy-Item $nativeLibrary.FullName (Join-Path $pluginDirectory "sqlite3mc_x64.dll") -Force
    Copy-Item $license.FullName (Join-Path $licenseDirectory "LICENSE") -Force
    if ($spdx) {
        Copy-Item $spdx.FullName (Join-Path $licenseDirectory "LICENSE.spdx") -Force
    }

    @(
        "Version=$sqliteVersion"
        "Archive=$sqliteArchiveName"
        "ArchiveSha256=$actualSha256"
        "Source=$sqliteUri"
    ) | Set-Content (Join-Path $licenseDirectory "SOURCE.txt") -Encoding ascii

    Write-Host "Installed verified SQLite3MC $sqliteVersion into $pluginDirectory"
} finally {
    $resolvedTemp = [System.IO.Path]::GetFullPath($temporaryRoot)
    $tempRoot = [System.IO.Path]::GetFullPath([System.IO.Path]::GetTempPath())
    if ((Test-Path $resolvedTemp) -and
        $resolvedTemp.StartsWith($tempRoot, [System.StringComparison]::OrdinalIgnoreCase)) {
        Remove-Item -LiteralPath $resolvedTemp -Recurse -Force
    }
}
