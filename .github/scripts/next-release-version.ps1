param(
    [Parameter(Mandatory = $true)]
    [string]$TagPrefix,

    [string]$RequestedMajor = "",

    [string[]]$LegacyTagPrefixes = @()
)

$ErrorActionPreference = "Stop"
$prefixes = @($TagPrefix) + $LegacyTagPrefixes | Select-Object -Unique
$versions = $prefixes |
    ForEach-Object {
        $prefix = $_
        $tagPattern = "^$([regex]::Escape($prefix))-v(?<major>\d+)\.(?<minor>\d+)\.0$"
        git tag --list "$prefix-v*" |
            ForEach-Object {
                if ($_ -match $tagPattern) {
                    [pscustomobject]@{
                        Tag = $_
                        Major = [int]$Matches.major
                        Minor = [int]$Matches.minor
                    }
                }
            }
    } |
    Sort-Object Major, Minor -Descending

$latest = $versions | Select-Object -First 1

if ([string]::IsNullOrWhiteSpace($RequestedMajor)) {
    if ($null -eq $latest) {
        $major = 0
        $minor = 1
    } else {
        $major = $latest.Major
        $minor = $latest.Minor + 1
    }
} else {
    $parsedMajor = 0
    if (-not [int]::TryParse($RequestedMajor, [ref]$parsedMajor) -or $parsedMajor -lt 1) {
        throw "major must be a positive integer."
    }
    if ($null -ne $latest -and $parsedMajor -le $latest.Major) {
        throw "major must be greater than the latest released major ($($latest.Major))."
    }

    $major = $parsedMajor
    $minor = 0
}

$version = "$major.$minor.0"
"version=$version" >> $env:GITHUB_OUTPUT
"tag=$TagPrefix-v$version" >> $env:GITHUB_OUTPUT

Write-Host "Next $TagPrefix release: v$version"
