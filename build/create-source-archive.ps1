param(
    [string]$Version = '1.0.0',
    [string]$AppName = 'PublicVideoLauncher',
    [string]$OutputRoot = 'artifacts/source'
)

$ErrorActionPreference = 'Stop'
New-Item -ItemType Directory -Path $OutputRoot -Force | Out-Null
$outFile = Join-Path $OutputRoot ("$AppName-source-$Version.zip")
if (Test-Path $outFile) {
    Remove-Item $outFile -Force
}

$items = Get-ChildItem -Force | Where-Object {
    $_.Name -notin @('bin','obj','artifacts','.tools','.public-video-cache','.git')
}

Compress-Archive -Path $items.FullName -DestinationPath $outFile -Force
Write-Host "Created $outFile"
