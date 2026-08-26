param(
    [string]$GodotExecutable = $env:GODOT_EXECUTABLE
)

$ErrorActionPreference = "Stop"

if ([string]::IsNullOrWhiteSpace($GodotExecutable)) {
    throw "Set GODOT_EXECUTABLE or pass -GodotExecutable with the Godot 4.7 console executable."
}

$root = Split-Path -Parent $PSScriptRoot
$userDataRoot = Join-Path $root "build/godot-smoke-appdata"
New-Item -ItemType Directory -Force $userDataRoot | Out-Null

# Godot 4.7 on Windows can access-violate during startup when its normal APPDATA
# location is unavailable. Keep the smoke's user:// writes in an ignored,
# writable directory and restore the caller's environment afterward.
$previousAppData = $env:APPDATA
try {
    $env:APPDATA = $userDataRoot
    & $GodotExecutable `
        --headless `
        --path (Join-Path $root "examples/godot-gdscript") `
        --scene "res://GuaSmoke.tscn"
    if ($LASTEXITCODE -ne 0) {
        throw "Godot GDScript smoke failed with exit code $LASTEXITCODE."
    }
}
finally {
    if ($null -eq $previousAppData) {
        Remove-Item Env:APPDATA -ErrorAction SilentlyContinue
    }
    else {
        $env:APPDATA = $previousAppData
    }
}
