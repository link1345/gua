param(
    [Parameter(Mandatory = $true)]
    [string]$Version,

    [Parameter(Mandatory = $true)]
    [string]$OutputDirectory
)

$ErrorActionPreference = "Stop"
$root = Split-Path -Parent $PSScriptRoot
$project = Join-Path $root "examples/unity-smoke"
$plugins = Join-Path $project "Assets/Plugins/Gua"
$artifact = Join-Path $OutputDirectory "com.link1345.gua"

if (Test-Path -LiteralPath $artifact) { Remove-Item -LiteralPath $artifact -Recurse -Force }
New-Item -ItemType Directory -Force `
    (Join-Path $artifact "Runtime/Plugins/Managed"), `
    (Join-Path $artifact "Runtime/Plugins/x86_64"), `
    (Join-Path $artifact "Editor"), `
    (Join-Path $artifact "Documentation~") | Out-Null

$package = Get-Content -LiteralPath (Join-Path $root "bindings/unity/package.json") -Raw | ConvertFrom-Json
$package.version = $Version
$package | ConvertTo-Json -Depth 10 | Set-Content -LiteralPath (Join-Path $artifact "package.json") -Encoding utf8NoBOM
Copy-Item (Join-Path $root "bindings/unity/Documentation~/index.md") (Join-Path $artifact "Documentation~")
Copy-Item (Join-Path $root "bindings/unity/Samples~") $artifact -Recurse
Copy-Item (Join-Path $root "bindings/unity/Runtime/link.xml") (Join-Path $artifact "Runtime")
Copy-Item (Join-Path $plugins "Managed/*.dll") (Join-Path $artifact "Runtime/Plugins/Managed")
Copy-Item (Join-Path $plugins "x86_64/*.dll") (Join-Path $artifact "Runtime/Plugins/x86_64")
Copy-Item (Join-Path $root "scripts/unity-meta/gua.dll.meta") (Join-Path $artifact "Runtime/Plugins/x86_64")
Copy-Item (Join-Path $root "scripts/unity-meta/gua_runtime.dll.meta") (Join-Path $artifact "Runtime/Plugins/x86_64")

$scriptAssemblies = Join-Path $project "Library/ScriptAssemblies"
foreach ($assembly in "Gua.Unity.dll", "Gua.Unity.Bootstrap.dll", "Gua.Unity.TMP.dll") {
    $source = Join-Path $scriptAssemblies $assembly
    if (-not (Test-Path -LiteralPath $source)) { throw "Unity script assembly is missing: $source" }
    Copy-Item -LiteralPath $source -Destination (Join-Path $artifact "Runtime/Plugins/Managed")
    Copy-Item (Join-Path $root "scripts/unity-meta/$assembly.meta") (Join-Path $artifact "Runtime/Plugins/Managed")
}
$editorAssembly = Join-Path $scriptAssemblies "Gua.Unity.Editor.dll"
if (-not (Test-Path -LiteralPath $editorAssembly)) { throw "Unity editor assembly is missing: $editorAssembly" }
Copy-Item -LiteralPath $editorAssembly -Destination (Join-Path $artifact "Editor")
Copy-Item (Join-Path $root "scripts/unity-meta/Gua.Unity.Editor.dll.meta") (Join-Path $artifact "Editor")

$staging = Join-Path $OutputDirectory "tgz-staging"
if (Test-Path -LiteralPath $staging) { Remove-Item -LiteralPath $staging -Recurse -Force }
New-Item -ItemType Directory -Force $staging | Out-Null
Copy-Item -LiteralPath $artifact -Destination (Join-Path $staging "package") -Recurse
$archive = Join-Path $OutputDirectory "com.link1345.gua-$Version.tgz"
tar -czf $archive -C $staging package
if ($LASTEXITCODE -ne 0) { throw "Failed to create Unity UPM archive $archive" }
Remove-Item -LiteralPath $staging -Recurse -Force

Write-Host "Unity UPM release archive: $archive"
