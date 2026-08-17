[CmdletBinding()]
param(
    [string]$UnityPath = "D:\Unity\2022.3.62f2\Editor\Unity.exe",
    [string]$OutputDirectory
)

$ErrorActionPreference = "Stop"
$projectRoot = Split-Path -Parent $PSScriptRoot
$repositoryRoot = $projectRoot
if ([string]::IsNullOrWhiteSpace($OutputDirectory)) {
    $OutputDirectory = Join-Path $repositoryRoot "artifacts\standalone\windows"
}

$expectedVersion = "2022.3.62f2"
if (-not (Test-Path -LiteralPath $UnityPath -PathType Leaf)) {
    throw "Unity $expectedVersion was not found at $UnityPath. Install that exact editor version first."
}

$actualVersion = (Get-Item -LiteralPath $UnityPath).VersionInfo.ProductVersion
if (-not $actualVersion.StartsWith($expectedVersion, [System.StringComparison]::OrdinalIgnoreCase)) {
    throw "Wrong Unity editor. Expected $expectedVersion, found $actualVersion at $UnityPath."
}

$resolvedProject = [System.IO.Path]::GetFullPath($projectRoot)
$resolvedOutput = [System.IO.Path]::GetFullPath($OutputDirectory)
$outputExecutable = Join-Path $resolvedOutput "UmaDesktopPet.exe"
$logDirectory = Join-Path $repositoryRoot "artifacts\standalone\logs"
$logPath = Join-Path $logDirectory "unity-build.log"
New-Item -ItemType Directory -Path $resolvedOutput -Force | Out-Null
New-Item -ItemType Directory -Path $logDirectory -Force | Out-Null

$unityArguments = @(
    "-batchmode"
    "-nographics"
    "-quit"
    "-projectPath", ('"' + $resolvedProject + '"')
    "-buildTarget", "Win64"
    "-executeMethod", "UmaDesktopPet.Standalone.Editor.WindowsBuild.Build"
    "-umaOutputPath", ('"' + $outputExecutable + '"')
    "-logFile", ('"' + $logPath + '"')
)
$unityProcess = Start-Process `
    -FilePath $UnityPath `
    -ArgumentList $unityArguments `
    -WindowStyle Hidden `
    -PassThru
$unityProcess.WaitForExit()

if ($unityProcess.ExitCode -ne 0) {
    throw "Unity build failed with exit code $($unityProcess.ExitCode). See $logPath"
}
if (-not (Test-Path -LiteralPath $outputExecutable -PathType Leaf)) {
    throw "Unity reported success but did not create $outputExecutable. See $logPath"
}

Write-Host "Built Uma Desktop Pet: $outputExecutable"
Write-Host "Unity log: $logPath"
