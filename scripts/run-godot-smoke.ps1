param(
    [string]$GodotExecutable = $env:GODOT_EXECUTABLE,
    [TimeSpan]$Timeout = [TimeSpan]::FromMinutes(2)
)

$ErrorActionPreference = "Stop"
$root = Split-Path -Parent $PSScriptRoot
if ([string]::IsNullOrWhiteSpace($GodotExecutable)) {
    $command = Get-Command "Godot_v4.7-stable_win64_console.exe" -ErrorAction SilentlyContinue
    if ($null -eq $command) { $command = Get-Command "godot" -ErrorAction SilentlyContinue }
    if ($null -ne $command) { $GodotExecutable = $command.Source }
}
if ($Timeout -le [TimeSpan]::Zero) { throw "Timeout must be greater than zero." }
if ([string]::IsNullOrWhiteSpace($GodotExecutable) -or -not (Test-Path -LiteralPath $GodotExecutable)) {
    throw "Godot executable was not found. Set GODOT_EXECUTABLE or pass -GodotExecutable."
}

function Stop-ProcessTree([System.Diagnostics.Process]$Process) {
    if ($env:OS -eq "Windows_NT") {
        & (Join-Path $env:SystemRoot "System32\taskkill.exe") /PID $Process.Id /T /F | Out-Null
        if ($LASTEXITCODE -eq 0) { return }
    }
    try { $Process.Kill($true) } catch { Stop-Process -Id $Process.Id -Force -ErrorAction SilentlyContinue }
}

$project = Join-Path $root "examples\godot-gdscript"
$log = Join-Path $root "artifacts\godot-smoke.log"
New-Item -ItemType Directory -Force (Split-Path -Parent $log) | Out-Null

$arguments = @(
    "--disable-crash-handler",
    "--headless",
    "--path", "`"$project`"",
    "--log-file", "`"$log`"",
    "--script", "res://scripts/gua_smoke.gd"
)
$process = Start-Process -FilePath $GodotExecutable -ArgumentList $arguments -NoNewWindow -PassThru
if (-not $process.WaitForExit([int][Math]::Min([int]::MaxValue, $Timeout.TotalMilliseconds))) {
    Stop-ProcessTree $process
    throw "Godot smoke timed out after $Timeout. Log: $log"
}
if ($process.ExitCode -ne 0) { throw "Godot smoke failed with exit code $($process.ExitCode). Log: $log" }

Write-Host "Godot smoke passed. Log: $log"
