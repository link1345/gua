param(
    [string]$Tag = $env:GITHUB_REF_NAME
)

$ErrorActionPreference = "Stop"

if ($Tag -notmatch '^gua-v(?<version>(?:0|[1-9]\d*)\.(?:0|[1-9]\d*)\.(?:0|[1-9]\d*))$') {
    throw "Release tag must use the gua-vX.Y.Z format; received '$Tag'."
}

$version = $Matches.version
"version=$version" >> $env:GITHUB_OUTPUT
"tag=$Tag" >> $env:GITHUB_OUTPUT

Write-Host "Release version: $version"
