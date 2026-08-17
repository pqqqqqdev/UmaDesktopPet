[CmdletBinding()]
param(
    [string]$OutputDirectory
)

$ErrorActionPreference = "Stop"
$standaloneRoot = Split-Path -Parent $PSScriptRoot
$repoRoot = $standaloneRoot
if ([string]::IsNullOrWhiteSpace($OutputDirectory)) {
    $OutputDirectory = Join-Path $repoRoot "artifacts/standalone/catalog-probe"
}
$OutputDirectory = [System.IO.Path]::GetFullPath($OutputDirectory)
$compiler = Join-Path $env:WINDIR "Microsoft.NET/Framework64/v4.0.30319/csc.exe"
$output = Join-Path $OutputDirectory "UmaDesktopPet.CatalogProbe.exe"
$sources = @(
    Get-ChildItem (Join-Path $standaloneRoot "Assets/Scripts/Core") -Filter "*.cs" -File -Recurse
    Get-Item (Join-Path $standaloneRoot "Tools/CatalogProbe/Program.cs")
) | Sort-Object FullName

if (-not (Test-Path $compiler -PathType Leaf)) {
    throw ".NET Framework compiler not found: $compiler"
}
New-Item $OutputDirectory -ItemType Directory -Force | Out-Null

$arguments = @(
    "/nologo",
    "/target:exe",
    "/platform:x64",
    "/optimize+",
    "/out:$output",
    "/reference:System.Core.dll"
)
$arguments += $sources.FullName
& $compiler @arguments
if ($LASTEXITCODE -ne 0) {
    throw "Catalog probe compilation failed with exit code $LASTEXITCODE."
}
Write-Host "Built $output"
