[CmdletBinding()]
param(
    [Parameter(Mandatory = $true)]
    [ValidatePattern('^\d+\.\d+\.\d+(?:[-+][0-9A-Za-z.-]+)?$')]
    [string]$Version,

    [Parameter(Mandatory = $true)]
    [Alias("StageDirectory")]
    [string]$PackageDirectory,

    [Parameter(Mandatory = $true)]
    [string]$ZipPath,

    [Parameter(Mandatory = $true)]
    [string]$SidecarPath
)

Set-StrictMode -Version Latest
$ErrorActionPreference = "Stop"

$packageRootName = "UmaDesktopPet"
$expectedUnityVersion = "2022.3.62f2"
$expectedUnityRevision = "7670c08855a9"
$expectedUnityProductVersion =
    "$expectedUnityVersion ($expectedUnityRevision)"
$expectedZipName =
    "UmaDesktopPet-v$Version-windows-x64.zip"
$expectedSidecarName = "$expectedZipName.sha256"
$repositoryRoot = Split-Path -Parent $PSScriptRoot

function Get-FullExistingPath {
    param(
        [Parameter(Mandatory = $true)]
        [string]$LiteralPath,

        [Parameter(Mandatory = $true)]
        [ValidateSet("Container", "Leaf")]
        [string]$PathType
    )

    if (-not (Test-Path -LiteralPath $LiteralPath -PathType $PathType)) {
        throw "Required $($PathType.ToLowerInvariant()) does not exist: $LiteralPath"
    }

    return [System.IO.Path]::GetFullPath(
        (Resolve-Path -LiteralPath $LiteralPath).ProviderPath)
}

function Test-PathIsWithin {
    param(
        [Parameter(Mandatory = $true)]
        [string]$Candidate,

        [Parameter(Mandatory = $true)]
        [string]$Parent
    )

    $prefix = $Parent.TrimEnd(
        [System.IO.Path]::DirectorySeparatorChar,
        [System.IO.Path]::AltDirectorySeparatorChar) +
        [System.IO.Path]::DirectorySeparatorChar
    return $Candidate.StartsWith(
        $prefix,
        [System.StringComparison]::OrdinalIgnoreCase)
}

function Get-Sha256Lower {
    param(
        [Parameter(Mandatory = $true)]
        [string]$LiteralPath
    )

    return (Get-FileHash -LiteralPath $LiteralPath -Algorithm SHA256).
        Hash.ToLowerInvariant()
}

function Get-StreamSha256Lower {
    param(
        [Parameter(Mandatory = $true)]
        [System.IO.Stream]$Stream
    )

    $algorithm = [System.Security.Cryptography.SHA256]::Create()
    try {
        $bytes = $algorithm.ComputeHash($Stream)
        return ([System.BitConverter]::ToString($bytes)).
            Replace("-", "").ToLowerInvariant()
    }
    finally {
        $algorithm.Dispose()
    }
}

function Assert-SafePackagePath {
    param(
        [Parameter(Mandatory = $true)]
        [string]$ArchivePath,

        [Parameter(Mandatory = $true)]
        [bool]$IsDirectory
    )

    if ([string]::IsNullOrWhiteSpace($ArchivePath)) {
        throw "The package contains an empty path."
    }
    if ($ArchivePath.IndexOf([char]0) -ge 0) {
        throw "The package path contains a NUL character."
    }
    if ($ArchivePath.Contains("\")) {
        throw "ZIP paths must use forward slashes only: $ArchivePath"
    }
    if ($ArchivePath.StartsWith("/", [System.StringComparison]::Ordinal) -or
        $ArchivePath.StartsWith("//", [System.StringComparison]::Ordinal) -or
        $ArchivePath -match '^[A-Za-z]:') {
        throw "Absolute, drive-qualified, and UNC package paths are forbidden: $ArchivePath"
    }

    $hasTrailingSlash = $ArchivePath.EndsWith(
        "/",
        [System.StringComparison]::Ordinal)
    if ($hasTrailingSlash -ne $IsDirectory) {
        throw "Directory marker and entry type disagree: $ArchivePath"
    }

    $pathWithoutSlash = if ($hasTrailingSlash) {
        $ArchivePath.Substring(0, $ArchivePath.Length - 1)
    }
    else {
        $ArchivePath
    }
    if ([string]::IsNullOrWhiteSpace($pathWithoutSlash)) {
        throw "The package contains an invalid root entry: $ArchivePath"
    }

    try {
        $normalized = $pathWithoutSlash.Normalize(
            [System.Text.NormalizationForm]::FormC)
    }
    catch {
        throw "The package path is not valid Unicode: $ArchivePath"
    }
    if ($normalized -cne $pathWithoutSlash) {
        throw "Package paths must use Unicode NFC normalization: $ArchivePath"
    }

    $parts = $pathWithoutSlash.Split('/')
    if ($parts.Count -eq 0 -or $parts[0] -cne $packageRootName) {
        throw "Every ZIP entry must be under the exact root '$packageRootName/': $ArchivePath"
    }
    if (-not $IsDirectory -and $parts.Count -lt 2) {
        throw "A file cannot replace the package root: $ArchivePath"
    }

    foreach ($part in $parts) {
        if ([string]::IsNullOrEmpty($part) -or
            $part -ceq "." -or
            $part -ceq "..") {
            throw "Empty, dot, and traversal components are forbidden: $ArchivePath"
        }
        if ($part -match '[\x00-\x1f<>:"\\|?*]') {
            throw "A package path contains a control, ADS, or invalid Windows character: $ArchivePath"
        }
        if ($part -match '[ .]$') {
            throw "Windows-normalized trailing dots and spaces are forbidden: $ArchivePath"
        }
        if ($part.Length -gt 255) {
            throw "A package path component exceeds 255 characters: $ArchivePath"
        }

        $deviceStem = $part.Split('.')[0]
        if ($deviceStem -match '^(?i:CON|PRN|AUX|NUL|COM[1-9]|LPT[1-9])$') {
            throw "A reserved Windows device name is forbidden: $ArchivePath"
        }
    }

    return $pathWithoutSlash
}

function Register-PathSpelling {
    param(
        [Parameter(Mandatory = $true)]
        [string]$PathWithoutSlash,

        [Parameter(Mandatory = $true)]
        [bool]$IsDirectory,

        [Parameter(Mandatory = $true)]
        [System.Collections.Generic.Dictionary[string,object]]$Spellings
    )

    $parts = $PathWithoutSlash.Split('/')
    for ($index = 0; $index -lt $parts.Count; $index++) {
        $prefix = [string]::Join('/', $parts[0..$index])
        $prefixIsDirectory = $index -lt ($parts.Count - 1) -or $IsDirectory
        $collisionKey = $prefix.Normalize(
            [System.Text.NormalizationForm]::FormC).ToUpperInvariant()

        if ($Spellings.ContainsKey($collisionKey)) {
            $existing = $Spellings[$collisionKey]
            if ($existing.Path -cne $prefix) {
                throw "Case or Unicode-normalization path collision: '$($existing.Path)' and '$prefix'"
            }
            if ([bool]$existing.IsDirectory -ne $prefixIsDirectory) {
                throw "File/directory path collision: $prefix"
            }
        }
        else {
            $Spellings.Add(
                $collisionKey,
                [pscustomobject]@{
                    Path = $prefix
                    IsDirectory = $prefixIsDirectory
                })
        }
    }
}

function Assert-ExpectedTopLevel {
    param(
        [Parameter(Mandatory = $true)]
        [string]$PathWithoutSlash,

        [Parameter(Mandatory = $true)]
        [bool]$IsDirectory
    )

    $parts = $PathWithoutSlash.Split('/')
    if ($parts.Count -eq 1) {
        if (-not $IsDirectory) {
            throw "The package root must be a directory: $PathWithoutSlash"
        }
        return
    }

    $allowedDirectories = @(
        "Licenses",
        "MonoBleedingEdge",
        "UmaDesktopPet_Data"
    )
    $allowedFiles = @(
        "LICENSE.txt",
        "README.md",
        "TESTING.md",
        "THIRD_PARTY_NOTICES.txt",
        "UmaDesktopPet.exe",
        "UnityCrashHandler64.exe",
        "UnityPlayer.dll",
        "WinPixEventRuntime.dll"
    )

    $topLevel = $parts[1]
    if ($parts.Count -gt 2 -or $IsDirectory) {
        if (-not ($allowedDirectories -ccontains $topLevel)) {
            throw "Unexpected top-level package directory: $topLevel"
        }
    }
    elseif (-not ($allowedFiles -ccontains $topLevel)) {
        throw "Unexpected top-level package file: $topLevel"
    }
}

function Get-SafeStageItems {
    param(
        [Parameter(Mandatory = $true)]
        [string]$Root
    )

    $rootAttributes = [System.IO.File]::GetAttributes($Root)
    if (($rootAttributes -band [System.IO.FileAttributes]::ReparsePoint) -ne 0) {
        throw "The staging root must not be a reparse point: $Root"
    }

    $results = [System.Collections.Generic.List[object]]::new()
    $directories = [System.Collections.Generic.Stack[string]]::new()
    $directories.Push($Root)

    while ($directories.Count -gt 0) {
        $current = $directories.Pop()
        foreach ($child in [System.IO.Directory]::EnumerateFileSystemEntries($current)) {
            $attributes = [System.IO.File]::GetAttributes($child)
            if (($attributes -band [System.IO.FileAttributes]::ReparsePoint) -ne 0) {
                throw "Reparse points and symlinks are forbidden in staging: $child"
            }
            if (($attributes -band [System.IO.FileAttributes]::Device) -ne 0) {
                throw "Special device entries are forbidden in staging: $child"
            }

            $isDirectory =
                ($attributes -band [System.IO.FileAttributes]::Directory) -ne 0
            $results.Add([pscustomobject]@{
                FullName = [System.IO.Path]::GetFullPath($child)
                IsDirectory = $isDirectory
            })
            if ($isDirectory) {
                $directories.Push($child)
            }
        }
    }

    return $results.ToArray()
}

function Test-BytesAt {
    param(
        [Parameter(Mandatory = $true)]
        [byte[]]$Bytes,

        [Parameter(Mandatory = $true)]
        [int]$Count,

        [Parameter(Mandatory = $true)]
        [int]$Offset,

        [Parameter(Mandatory = $true)]
        [byte[]]$Expected
    )

    if ($Offset -lt 0 -or $Count -lt ($Offset + $Expected.Length)) {
        return $false
    }
    for ($index = 0; $index -lt $Expected.Length; $index++) {
        if ($Bytes[$Offset + $index] -ne $Expected[$index]) {
            return $false
        }
    }
    return $true
}

function Test-AsciiAt {
    param(
        [Parameter(Mandatory = $true)]
        [byte[]]$Bytes,

        [Parameter(Mandatory = $true)]
        [int]$Count,

        [Parameter(Mandatory = $true)]
        [int]$Offset,

        [Parameter(Mandatory = $true)]
        [string]$Text
    )

    return Test-BytesAt `
        -Bytes $Bytes `
        -Count $Count `
        -Offset $Offset `
        -Expected ([System.Text.Encoding]::ASCII.GetBytes($Text))
}

function Get-ProhibitedMagic {
    param(
        [Parameter(Mandatory = $true)]
        [string]$LiteralPath
    )

    $buffer = New-Object byte[] 64
    $stream = [System.IO.File]::Open(
        $LiteralPath,
        [System.IO.FileMode]::Open,
        [System.IO.FileAccess]::Read,
        [System.IO.FileShare]::Read)
    try {
        $count = $stream.Read($buffer, 0, $buffer.Length)
    }
    finally {
        $stream.Dispose()
    }

    if (Test-AsciiAt $buffer $count 0 "SQLite format 3`0") {
        return "SQLite database"
    }
    foreach ($unityHeader in @("UnityFS", "UnityWeb", "UnityRaw")) {
        if (Test-AsciiAt $buffer $count 0 $unityHeader) {
            return "Unity asset bundle ($unityHeader)"
        }
    }
    if (Test-BytesAt $buffer $count 0 ([byte[]](0x89,0x50,0x4e,0x47,0x0d,0x0a,0x1a,0x0a))) {
        return "PNG image"
    }
    if (Test-BytesAt $buffer $count 0 ([byte[]](0xff,0xd8,0xff))) {
        return "JPEG image"
    }
    if ((Test-AsciiAt $buffer $count 0 "GIF87a") -or
        (Test-AsciiAt $buffer $count 0 "GIF89a")) {
        return "GIF image"
    }
    if (Test-AsciiAt $buffer $count 0 "OggS") {
        return "Ogg media"
    }
    if (Test-AsciiAt $buffer $count 0 "fLaC") {
        return "FLAC audio"
    }
    if (Test-AsciiAt $buffer $count 0 "ID3") {
        return "MP3 audio"
    }
    if (Test-AsciiAt $buffer $count 0 "RIFF") {
        foreach ($riffType in @("WAVE", "AVI ", "WEBP")) {
            if (Test-AsciiAt $buffer $count 8 $riffType) {
                return "RIFF $($riffType.Trim()) media"
            }
        }
    }
    if (Test-AsciiAt $buffer $count 4 "ftyp") {
        return "ISO base media"
    }
    if (Test-AsciiAt $buffer $count 0 "Kaydara FBX Binary  `0") {
        return "FBX model"
    }
    if (Test-AsciiAt $buffer $count 0 "glTF") {
        return "binary glTF model"
    }
    if ((Test-BytesAt $buffer $count 0 ([byte[]](0x50,0x4b,0x03,0x04))) -or
        (Test-BytesAt $buffer $count 0 ([byte[]](0x50,0x4b,0x05,0x06))) -or
        (Test-BytesAt $buffer $count 0 ([byte[]](0x50,0x4b,0x07,0x08)))) {
        return "nested ZIP archive"
    }
    if (Test-BytesAt $buffer $count 0 ([byte[]](0x37,0x7a,0xbc,0xaf,0x27,0x1c))) {
        return "7-Zip archive"
    }
    if ((Test-AsciiAt $buffer $count 0 "Rar!") -or
        (Test-BytesAt $buffer $count 0 ([byte[]](0x1f,0x8b))) -or
        (Test-AsciiAt $buffer $count 0 "BZh") -or
        (Test-BytesAt $buffer $count 0 ([byte[]](0xfd,0x37,0x7a,0x58,0x5a,0x00)))) {
        return "nested compressed archive"
    }
    if (Test-AsciiAt $buffer $count 0 "MSCF") {
        return "CAB archive"
    }
    if (Test-BytesAt $buffer $count 0 ([byte[]](0xd0,0xcf,0x11,0xe0,0xa1,0xb1,0x1a,0xe1))) {
        return "OLE compound document or installer"
    }

    return $null
}

function Assert-AllowedPayload {
    param(
        [Parameter(Mandatory = $true)]
        [string]$RelativePath,

        [Parameter(Mandatory = $true)]
        [string]$LiteralPath
    )

    $extension = [System.IO.Path]::GetExtension($RelativePath).
        ToLowerInvariant()
    if ($extension -eq ".map") {
        $allowedMonoMaps = @(
            "MonoBleedingEdge/etc/mono/2.0/settings.map",
            "MonoBleedingEdge/etc/mono/4.0/settings.map",
            "MonoBleedingEdge/etc/mono/4.5/settings.map"
        )
        if (-not ($allowedMonoMaps -ccontains $RelativePath)) {
            throw "Unexpected linker/debug map payload: $RelativePath"
        }
    }
    $prohibitedExtensions = @(
        ".7z", ".aac", ".ab", ".acb", ".anim", ".appx", ".asmdef",
        ".asmref", ".asset", ".assetbundle", ".avi", ".awb", ".bak",
        ".bat", ".blend", ".bmp", ".bundle", ".bz2", ".c", ".cab",
        ".cginc", ".controller", ".cpk", ".cpp", ".cs", ".csproj",
        ".dae", ".db", ".dbg", ".dds", ".dmp", ".exp", ".fbx",
        ".flac", ".gif", ".glb", ".gltf", ".gz", ".h", ".hlsl",
        ".hpp", ".ilk", ".iso", ".jpeg", ".jpg", ".js", ".jsx",
        ".ktx", ".lib", ".log", ".m4a", ".mat", ".mdb", ".meta",
        ".mkv", ".mov", ".mp3", ".mp4", ".msi", ".msix", ".ogg",
        ".pdf", ".pdb", ".png", ".prefab", ".ps1", ".psd1", ".psm1",
        ".py", ".rar", ".shader", ".sh", ".sln", ".sqlite", ".sqlite3",
        ".tar", ".tga", ".tif", ".tiff", ".ts", ".tsx", ".unity",
        ".unity3d", ".usm", ".vrm", ".wav", ".webm", ".webp", ".xz",
        ".zip"
    )
    if ($prohibitedExtensions -contains $extension) {
        throw "Prohibited game/source/media/debug payload extension '$extension': $RelativePath"
    }

    $leafName = [System.IO.Path]::GetFileName($RelativePath)
    $lowerPath = $RelativePath.ToLowerInvariant()
    if ($leafName -match '^(?i:master\.mdb|meta(?:\.(?:db|json))?|thumbs\.db|desktop\.ini|\.ds_store)$' -or
        $leafName -match '^(?i:[0-9a-f]{32,64})$' -or
        $lowerPath -match '(^|/)(?:dat|assets|packages|projectsettings|library|temp|logs|scratch|\.git|\.github)(/|$)' -or
        $lowerPath -match '(^|/)[^/]*_burstdebuginformation_donotship(/|$)' -or
        $leafName -ieq "RECORDING_ONLY_DO_NOT_SHIP.txt" -or
        $leafName -ieq "Launch-Recording-Tools.cmd" -or
        $leafName -ieq "SMOKE_TEST.md" -or
        $leafName -match '^(?i:UnityEditor\..*\.dll|UnityEngine\.TestRunner\.dll|nunit\.framework\.dll)$') {
        throw "Prohibited game/source/debug payload name: $RelativePath"
    }

    $magic = Get-ProhibitedMagic -LiteralPath $LiteralPath
    if ($null -ne $magic) {
        throw "Prohibited $magic detected by file signature: $RelativePath"
    }
}

function Get-PeInfo {
    param(
        [Parameter(Mandatory = $true)]
        [string]$LiteralPath
    )

    $stream = [System.IO.File]::Open(
        $LiteralPath,
        [System.IO.FileMode]::Open,
        [System.IO.FileAccess]::Read,
        [System.IO.FileShare]::Read)
    $reader = [System.IO.BinaryReader]::new($stream)
    try {
        if ($stream.Length -lt 128) {
            throw "PE file is too short: $LiteralPath"
        }
        if ($reader.ReadUInt16() -ne 0x5a4d) {
            throw "Expected a PE file with an MZ header: $LiteralPath"
        }

        $stream.Position = 0x3c
        $peOffset = $reader.ReadInt32()
        if ($peOffset -lt 0x40 -or ($peOffset + 24) -gt $stream.Length) {
            throw "Invalid PE header offset: $LiteralPath"
        }

        $stream.Position = $peOffset
        if ($reader.ReadUInt32() -ne 0x00004550) {
            throw "Invalid PE signature: $LiteralPath"
        }
        $machine = $reader.ReadUInt16()
        [void]$reader.ReadUInt16()
        [void]$reader.ReadUInt32()
        [void]$reader.ReadUInt32()
        [void]$reader.ReadUInt32()
        $optionalHeaderSize = $reader.ReadUInt16()
        [void]$reader.ReadUInt16()

        $optionalHeaderStart = $stream.Position
        if (($optionalHeaderStart + $optionalHeaderSize) -gt $stream.Length -or
            $optionalHeaderSize -lt 2) {
            throw "Invalid PE optional header: $LiteralPath"
        }
        $magic = $reader.ReadUInt16()
        if ($magic -eq 0x10b) {
            $numberOfDirectoriesOffset = 92
            $dataDirectoriesOffset = 96
        }
        elseif ($magic -eq 0x20b) {
            $numberOfDirectoriesOffset = 108
            $dataDirectoriesOffset = 112
        }
        else {
            throw "Unsupported PE optional-header magic 0x$($magic.ToString('x')): $LiteralPath"
        }

        $isManaged = $false
        if ($optionalHeaderSize -ge ($dataDirectoriesOffset + (15 * 8))) {
            $stream.Position = $optionalHeaderStart + $numberOfDirectoriesOffset
            $directoryCount = $reader.ReadUInt32()
            if ($directoryCount -gt 14) {
                $stream.Position = $optionalHeaderStart +
                    $dataDirectoriesOffset + (14 * 8)
                $cliRva = $reader.ReadUInt32()
                $cliSize = $reader.ReadUInt32()
                $isManaged = $cliRva -ne 0 -and $cliSize -ne 0
            }
        }

        $machineName = switch ($machine) {
            0x014c { "I386" }
            0x8664 { "AMD64" }
            0xaa64 { "ARM64" }
            default { "0x$($machine.ToString('x4'))" }
        }
        return [pscustomobject]@{
            Machine = $machine
            MachineName = $machineName
            IsManaged = $isManaged
        }
    }
    finally {
        $reader.Dispose()
        $stream.Dispose()
    }
}

function Test-IsPeFile {
    param(
        [Parameter(Mandatory = $true)]
        [string]$LiteralPath
    )

    $stream = [System.IO.File]::Open(
        $LiteralPath,
        [System.IO.FileMode]::Open,
        [System.IO.FileAccess]::Read,
        [System.IO.FileShare]::Read)
    try {
        return $stream.Length -ge 2 -and
            $stream.ReadByte() -eq 0x4d -and
            $stream.ReadByte() -eq 0x5a
    }
    finally {
        $stream.Dispose()
    }
}

$resolvedPackageDirectory = Get-FullExistingPath `
    -LiteralPath $PackageDirectory `
    -PathType Container
$resolvedZipPath = Get-FullExistingPath `
    -LiteralPath $ZipPath `
    -PathType Leaf
$resolvedSidecarPath = Get-FullExistingPath `
    -LiteralPath $SidecarPath `
    -PathType Leaf

$stageInfo = Get-Item -LiteralPath $resolvedPackageDirectory -Force
$zipInfo = Get-Item -LiteralPath $resolvedZipPath -Force
$sidecarInfo = Get-Item -LiteralPath $resolvedSidecarPath -Force
if ($stageInfo.Name -cne $packageRootName) {
    throw "Staging directory must have the exact name '$packageRootName': $($stageInfo.Name)"
}
if ($zipInfo.Name -cne $expectedZipName) {
    throw "ZIP filename must be exactly '$expectedZipName': $($zipInfo.Name)"
}
if ($sidecarInfo.Name -cne $expectedSidecarName) {
    throw "Sidecar filename must be exactly '$expectedSidecarName': $($sidecarInfo.Name)"
}
$expectedSidecarPath = "$resolvedZipPath.sha256"
if (-not $resolvedSidecarPath.Equals(
    $expectedSidecarPath,
    [System.StringComparison]::OrdinalIgnoreCase)) {
    throw "The sidecar must be adjacent to and named after the ZIP: $expectedSidecarPath"
}
if ((Test-PathIsWithin $resolvedZipPath $resolvedPackageDirectory) -or
    (Test-PathIsWithin $resolvedSidecarPath $resolvedPackageDirectory)) {
    throw "The ZIP and sidecar must not be inside the staging directory."
}

$sidecarBytes = [System.IO.File]::ReadAllBytes($resolvedSidecarPath)
if ($sidecarBytes.Length -ge 3 -and
    $sidecarBytes[0] -eq 0xef -and
    $sidecarBytes[1] -eq 0xbb -and
    $sidecarBytes[2] -eq 0xbf) {
    throw "The SHA-256 sidecar must be UTF-8 without a byte-order mark."
}
$strictUtf8 = [System.Text.UTF8Encoding]::new($false, $true)
$sidecarText = $strictUtf8.GetString($sidecarBytes)
$actualZipHash = Get-Sha256Lower $resolvedZipPath
$expectedSidecarLine = "$actualZipHash  $expectedZipName"
if ($sidecarText -cne ($expectedSidecarLine + "`r`n") -and
    $sidecarText -cne ($expectedSidecarLine + "`n")) {
    throw "The sidecar must contain exactly the lowercase fresh SHA-256, two spaces, and '$expectedZipName'."
}

$stagePrefix = $resolvedPackageDirectory.TrimEnd(
    [System.IO.Path]::DirectorySeparatorChar,
    [System.IO.Path]::AltDirectorySeparatorChar) +
    [System.IO.Path]::DirectorySeparatorChar
$stageFiles = [System.Collections.Generic.Dictionary[string,object]]::new(
    [System.StringComparer]::Ordinal)
$stageDirectories = [System.Collections.Generic.HashSet[string]]::new(
    [System.StringComparer]::Ordinal)
$stageSpellings = [System.Collections.Generic.Dictionary[string,object]]::new(
    [System.StringComparer]::Ordinal)

foreach ($item in (Get-SafeStageItems $resolvedPackageDirectory)) {
    if (-not $item.FullName.StartsWith(
        $stagePrefix,
        [System.StringComparison]::OrdinalIgnoreCase)) {
        throw "A staged path escaped the staging directory: $($item.FullName)"
    }
    $relativePath = $item.FullName.Substring($stagePrefix.Length).
        Replace('\', '/')
    $archivePath = "$packageRootName/$relativePath"
    $pathForValidation = if ($item.IsDirectory) {
        "$archivePath/"
    }
    else {
        $archivePath
    }
    $cleanPath = Assert-SafePackagePath `
        -ArchivePath $pathForValidation `
        -IsDirectory ([bool]$item.IsDirectory)
    Assert-ExpectedTopLevel $cleanPath ([bool]$item.IsDirectory)
    Register-PathSpelling `
        -PathWithoutSlash $cleanPath `
        -IsDirectory ([bool]$item.IsDirectory) `
        -Spellings $stageSpellings

    if ($item.IsDirectory) {
        [void]$stageDirectories.Add($cleanPath)
        continue
    }
    if ($stageFiles.ContainsKey($cleanPath)) {
        throw "Duplicate staged file path: $cleanPath"
    }

    Assert-AllowedPayload `
        -RelativePath $relativePath `
        -LiteralPath $item.FullName
    $before = Get-Item -LiteralPath $item.FullName -Force
    $fileHash = Get-Sha256Lower $item.FullName
    $after = Get-Item -LiteralPath $item.FullName -Force
    if ($before.Length -ne $after.Length -or
        $before.LastWriteTimeUtc.Ticks -ne $after.LastWriteTimeUtc.Ticks) {
        throw "A staged file changed while it was being audited: $relativePath"
    }
    $stageFiles.Add(
        $cleanPath,
        [pscustomobject]@{
            FullName = $item.FullName
            RelativePath = $relativePath
            Length = [int64]$after.Length
            Hash = $fileHash
        })
}
if ($stageFiles.Count -eq 0) {
    throw "The staging directory contains no files."
}

$requiredStagePaths = @(
    "LICENSE.txt",
    "README.md",
    "TESTING.md",
    "THIRD_PARTY_NOTICES.txt",
    "Licenses/SQLite3MultipleCiphers-MIT.txt",
    "Licenses/UniWindowController-MIT.txt",
    "Licenses/BootstrapIcons-MIT.txt",
    "Licenses/KenneyFurnitureKit-CC0-1.0.txt",
    "UmaDesktopPet.exe",
    "UnityPlayer.dll",
    "UnityCrashHandler64.exe",
    "MonoBleedingEdge/EmbedRuntime/mono-2.0-bdwgc.dll",
    "UmaDesktopPet_Data/app.info",
    "UmaDesktopPet_Data/boot.config",
    "UmaDesktopPet_Data/globalgamemanagers",
    "UmaDesktopPet_Data/globalgamemanagers.assets",
    "UmaDesktopPet_Data/resources.assets",
    "UmaDesktopPet_Data/Managed/Assembly-CSharp.dll"
)
foreach ($requiredPath in $requiredStagePaths) {
    $archivePath = "$packageRootName/$requiredPath"
    if (-not $stageFiles.ContainsKey($archivePath)) {
        throw "Required Unity player or release file is missing: $requiredPath"
    }
}

$supportFileSources = [ordered]@{
    "README.md" = "README.md"
    "LICENSE.txt" = "LICENSE"
    "TESTING.md" = "docs/TESTING.md"
    "THIRD_PARTY_NOTICES.txt" = "THIRD_PARTY_NOTICES.txt"
    "Licenses/SQLite3MultipleCiphers-MIT.txt" =
        "ThirdParty/SQLite3MultipleCiphers/LICENSE"
    "Licenses/UniWindowController-MIT.txt" =
        "ThirdParty/UniWindowController/LICENSE.md"
    "Licenses/BootstrapIcons-MIT.txt" =
        "ThirdParty/BootstrapIcons/LICENSE"
    "Licenses/KenneyFurnitureKit-CC0-1.0.txt" =
        "ThirdParty/KenneyFurnitureKit/LICENSE.txt"
}
foreach ($destinationPath in $supportFileSources.Keys) {
    $sourcePath = Join-Path `
        $repositoryRoot `
        $supportFileSources[$destinationPath]
    if (-not (Test-Path -LiteralPath $sourcePath -PathType Leaf)) {
        throw "Repository source for required support file is missing: $sourcePath"
    }
    $staged = $stageFiles["$packageRootName/$destinationPath"]
    $sourceHash = Get-Sha256Lower $sourcePath
    if (-not $sourceHash.Equals(
        $staged.Hash,
        [System.StringComparison]::OrdinalIgnoreCase)) {
        throw "Packaged support file differs from repository source: $destinationPath"
    }
}

$appInfoPath =
    $stageFiles["$packageRootName/UmaDesktopPet_Data/app.info"].FullName
$appInfoLines = [System.IO.File]::ReadAllLines($appInfoPath)
if ($appInfoLines.Count -ne 2 -or
    $appInfoLines[0] -cne "pqqqqqdev" -or
    $appInfoLines[1] -cne "Uma Desktop Pet") {
    throw "UmaDesktopPet_Data/app.info does not contain the exact approved company and product names."
}

$globalManagersPath =
    $stageFiles["$packageRootName/UmaDesktopPet_Data/globalgamemanagers"].FullName
$globalManagersText = [System.Text.Encoding]::GetEncoding(28591).GetString(
    [System.IO.File]::ReadAllBytes($globalManagersPath))
$versionPattern = "(?<![0-9A-Za-z.+-])" +
    [System.Text.RegularExpressions.Regex]::Escape($Version) +
    "(?![0-9A-Za-z.+-])"
if (-not [System.Text.RegularExpressions.Regex]::IsMatch(
    $globalManagersText,
    $versionPattern,
    [System.Text.RegularExpressions.RegexOptions]::CultureInvariant)) {
    throw "The packaged globalgamemanagers does not contain app version '$Version'."
}

$readmeText = [System.IO.File]::ReadAllText(
    $stageFiles["$packageRootName/README.md"].FullName)
if ($readmeText.IndexOf(
    $expectedZipName,
    [System.StringComparison]::Ordinal) -lt 0 -or
    $readmeText.IndexOf(
        $expectedSidecarName,
        [System.StringComparison]::Ordinal) -lt 0) {
    throw "README.md must name the exact candidate ZIP and SHA-256 sidecar."
}

$projectVersionPath = Join-Path `
    $repositoryRoot `
    "ProjectSettings/ProjectVersion.txt"
$projectVersionText = [System.IO.File]::ReadAllText($projectVersionPath)
if ($projectVersionText -notmatch
    '(?m)^m_EditorVersion: 2022\.3\.62f2\s*$' -or
    $projectVersionText -notmatch
    '(?m)^m_EditorVersionWithRevision: 2022\.3\.62f2 \(7670c08855a9\)\s*$') {
    throw "ProjectSettings/ProjectVersion.txt is not the approved exact Unity $expectedUnityProductVersion."
}
if ($projectVersionText -match '2022\.3\.62f1') {
    throw "Rejected Unity 2022.3.62f1 project metadata."
}

foreach ($unityMetadataPath in @("UmaDesktopPet.exe", "UnityPlayer.dll")) {
    $staged = $stageFiles["$packageRootName/$unityMetadataPath"]
    $metadata = [System.Diagnostics.FileVersionInfo]::GetVersionInfo(
        $staged.FullName)
    if ($metadata.ProductVersion -ne $expectedUnityProductVersion) {
        throw "$unityMetadataPath reports Unity product version '$($metadata.ProductVersion)', expected '$expectedUnityProductVersion'."
    }
    if (($metadata.ProductVersion + " " + $metadata.FileVersion) -match
        '2022\.3\.62f1') {
        throw "$unityMetadataPath contains rejected Unity 2022.3.62f1 metadata."
    }
}

$bootConfigPath =
    $stageFiles["$packageRootName/UmaDesktopPet_Data/boot.config"].FullName
$bootConfig = [System.IO.File]::ReadAllText($bootConfigPath)
if ($bootConfig -match '(?im)^\s*(?:player-connection-debug|wait-for-native-debugger|profiler-enabled)\s*=\s*(?!0\s*$).+$') {
    throw "boot.config enables a development debugger or profiler option."
}
if ($bootConfig -match '(?im)^\s*player-connection-[^=]*=') {
    throw "boot.config contains a development player-connection setting."
}

$peReports = [System.Collections.Generic.List[object]]::new()
foreach ($staged in $stageFiles.Values) {
    $extension = [System.IO.Path]::GetExtension($staged.RelativePath)
    if ($extension -ine ".exe" -and
        $extension -ine ".dll" -and
        -not (Test-IsPeFile $staged.FullName)) {
        continue
    }

    $pe = Get-PeInfo $staged.FullName
    if (-not $pe.IsManaged -and $pe.Machine -ne 0x8664) {
        throw "Native PE is not AMD64 ($($pe.MachineName)): $($staged.RelativePath)"
    }
    $peReports.Add([pscustomobject]@{
        Path = $staged.RelativePath
        Machine = $pe.MachineName
        IsManaged = [bool]$pe.IsManaged
        FullName = $staged.FullName
    })
}

Add-Type -AssemblyName System.IO.Compression
$zipStream = [System.IO.File]::Open(
    $resolvedZipPath,
    [System.IO.FileMode]::Open,
    [System.IO.FileAccess]::Read,
    [System.IO.FileShare]::Read)
$archive = $null
try {
    $archive = [System.IO.Compression.ZipArchive]::new(
        $zipStream,
        [System.IO.Compression.ZipArchiveMode]::Read,
        $false)
    $zipFiles = [System.Collections.Generic.Dictionary[string,object]]::new(
        [System.StringComparer]::Ordinal)
    $zipEntryNames = [System.Collections.Generic.HashSet[string]]::new(
        [System.StringComparer]::Ordinal)
    $zipSpellings = [System.Collections.Generic.Dictionary[string,object]]::new(
        [System.StringComparer]::Ordinal)

    foreach ($entry in $archive.Entries) {
        $rawName = $entry.FullName
        if (-not $zipEntryNames.Add($rawName)) {
            throw "Duplicate exact ZIP entry: $rawName"
        }
        $isDirectory = $rawName.EndsWith(
            "/",
            [System.StringComparison]::Ordinal)
        $cleanPath = Assert-SafePackagePath $rawName $isDirectory
        Assert-ExpectedTopLevel $cleanPath $isDirectory
        Register-PathSpelling $cleanPath $isDirectory $zipSpellings

        $externalAttributes = [System.BitConverter]::ToUInt32(
            [System.BitConverter]::GetBytes(
                [int32]$entry.ExternalAttributes),
            0)
        $dosAttributes = $externalAttributes -band 0xffff
        $unixMode = ($externalAttributes -shr 16) -band 0xffff
        $unixType = $unixMode -band 0xf000
        if (($dosAttributes -band 0x0400) -ne 0 -or
            ($dosAttributes -band 0x0040) -ne 0) {
            throw "ZIP reparse points and device entries are forbidden: $rawName"
        }
        if (($dosAttributes -band 0x0010) -ne 0 -and -not $isDirectory) {
            throw "ZIP entry has conflicting DOS directory attributes: $rawName"
        }
        if ($unixType -ne 0 -and
            $unixType -ne 0x4000 -and
            $unixType -ne 0x8000) {
            throw "ZIP symlink or special Unix entry type 0x$($unixType.ToString('x4')) is forbidden: $rawName"
        }
        if ($unixType -ne 0 -and
            (($unixType -eq 0x4000) -ne $isDirectory)) {
            throw "ZIP entry has conflicting Unix file type attributes: $rawName"
        }

        if ($isDirectory) {
            if ($entry.Length -ne 0) {
                throw "ZIP directory entries must be empty: $rawName"
            }
            if ($cleanPath -cne $packageRootName -and
                -not $stageDirectories.Contains($cleanPath)) {
                throw "ZIP contains a directory absent from staging: $rawName"
            }
            continue
        }

        if ($zipFiles.ContainsKey($cleanPath)) {
            throw "Duplicate ZIP file path: $rawName"
        }
        $entryStream = $entry.Open()
        try {
            $entryHash = Get-StreamSha256Lower $entryStream
        }
        finally {
            $entryStream.Dispose()
        }
        $zipFiles.Add(
            $cleanPath,
            [pscustomobject]@{
                Length = [int64]$entry.Length
                Hash = $entryHash
            })
    }

    if ($zipFiles.Count -ne $stageFiles.Count) {
        throw "Stage/ZIP file-count mismatch: stage=$($stageFiles.Count), ZIP=$($zipFiles.Count)."
    }
    foreach ($archivePath in $stageFiles.Keys) {
        if (-not $zipFiles.ContainsKey($archivePath)) {
            throw "Staged file is missing from the ZIP: $archivePath"
        }
        $staged = $stageFiles[$archivePath]
        $zipped = $zipFiles[$archivePath]
        if ($staged.Length -ne $zipped.Length) {
            throw "Stage/ZIP length mismatch for $archivePath"
        }
        if (-not $staged.Hash.Equals(
            $zipped.Hash,
            [System.StringComparison]::OrdinalIgnoreCase)) {
            throw "Stage/ZIP SHA-256 mismatch for $archivePath"
        }
    }
    foreach ($archivePath in $zipFiles.Keys) {
        if (-not $stageFiles.ContainsKey($archivePath)) {
            throw "ZIP file is absent from staging: $archivePath"
        }
    }
}
finally {
    if ($null -ne $archive) {
        $archive.Dispose()
    }
    $zipStream.Dispose()
}

Write-Host "Authenticode report (informational; signatures are not required):"
$signatureCommand = Get-Command Get-AuthenticodeSignature -ErrorAction SilentlyContinue
if ($null -eq $signatureCommand) {
    Write-Host "  unavailable in this PowerShell host"
}
else {
    foreach ($peReport in ($peReports | Sort-Object Path)) {
        if ($peReport.IsManaged) {
            continue
        }
        try {
            $signature = Get-AuthenticodeSignature `
                -LiteralPath $peReport.FullName
            $signer = if ($null -ne $signature.SignerCertificate) {
                $signature.SignerCertificate.Subject
            }
            else {
                "<none>"
            }
            Write-Host (
                "  {0}: status={1}; signer={2}" -f
                $peReport.Path,
                $signature.Status,
                $signer)
        }
        catch {
            Write-Host (
                "  {0}: status=Unavailable; detail={1}" -f
                $peReport.Path,
                $_.Exception.Message)
        }
    }
}

$managedCount = @($peReports | Where-Object IsManaged).Count
$nativeCount = $peReports.Count - $managedCount
Write-Host "Package audit passed."
Write-Host "  Version:        $Version"
Write-Host "  Stage:          $resolvedPackageDirectory"
Write-Host "  ZIP:            $resolvedZipPath"
Write-Host "  SHA-256:        $actualZipHash"
Write-Host "  Files verified: $($stageFiles.Count)"
Write-Host "  PE files:       $nativeCount native AMD64, $managedCount managed"
Write-Host "  Unity:          $expectedUnityProductVersion"
Write-Host "  Antivirus:      not run; record any external scan separately"
