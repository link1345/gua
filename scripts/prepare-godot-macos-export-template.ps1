param(
    [Parameter(Mandatory = $true)]
    [string]$TemplateArchive,

    [Parameter(Mandatory = $true)]
    [ValidateSet("x86_64", "arm64")]
    [string]$Architecture
)

$ErrorActionPreference = "Stop"

if (-not $IsMacOS) {
    throw "Godot macOS export templates can only be prepared on macOS."
}

$archive = (Resolve-Path -LiteralPath $TemplateArchive).Path
$workingDirectory = Join-Path ([System.IO.Path]::GetTempPath()) "gua-godot-macos-template-$([Guid]::NewGuid().ToString('N'))"
$replacementArchive = "$archive.gua.zip"

try {
    New-Item -ItemType Directory -Force $workingDirectory | Out-Null
    Expand-Archive -LiteralPath $archive -DestinationPath $workingDirectory

    foreach ($configuration in @("debug", "release")) {
        $universalName = "godot_macos_$configuration.universal"
        $universal = Get-ChildItem -LiteralPath $workingDirectory -Recurse -File -Filter $universalName | Select-Object -First 1
        if ($null -eq $universal) {
            throw "The official Godot template archive does not contain '$universalName'."
        }

        $slice = Join-Path $universal.DirectoryName "godot_macos_$configuration.$Architecture"
        & /usr/bin/lipo $universal.FullName -thin $Architecture -output $slice
        if ($LASTEXITCODE -ne 0) {
            throw "lipo failed to extract $Architecture from '$universalName'."
        }
        & /usr/bin/lipo $slice -verify_arch $Architecture
        if ($LASTEXITCODE -ne 0) {
            throw "The generated '$slice' does not contain $Architecture."
        }
        chmod +x $slice
    }

    if (Test-Path -LiteralPath $replacementArchive) {
        Remove-Item -LiteralPath $replacementArchive -Force
    }
    Push-Location $workingDirectory
    try {
        & /usr/bin/zip -qry $replacementArchive .
        if ($LASTEXITCODE -ne 0) {
            throw "Failed to repack the Godot macOS export template."
        }
    }
    finally {
        Pop-Location
    }

    Move-Item -LiteralPath $replacementArchive -Destination $archive -Force
    Write-Host "Prepared Godot macOS $Architecture export template at '$archive'."
}
finally {
    if (Test-Path -LiteralPath $replacementArchive) {
        Remove-Item -LiteralPath $replacementArchive -Force
    }
    if (Test-Path -LiteralPath $workingDirectory) {
        Remove-Item -LiteralPath $workingDirectory -Recurse -Force
    }
}
