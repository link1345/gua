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
    $godotOutput = @(& $GodotExecutable `
        --headless `
        --path (Join-Path $root "examples/godot-gdscript") `
        --scene "res://GuaSmoke.tscn" 2>&1 | ForEach-Object { $_.ToString() })
    $godotExitCode = $LASTEXITCODE

    if ($godotExitCode -ne 0) {
        $godotOutput | Write-Host
        throw "Godot GDScript smoke failed with exit code $godotExitCode."
    }

    # Some restricted Windows environments deny Godot access to the system CA
    # store. Godot falls back to its bundled Mozilla certificates, and this
    # offline smoke does not use TLS. Remove only that exact engine diagnostic;
    # every other Godot error remains visible and fails the smoke.
    $filteredOutput = [System.Collections.Generic.List[string]]::new()
    for ($index = 0; $index -lt $godotOutput.Count; $index++) {
        if ($godotOutput[$index] -eq "ERROR: Failed to read the root certificate store." -and
            $index + 1 -lt $godotOutput.Count -and
            $godotOutput[$index + 1].Trim() -like "at: get_system_ca_certificates*") {
            $index++
            continue
        }
        $filteredOutput.Add($godotOutput[$index])
    }

    if (-not ($filteredOutput -contains "Gua GDScript smoke passed.")) {
        $godotOutput | Write-Host
        throw "Godot GDScript smoke did not report successful completion."
    }
    if ($filteredOutput | Where-Object { $_ -match "^ERROR:" }) {
        $godotOutput | Write-Host
        throw "Godot GDScript smoke emitted an unexpected error."
    }
    $filteredOutput | Write-Host
}
finally {
    if ($null -eq $previousAppData) {
        Remove-Item Env:APPDATA -ErrorAction SilentlyContinue
    }
    else {
        $env:APPDATA = $previousAppData
    }
}
