param(
    [Parameter(Mandatory = $true)]
    [string]$AssetsFile,

    [Parameter(Mandatory = $true)]
    [string]$TargetFramework,

    [Parameter(Mandatory = $true)]
    [string]$Destination
)

$ErrorActionPreference = "Stop"

if (-not (Test-Path -LiteralPath $AssetsFile)) {
    throw "Managed dependency assets file is missing: $AssetsFile"
}

$assets = Get-Content -LiteralPath $AssetsFile -Raw | ConvertFrom-Json -AsHashtable
$targetName = @($assets.targets.Keys | Where-Object { $_ -eq $TargetFramework -or $_ -like "$TargetFramework/*" }) | Select-Object -First 1
if ([string]::IsNullOrWhiteSpace($targetName)) {
    throw "Target framework '$TargetFramework' is missing from $AssetsFile."
}

$packageRoots = @($assets.packageFolders.Keys)
if ($packageRoots.Count -eq 0) {
    throw "No NuGet package folder is recorded in $AssetsFile."
}

New-Item -ItemType Directory -Path $Destination -Force | Out-Null
$copied = @{}
foreach ($libraryKey in @($assets.targets[$targetName].Keys | Sort-Object)) {
    $library = $assets.libraries[$libraryKey]
    if ($null -eq $library -or $library.type -ne "package") { continue }

    $runtimeAssets = $assets.targets[$targetName][$libraryKey].runtime
    if ($null -eq $runtimeAssets) { continue }
    $assetPaths = @($runtimeAssets.Keys | Where-Object { $_.EndsWith(".dll", [StringComparison]::OrdinalIgnoreCase) } | Sort-Object)
    $hasFrameworkPlaceholder = @($runtimeAssets.Keys | Where-Object { $_ -eq "_._" -or $_.EndsWith("/_._", [StringComparison]::Ordinal) }).Count -gt 0
    if ($assetPaths.Count -eq 0 -and $hasFrameworkPlaceholder) {
        # The .NET SDK replaces framework-provided netstandard2.1 dependencies with _._.
        # Unity packages must carry their own implementation closure, so use the compatible
        # netstandard2.0 implementation selected from the same restored package version.
        $packageId = $libraryKey.Substring(0, $libraryKey.LastIndexOf('/'))
        foreach ($framework in "netstandard2.1", "netstandard2.0") {
            $fallback = "lib/$framework/$packageId.dll"
            $exists = $false
            foreach ($packageRoot in $packageRoots) {
                if (Test-Path -LiteralPath (Join-Path (Join-Path $packageRoot $library.path) $fallback)) {
                    $exists = $true
                    break
                }
            }
            if ($exists) { $assetPaths = @($fallback); break }
        }
    }

    foreach ($assetPath in $assetPaths) {

        $source = $null
        foreach ($packageRoot in $packageRoots) {
            $candidate = Join-Path (Join-Path $packageRoot $library.path) $assetPath
            if (Test-Path -LiteralPath $candidate) { $source = $candidate; break }
        }
        if ($null -eq $source) {
            throw "Resolved managed Unity dependency is missing: $libraryKey/$assetPath"
        }

        $fileName = Split-Path -Leaf $source
        if ($copied.ContainsKey($fileName) -and $copied[$fileName] -ne $source) {
            throw "Multiple managed Unity dependencies resolve to '$fileName': '$($copied[$fileName])' and '$source'."
        }
        Copy-Item -LiteralPath $source -Destination (Join-Path $Destination $fileName) -Force
        $copied[$fileName] = $source
    }
}

if ($copied.Count -eq 0) {
    throw "No managed runtime dependencies were resolved for '$TargetFramework' from $AssetsFile."
}

Write-Host "Copied $($copied.Count) resolved managed Unity dependencies for $TargetFramework."
