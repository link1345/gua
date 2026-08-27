param(
    [string]$BuildDirectory = "",
    [ValidateSet("Debug", "Release")]
    [string]$Configuration = "Debug",
    [switch]$RuntimeOnly,
    [string]$Version = "0.0.0",
    [string]$BuildId = "development",
    [string]$Generator = ""
)

$ErrorActionPreference = "Stop"
$root = Split-Path -Parent $PSScriptRoot
if ([string]::IsNullOrWhiteSpace($BuildDirectory)) {
    $BuildDirectory = "build/web-$($Configuration.ToLowerInvariant())"
}
$build = Join-Path $root $BuildDirectory
$emcmake = Get-Command emcmake.bat -ErrorAction SilentlyContinue
if ($null -eq $emcmake) { $emcmake = Get-Command emcmake -ErrorAction Stop }

$makeProgram = $null
if ([string]::IsNullOrWhiteSpace($Generator)) {
    if ($null -ne (Get-Command ninja -ErrorAction SilentlyContinue)) {
        $Generator = "Ninja"
    } elseif ($null -ne (Get-Command nmake -ErrorAction SilentlyContinue)) {
        $Generator = "NMake Makefiles"
    } else {
        $makeProgram = Get-ChildItem "${env:ProgramFiles}\Microsoft Visual Studio" -Recurse -Filter nmake.exe -ErrorAction SilentlyContinue |
            Where-Object { $_.FullName -match 'Hostx64\\x64' } | Select-Object -First 1
        if ($null -eq $makeProgram) { throw "Neither Ninja nor nmake was found for the Web native build." }
        $Generator = "NMake Makefiles"
    }
}

$configureArguments = @(
    "-S", $root, "-B", $build, "-G", $Generator,
    "-DCMAKE_BUILD_TYPE=$Configuration",
    "-DGUA_BUILD_EXAMPLES=OFF",
    "-DGUA_BUILD_GODOT=$(if ($RuntimeOnly) { 'OFF' } else { 'ON' })",
    "-DGUA_VERSION=$Version",
    "-DGUA_GODOT_PLUGIN_VERSION=$Version",
    "-DGUA_BUILD_ID=$BuildId"
)
if ($null -ne $makeProgram) { $configureArguments += "-DCMAKE_MAKE_PROGRAM=$($makeProgram.FullName)" }

& $emcmake.Source cmake @configureArguments
if ($LASTEXITCODE -ne 0) { throw "Failed to configure the Gua Web native build." }

$targets = @("gua-runtime")
if (-not $RuntimeOnly) { $targets += "gua-godot" }
cmake --build $build --target @targets
if ($LASTEXITCODE -ne 0) { throw "Failed to build the Gua Web native targets." }

Write-Host "Unity WebGL runtime: $(Join-Path $build 'native/gua-runtime/libgua_runtime.a')"
Write-Host "Unity WebGL core: $(Join-Path $build 'native/gua-core/libgua-core.a')"
if (-not $RuntimeOnly) {
    Write-Host "Godot Web extension: $(Join-Path $root 'examples/godot-gdscript/addons/gua/bin/gua_godot.web.debug.wasm32.wasm')"
}
