param(
    [Parameter(Mandatory = $true)]
    [string]$Version,

    [Parameter(Mandatory = $true)]
    [string]$WindowsAssetsDirectory,

    [Parameter(Mandatory = $true)]
    [string]$NativeAssetsDirectory,

    [Parameter(Mandatory = $true)]
    [string]$WebNativeDirectory,

    [Parameter(Mandatory = $true)]
    [string]$UnityPackageDirectory,

    [Parameter(Mandatory = $true)]
    [string]$OutputDirectory
)

$ErrorActionPreference = "Stop"
$root = Split-Path -Parent $PSScriptRoot

function Require-File([string]$Path) {
    if (-not (Test-Path -LiteralPath $Path -PathType Leaf)) {
        throw "Required release asset is missing: $Path"
    }
    return (Resolve-Path -LiteralPath $Path).Path
}

if (Test-Path -LiteralPath $OutputDirectory) {
    $existingOutput = @(Get-ChildItem -LiteralPath $OutputDirectory -Force)
    if ($existingOutput.Count -gt 0) {
        throw "Release output directory must be empty: $OutputDirectory"
    }
} else {
    New-Item -ItemType Directory -Force $OutputDirectory | Out-Null
}

$windowsAddonArchive = Require-File (Join-Path $WindowsAssetsDirectory "gua-godot-plugin-windows-v$Version.zip")
$webAddonArchive = Require-File (Join-Path $WebNativeDirectory "gua-godot-plugin-web-v$Version.zip")
$unityArchive = Require-File (Join-Path $UnityPackageDirectory "com.link1345.gua-$Version.tgz")

$staging = Join-Path $OutputDirectory ".godot-addon-staging"
$windowsAddonRoot = Join-Path $staging "windows"
$webAddonRoot = Join-Path $staging "web"

try {
    New-Item -ItemType Directory -Force $windowsAddonRoot, $webAddonRoot | Out-Null
    Expand-Archive -LiteralPath $windowsAddonArchive -DestinationPath $windowsAddonRoot
    Expand-Archive -LiteralPath $webAddonArchive -DestinationPath $webAddonRoot

    $windowsAddon = Join-Path $windowsAddonRoot "addons/gua"
    $webAddon = Join-Path $webAddonRoot "addons/gua"
    if (-not (Test-Path -LiteralPath $windowsAddon -PathType Container)) {
        throw "Windows Godot archive does not contain addons/gua: $windowsAddonArchive"
    }
    if (-not (Test-Path -LiteralPath $webAddon -PathType Container)) {
        throw "Web Godot archive does not contain addons/gua: $webAddonArchive"
    }

    foreach ($webMetadata in Get-ChildItem -LiteralPath $webAddon -File) {
        $windowsMetadata = Join-Path $windowsAddon $webMetadata.Name
        if (-not (Test-Path -LiteralPath $windowsMetadata -PathType Leaf)) {
            throw "Godot addon metadata is missing from the Windows archive: $($webMetadata.Name)"
        }
        $webContent = [System.IO.File]::ReadAllText($webMetadata.FullName).Replace("`r`n", "`n").Replace("`r", "`n")
        $windowsContent = [System.IO.File]::ReadAllText($windowsMetadata).Replace("`r`n", "`n").Replace("`r", "`n")
        if (-not [string]::Equals($webContent, $windowsContent, [System.StringComparison]::Ordinal)) {
            throw "Godot addon metadata differs between Windows and Web archives beyond line endings: $($webMetadata.Name)"
        }
    }

    $windowsBin = Join-Path $windowsAddon "bin"
    $webBin = Join-Path $webAddon "bin"
    Copy-Item -LiteralPath (Join-Path $webBin "gua_godot.web.debug.wasm32.wasm") -Destination $windowsBin
    Copy-Item -LiteralPath (Join-Path $webBin "gua_godot.web.release.wasm32.wasm") -Destination $windowsBin

    & (Join-Path $root "scripts/verify-godot-windows-addon.ps1") -AddonDirectory $windowsAddon -RequireBinaries
    & (Join-Path $root "scripts/verify-godot-web-addon.ps1") -AddonDirectory $windowsAddon -RequireBinaries

    $godotArchive = Join-Path $OutputDirectory "gua-godot-addon-v$Version.zip"
    Compress-Archive -Path (Join-Path $windowsAddonRoot "addons") -DestinationPath $godotArchive -CompressionLevel Optimal

    Copy-Item -LiteralPath $unityArchive -Destination $OutputDirectory

    foreach ($rid in "linux-x64", "osx-x64", "osx-arm64") {
        $nativeArchive = Require-File (Join-Path $NativeAssetsDirectory "gua-native-$rid-v$Version.zip")
        Copy-Item -LiteralPath $nativeArchive -Destination $OutputDirectory
    }
    $windowsNativeArchive = Require-File (Join-Path $WindowsAssetsDirectory "gua-native-win-x64-v$Version.zip")
    Copy-Item -LiteralPath $windowsNativeArchive -Destination $OutputDirectory

    $inspectorFiles = @(
        Get-ChildItem -LiteralPath (Join-Path $WindowsAssetsDirectory "inspector") -Recurse -File |
            Where-Object { $_.Extension -in ".exe", ".msi" }
    )
    if ($inspectorFiles.Count -eq 0) {
        throw "No Inspector installer was found below $WindowsAssetsDirectory"
    }
    foreach ($inspectorFile in $inspectorFiles) {
        $destination = Join-Path $OutputDirectory $inspectorFile.Name
        if (Test-Path -LiteralPath $destination) {
            throw "Inspector installer names must be unique: $($inspectorFile.Name)"
        }
        Copy-Item -LiteralPath $inspectorFile.FullName -Destination $destination
    }
} finally {
    if (Test-Path -LiteralPath $staging) {
        Remove-Item -LiteralPath $staging -Recurse -Force
    }
}

$publishedFiles = @(Get-ChildItem -LiteralPath $OutputDirectory -File | Sort-Object Name)
Write-Host "GitHub Release assets:"
$publishedFiles | ForEach-Object { Write-Host "- $($_.Name)" }
