[CmdletBinding()]
param(
    [string]$Base = "origin/main",
    [ValidateRange(1, 4)]
    [int]$MaxPasses = 4,
    [string]$Model = "gpt-5.6-sol",
    [ValidateSet("high", "xhigh", "max", "ultra")]
    [string]$ReasoningEffort = "max",
    [switch]$Fix,
    [switch]$Sequential
)

$ErrorActionPreference = "Stop"
Set-StrictMode -Version Latest

function Invoke-Git {
    param([Parameter(ValueFromRemainingArguments = $true)][string[]]$Arguments)

    $output = & git @Arguments 2>&1
    if ($LASTEXITCODE -ne 0) {
        throw "git $($Arguments -join ' ') failed:`n$($output -join [Environment]::NewLine)"
    }
    return $output
}

function Get-WorkspaceFingerprint {
    $status = @(Invoke-Git status --porcelain=v1 --untracked-files=all)
    $untracked = @(Invoke-Git ls-files --others --exclude-standard)
    $payload = [System.Collections.Generic.List[string]]::new()
    $payload.AddRange([string[]]$status)
    $payload.AddRange([string[]]@(Invoke-Git diff --binary HEAD))
    $payload.AddRange([string[]]@(Invoke-Git diff --binary --cached HEAD))

    foreach ($path in $untracked) {
        $payload.Add("untracked:$path")
        $payload.AddRange([string[]]@(Invoke-Git hash-object -- $path))
    }

    $bytes = [Text.Encoding]::UTF8.GetBytes(($payload -join "`n"))
    $sha256 = [Security.Cryptography.SHA256]::Create()
    try {
        $hash = $sha256.ComputeHash($bytes)
    }
    finally {
        $sha256.Dispose()
    }
    return ([BitConverter]::ToString($hash)).Replace("-", "")
}

function Invoke-Reviewer {
    param(
        [hashtable]$Reviewer,
        [string]$OutputPath
    )

    if ($Reviewer.Kind -eq "Dedicated") {
        $head = (Invoke-Git rev-parse HEAD | Select-Object -First 1).Trim()
        $mergeBase = (Invoke-Git merge-base HEAD $Base | Select-Object -First 1).Trim()
        $output = @()
        $exitCode = 0
        if ($head -ne $mergeBase) {
            $arguments = @(
                "review", "--base", $Base,
                "-c", "model=`"$Model`"",
                "-c", "model_reasoning_effort=`"$ReasoningEffort`"",
                "-c", 'sandbox_mode="read-only"'
            )
            $output += @(& $script:CodexCommand @arguments 2>&1)
            $exitCode = $LASTEXITCODE
        }
        if ($exitCode -eq 0 -and @(Invoke-Git status --porcelain=v1 --untracked-files=all).Count -gt 0) {
            $output += "`n===== dedicated uncommitted review ====="
            $arguments = @(
                "review", "--uncommitted",
                "-c", "model=`"$Model`"",
                "-c", "model_reasoning_effort=`"$ReasoningEffort`"",
                "-c", 'sandbox_mode="read-only"'
            )
            $output += @(& $script:CodexCommand @arguments 2>&1)
            $exitCode = $LASTEXITCODE
        }
    }
    else {
        $arguments = @(
            "-a", "never",
            "-s", "read-only",
            "-C", $script:RepositoryRoot,
            "exec",
            "-m", $Model,
            "-c", "model_reasoning_effort=`"$ReasoningEffort`"",
            "--ephemeral",
            $Reviewer.Prompt
        )
        $output = @(& $script:CodexCommand @arguments 2>&1)
        $exitCode = $LASTEXITCODE
    }
    $output | Set-Content -LiteralPath $OutputPath -Encoding utf8

    if ($exitCode -ne 0) {
        throw "Reviewer '$($Reviewer.Name)' failed with exit code $exitCode. See $OutputPath"
    }
}

function Invoke-ReviewPass {
    param(
        [int]$Pass,
        [string]$PassDirectory
    )

    $common = @"
Use the project-local gua-bug-hunt skill. Review the entire cumulative diff
against $Base, not merely the latest commit. Inspect committed changes with
git diff $Base...HEAD, staged and unstaged changes with git diff --cached and
git diff, and every path from git ls-files --others --exclude-standard. Remain
read-only. Report only
reproducible, actionable defects with tight file and line references, trigger,
expected versus actual behavior, violated contract, and verification evidence.
Ignore style, speculation, duplicate findings, and unchanged out-of-scope code.
"@

    $reviewers = @(
        @{
            Name = "general"
            Kind = "Dedicated"
        },
        @{
            Name = "protocol-core"
            Kind = "Lane"
            Prompt = "$common`nFocus on protocol/schema, native C ABI, runtime/bridge, ownership, queues, leases, epochs, request correlation, concurrency, cleanup, and consumed-request completion."
        },
        @{
            Name = "engine-adapters"
            Kind = "Lane"
            Prompt = "$common`nFocus on Unity, Godot, and ImGui host mappings, lifecycle teardown, capability and enum translation, device isolation, Action Map reauthorization, and exceptional cleanup."
        },
        @{
            Name = "consumers"
            Kind = "Lane"
            Prompt = "$common`nFocus on MCP, Inspector, WebMCP, recording/replay, shared metadata validation, confirmation gates, sensitive-value handling, timing, cancellation, and schema drift."
        }
    )

    Write-Host "Starting cumulative review pass $Pass against $Base..."
    if ($Sequential) {
        foreach ($reviewer in $reviewers) {
            $path = Join-Path $PassDirectory "$($reviewer.Name).txt"
            Invoke-Reviewer -Reviewer $reviewer -OutputPath $path
            Write-Host "  completed $($reviewer.Name)"
        }
    }
    else {
        $jobs = foreach ($reviewer in $reviewers) {
            $path = Join-Path $PassDirectory "$($reviewer.Name).txt"
            Start-Job -Name $reviewer.Name -ArgumentList @(
                $script:RepositoryRoot,
                $script:CodexCommand,
                $Base,
                $Model,
                $ReasoningEffort,
                $reviewer,
                $path
            ) -ScriptBlock {
                param($RepositoryRoot, $CodexCommand, $Base, $Model, $ReasoningEffort, $Reviewer, $OutputPath)
                Set-Location -LiteralPath $RepositoryRoot
                if ($Reviewer.Kind -eq "Dedicated") {
                    $head = (& git rev-parse HEAD | Select-Object -First 1).Trim()
                    $mergeBase = (& git merge-base HEAD $Base | Select-Object -First 1).Trim()
                    $output = @()
                    $exitCode = 0
                    if ($head -ne $mergeBase) {
                        $arguments = @(
                            "review", "--base", $Base,
                            "-c", "model=`"$Model`"",
                            "-c", "model_reasoning_effort=`"$ReasoningEffort`"",
                            "-c", 'sandbox_mode="read-only"'
                        )
                        $output += @(& $CodexCommand @arguments 2>&1)
                        $exitCode = $LASTEXITCODE
                    }
                    $dirty = @(& git status --porcelain=v1 --untracked-files=all).Count -gt 0
                    if ($exitCode -eq 0 -and $dirty) {
                        $output += "`n===== dedicated uncommitted review ====="
                        $arguments = @(
                            "review", "--uncommitted",
                            "-c", "model=`"$Model`"",
                            "-c", "model_reasoning_effort=`"$ReasoningEffort`"",
                            "-c", 'sandbox_mode="read-only"'
                        )
                        $output += @(& $CodexCommand @arguments 2>&1)
                        $exitCode = $LASTEXITCODE
                    }
                }
                else {
                    $arguments = @(
                        "-a", "never",
                        "-s", "read-only",
                        "-C", $RepositoryRoot,
                        "exec",
                        "-m", $Model,
                        "-c", "model_reasoning_effort=`"$ReasoningEffort`"",
                        "--ephemeral",
                        $Reviewer.Prompt
                    )
                    $output = @(& $CodexCommand @arguments 2>&1)
                    $exitCode = $LASTEXITCODE
                }
                $output | Set-Content -LiteralPath $OutputPath -Encoding utf8
                if ($exitCode -ne 0) {
                    throw "Reviewer '$($Reviewer.Name)' failed with exit code $exitCode. See $OutputPath"
                }
                [pscustomobject]@{ Name = $Reviewer.Name; OutputPath = $OutputPath }
            }
        }

        try {
            $jobs | Wait-Job | Out-Null
            $failures = @($jobs | Where-Object State -eq "Failed")
            foreach ($job in $jobs) {
                Receive-Job -Job $job | Out-Null
            }
            if ($failures.Count -gt 0) {
                throw "Reviewers failed: $($failures.Name -join ', ')"
            }
        }
        finally {
            $jobs | Remove-Job -Force -ErrorAction SilentlyContinue
        }
    }

    return @($reviewers | ForEach-Object { Join-Path $PassDirectory "$($_.Name).txt" })
}

function Invoke-Fixer {
    param(
        [int]$Pass,
        [string[]]$ReportPaths,
        [string]$PassDirectory
    )

    $reportList = $ReportPaths | ForEach-Object { "- $_" }
    $prompt = @"
This is pass $Pass of the Gua local pull-request audit fix phase.

Read AGENTS.md and use the project-local gua-bug-hunt skill. Independently
validate every finding in these read-only reviewer reports:
$($reportList -join "`n")

Reject speculative, duplicate, style-only, unsupported, and out-of-scope
findings. Fix only reproducible findings that are within the cumulative branch
diff against $Base. Preserve unrelated user changes. Run the narrowest relevant
verification plus git diff --check. Do not commit, push, post GitHub comments,
run scripts/run-local-pr-audit.ps1, or spawn another reviewer; the outer script
owns the next audit pass. If no supported finding remains, make no repository
change and say so explicitly.
"@

    $outputPath = Join-Path $PassDirectory "fixer.txt"
    $arguments = @(
        "-a", "never",
        "-s", "workspace-write",
        "-C", $script:RepositoryRoot,
        "exec",
        "-m", $Model,
        "-c", "model_reasoning_effort=`"$ReasoningEffort`"",
        "--ephemeral",
        "-o", $outputPath,
        $prompt
    )
    & $script:CodexCommand @arguments
    if ($LASTEXITCODE -ne 0) {
        throw "Fixer failed with exit code $LASTEXITCODE. See $outputPath"
    }
}

$script:RepositoryRoot = (Invoke-Git rev-parse --show-toplevel | Select-Object -First 1).Trim()
Set-Location -LiteralPath $script:RepositoryRoot
$null = Invoke-Git rev-parse --verify "$Base^{commit}"
$script:CodexCommand = (Get-Command codex -ErrorAction Stop).Source

$branch = (Invoke-Git branch --show-current | Select-Object -First 1).Trim()
$mergeBase = (Invoke-Git merge-base HEAD $Base | Select-Object -First 1).Trim()
$head = (Invoke-Git rev-parse HEAD | Select-Object -First 1).Trim()
$hasLocalChanges = @(Invoke-Git status --porcelain=v1 --untracked-files=all).Count -gt 0
if ($mergeBase -eq $head -and -not $hasLocalChanges) {
    throw "There is no branch or working-tree diff against $Base. Run this from the pull-request branch or its worktree."
}

$runStamp = Get-Date -Format "yyyyMMdd-HHmmss"
$reportRoot = Join-Path ([IO.Path]::GetTempPath()) "gua-local-pr-audit\$runStamp"
$null = New-Item -ItemType Directory -Path $reportRoot -Force

Write-Host "Repository: $script:RepositoryRoot"
Write-Host "Branch:     $branch"
Write-Host "Base:       $Base"
Write-Host "Reports:    $reportRoot"

for ($pass = 1; $pass -le $(if ($Fix) { $MaxPasses } else { 1 }); $pass++) {
    $passDirectory = Join-Path $reportRoot "pass-$pass"
    $null = New-Item -ItemType Directory -Path $passDirectory -Force
    $reports = Invoke-ReviewPass -Pass $pass -PassDirectory $passDirectory

    foreach ($report in $reports) {
        Write-Host "`n===== $([IO.Path]::GetFileNameWithoutExtension($report)) ====="
        Get-Content -LiteralPath $report
    }

    if (-not $Fix) {
        break
    }

    $before = Get-WorkspaceFingerprint
    Invoke-Fixer -Pass $pass -ReportPaths $reports -PassDirectory $passDirectory
    $after = Get-WorkspaceFingerprint
    if ($before -eq $after) {
        Write-Host "No repository changes were made; the audit loop is clean or findings were unsupported."
        break
    }

    if ($pass -eq $MaxPasses) {
        Write-Warning "The repository changed on the final allowed pass. Review the final reports and rerun after validation."
    }
}

Write-Host "Local PR audit finished. Reports: $reportRoot"
