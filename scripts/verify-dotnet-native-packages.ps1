param(
    [Parameter(Mandatory = $true)]
    [string]$PackageDirectory,

    [Parameter(Mandatory = $true)]
    [string]$Version
)

$ErrorActionPreference = "Stop"
Add-Type -AssemblyName System.IO.Compression.FileSystem

function Assert-PackageEntries([string]$PackageName, [string[]]$ExpectedEntries) {
    $path = Join-Path $PackageDirectory "$PackageName.$Version.nupkg"
    if (-not (Test-Path -LiteralPath $path -PathType Leaf)) {
        throw "Native package is missing: $path"
    }

    $archive = [System.IO.Compression.ZipFile]::OpenRead((Resolve-Path -LiteralPath $path))
    try {
        $entries = @($archive.Entries | ForEach-Object { $_.FullName.Replace('\', '/') })
        foreach ($expected in $ExpectedEntries) {
            if ($entries -notcontains $expected) {
                throw "$PackageName is missing native package entry: $expected"
            }
        }
    } finally {
        $archive.Dispose()
    }
}

Assert-PackageEntries "Gua.Core" @(
    "runtimes/win-x64/native/gua.dll",
    "runtimes/linux-x64/native/libgua.so",
    "runtimes/osx-x64/native/libgua.dylib",
    "runtimes/osx-arm64/native/libgua.dylib"
)

Assert-PackageEntries "Gua.Runtime" @(
    "runtimes/win-x64/native/gua_runtime.dll",
    "runtimes/linux-x64/native/libgua_runtime.so",
    "runtimes/osx-x64/native/libgua_runtime.dylib",
    "runtimes/osx-arm64/native/libgua_runtime.dylib"
)

Write-Host "Verified native NuGet assets for $Version."
