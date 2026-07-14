param(
    [Parameter(Mandatory = $true)][string]$UnityProjectPath
)
$ErrorActionPreference = "Stop"
$project = (Resolve-Path -LiteralPath $UnityProjectPath).Path
$package = Get-ChildItem -LiteralPath (Join-Path $project "Library\PackageCache") -Recurse -Filter "TMP Essential Resources.unitypackage" |
    Select-Object -First 1
if ($null -eq $package) { throw "TMP Essential Resources.unitypackage was not found. Open the Unity project once so UPM can restore com.unity.ugui." }
$destinationRoot = Join-Path $project "Assets"
$temp = Join-Path $project "Temp\GuaTmpEssentials"
if (Test-Path -LiteralPath $temp) { Remove-Item -LiteralPath $temp -Recurse -Force }
New-Item -ItemType Directory -Path $temp -Force | Out-Null
try {
    tar -xf $package.FullName -C $temp
    if ($LASTEXITCODE -ne 0) { throw "Failed to extract $($package.FullName)." }
    foreach ($pathnameFile in Get-ChildItem -LiteralPath $temp -Recurse -Filter pathname) {
        $entry = Split-Path -Parent $pathnameFile.FullName
        $relative = (Get-Content -LiteralPath $pathnameFile.FullName -Raw).Trim().Replace('/', '\')
        if (-not $relative.StartsWith("Assets\", [StringComparison]::Ordinal)) { continue }
        $relative = $relative.Substring("Assets\".Length)
        $target = Join-Path $destinationRoot $relative
        New-Item -ItemType Directory -Path (Split-Path -Parent $target) -Force | Out-Null
        $asset = Join-Path $entry "asset"
        $meta = Join-Path $entry "asset.meta"
        if (Test-Path -LiteralPath $asset) { Copy-Item -LiteralPath $asset -Destination $target -Force }
        if (Test-Path -LiteralPath $meta) { Copy-Item -LiteralPath $meta -Destination ($target + ".meta") -Force }
    }
}
finally {
    if (Test-Path -LiteralPath $temp) { Remove-Item -LiteralPath $temp -Recurse -Force }
}
