param(
    [Parameter(Mandatory = $true)]
    [string]$Version,

    [Parameter(Mandatory = $true)]
    [string]$NativeDirectory,

    [string]$Configuration = "Release"
)

$ErrorActionPreference = "Stop"
$root = Split-Path -Parent $PSScriptRoot
$plugins = Join-Path $root "examples/unity-smoke/Assets/Plugins/Gua"
$managed = Join-Path $plugins "Managed"
$native = Join-Path $plugins "x86_64"

dotnet restore (Join-Path $root "bindings/dotnet/src/Gua.Runtime/Gua.Runtime.csproj") -p:RestoreEnablePackagePruning=false --force-evaluate
dotnet build (Join-Path $root "bindings/dotnet/src/Gua.Core/Gua.Core.csproj") -c $Configuration -f netstandard2.1 --no-restore -p:Version=$Version -p:RestoreEnablePackagePruning=false
dotnet build (Join-Path $root "bindings/dotnet/src/Gua.Runtime/Gua.Runtime.csproj") -c $Configuration -f netstandard2.1 --no-restore -p:Version=$Version -p:RestoreEnablePackagePruning=false
if ($LASTEXITCODE -ne 0) { throw "Failed to build the managed Unity package closure." }

New-Item -ItemType Directory -Force $managed, $native | Out-Null
Copy-Item (Join-Path $root "bindings/dotnet/src/Gua.Core/bin/$Configuration/netstandard2.1/Gua.Core.dll") $managed -Force
Copy-Item (Join-Path $root "bindings/dotnet/src/Gua.Runtime/bin/$Configuration/netstandard2.1/Gua.Runtime.dll") $managed -Force

& (Join-Path $PSScriptRoot "copy-unity-managed-closure.ps1") `
    -AssetsFile (Join-Path $root "bindings/dotnet/src/Gua.Runtime/obj/project.assets.json") `
    -TargetFramework "netstandard2.1" `
    -Destination $managed

foreach ($file in "gua.dll", "gua_runtime.dll") {
    $matches = @(Get-ChildItem -LiteralPath $NativeDirectory -Recurse -File -Filter $file)
    if ($matches.Count -ne 1) { throw "Expected exactly one $file below '$NativeDirectory', found $($matches.Count)." }
    Copy-Item -LiteralPath $matches[0].FullName -Destination $native -Force
}

Write-Host "Prepared Unity project dependencies for Gua $Version."
