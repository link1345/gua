param(
    [string]$UnityExecutable = $env:UNITY_EXECUTABLE,
    [string]$Configuration = "Release"
)
$ErrorActionPreference = "Stop"
$root = Split-Path -Parent $PSScriptRoot
if ([string]::IsNullOrWhiteSpace($UnityExecutable)) { $UnityExecutable = "C:\Program Files\Unity\Hub\Editor\6000.5.3f1\Editor\Unity.exe" }
$managedVersion = ([xml](Get-Content -LiteralPath (Join-Path $root "Directory.Build.props") -Raw)).Project.PropertyGroup.GuaPackageVersion
$unityVersion = (Get-Content -LiteralPath (Join-Path $root "bindings\unity\package.json") -Raw | ConvertFrom-Json).version
if ($managedVersion -ne $unityVersion) { throw "Unity package version '$unityVersion' does not match Gua package version '$managedVersion'." }
$core = Join-Path $root "bindings\dotnet\src\Gua.Core\Gua.Core.csproj"
$runtime = Join-Path $root "bindings\dotnet\src\Gua.Runtime\Gua.Runtime.csproj"
dotnet build $core -c $Configuration -f netstandard2.1
dotnet build $runtime -c $Configuration -f netstandard2.1
cmake --preset windows-msvc-release
cmake --build --preset windows-msvc-release --config Release --target gua gua-runtime

$plugins = Join-Path $root "examples\unity-smoke\Assets\Plugins\Gua"
New-Item -ItemType Directory -Force (Join-Path $plugins "Managed"), (Join-Path $plugins "x86_64") | Out-Null
Copy-Item (Join-Path $root "bindings\dotnet\src\Gua.Core\bin\$Configuration\netstandard2.1\Gua.Core.dll") (Join-Path $plugins "Managed") -Force
Copy-Item (Join-Path $root "bindings\dotnet\src\Gua.Runtime\bin\$Configuration\netstandard2.1\Gua.Runtime.dll") (Join-Path $plugins "Managed") -Force
$dependencies = @(
    "$env:USERPROFILE\.nuget\packages\microsoft.bcl.asyncinterfaces\10.0.0\lib\netstandard2.1\Microsoft.Bcl.AsyncInterfaces.dll",
    "$env:USERPROFILE\.nuget\packages\system.io.pipelines\10.0.0\lib\netstandard2.0\System.IO.Pipelines.dll",
    "$env:USERPROFILE\.nuget\packages\system.runtime.compilerservices.unsafe\6.1.2\lib\netstandard2.0\System.Runtime.CompilerServices.Unsafe.dll",
    "$env:USERPROFILE\.nuget\packages\system.text.encodings.web\10.0.0\lib\netstandard2.0\System.Text.Encodings.Web.dll",
    "$env:USERPROFILE\.nuget\packages\system.text.json\10.0.0\lib\netstandard2.0\System.Text.Json.dll"
)
foreach ($dependency in $dependencies) {
    if (-not (Test-Path -LiteralPath $dependency)) { throw "Managed Unity dependency is missing: $dependency" }
    Copy-Item -LiteralPath $dependency -Destination (Join-Path $plugins "Managed") -Force
}
Copy-Item (Join-Path $root "build\windows-msvc-release\native\gua-core\Release\gua.dll") (Join-Path $plugins "x86_64") -Force
Copy-Item (Join-Path $root "build\windows-msvc-release\native\gua-runtime\Release\gua_runtime.dll") (Join-Path $plugins "x86_64") -Force

$project = Join-Path $root "examples\unity-smoke"
$log = Join-Path $root "artifacts\unity-compile.log"
New-Item -ItemType Directory -Force (Split-Path $log) | Out-Null
$tmpSettings = Join-Path $project "Assets\TextMesh Pro\Resources\TMP Settings.asset"
if (-not (Test-Path -LiteralPath $tmpSettings)) { & (Join-Path $PSScriptRoot "import-unity-tmp-resources.ps1") -UnityProjectPath $project }
$unityProcess = Start-Process -FilePath $UnityExecutable -ArgumentList @("-batchmode", "-quit", "-projectPath", $project, "-logFile", $log) -WindowStyle Hidden -PassThru -Wait
if ($unityProcess.ExitCode -ne 0) { throw "Unity package compilation failed with exit code $($unityProcess.ExitCode). See $log" }

$artifact = Join-Path $root "artifacts\unity\com.link1345.gua"
if (Test-Path $artifact) { Remove-Item -Recurse -Force $artifact }
New-Item -ItemType Directory -Force (Join-Path $artifact "Runtime\Plugins\Managed"), (Join-Path $artifact "Runtime\Plugins\x86_64"), (Join-Path $artifact "Editor"), (Join-Path $artifact "Documentation~") | Out-Null
Copy-Item (Join-Path $root "bindings\unity\package.json") $artifact
Copy-Item (Join-Path $root "bindings\unity\Documentation~\index.md") (Join-Path $artifact "Documentation~")
Copy-Item (Join-Path $root "bindings\unity\Samples~") $artifact -Recurse
Copy-Item (Join-Path $root "bindings\unity\Runtime\link.xml") (Join-Path $artifact "Runtime")
Copy-Item (Join-Path $root "bindings\unity\Runtime\Bootstrap") (Join-Path $artifact "Runtime\Bootstrap") -Recurse
Copy-Item (Join-Path $plugins "Managed\*.dll") (Join-Path $artifact "Runtime\Plugins\Managed")
Copy-Item (Join-Path $plugins "x86_64\*.dll") (Join-Path $artifact "Runtime\Plugins\x86_64")
Copy-Item (Join-Path $root "scripts\unity-meta\gua.dll.meta") (Join-Path $artifact "Runtime\Plugins\x86_64")
Copy-Item (Join-Path $root "scripts\unity-meta\gua_runtime.dll.meta") (Join-Path $artifact "Runtime\Plugins\x86_64")
Copy-Item (Join-Path $project "Library\ScriptAssemblies\Gua.Unity.dll") (Join-Path $artifact "Runtime\Plugins\Managed")
Copy-Item (Join-Path $project "Library\ScriptAssemblies\Gua.Unity.TMP.dll") (Join-Path $artifact "Runtime\Plugins\Managed")
Copy-Item (Join-Path $project "Library\ScriptAssemblies\Gua.Unity.Editor.dll") (Join-Path $artifact "Editor")
Copy-Item (Join-Path $root "scripts\unity-meta\Gua.Unity.dll.meta") (Join-Path $artifact "Runtime\Plugins\Managed")
Copy-Item (Join-Path $root "scripts\unity-meta\Gua.Unity.TMP.dll.meta") (Join-Path $artifact "Runtime\Plugins\Managed")
Copy-Item (Join-Path $root "scripts\unity-meta\Gua.Unity.Editor.dll.meta") (Join-Path $artifact "Editor")
$tgzStaging = Join-Path $root "artifacts\unity\tgz-staging"
if (Test-Path -LiteralPath $tgzStaging) { Remove-Item -LiteralPath $tgzStaging -Recurse -Force }
New-Item -ItemType Directory -Path $tgzStaging -Force | Out-Null
Copy-Item -LiteralPath $artifact -Destination (Join-Path $tgzStaging "package") -Recurse
$tgz = Join-Path $root "artifacts\unity\com.link1345.gua-$((Get-Content (Join-Path $artifact 'package.json') -Raw | ConvertFrom-Json).version).tgz"
tar -czf $tgz -C $tgzStaging package
if ($LASTEXITCODE -ne 0) { throw "Failed to create Unity UPM archive $tgz" }
Remove-Item -LiteralPath $tgzStaging -Recurse -Force
Write-Host "Unity UPM artifact: $artifact"
Write-Host "Unity UPM archive: $tgz"
