param(
    [string]$PublishRoot = 'artifacts/publish',
    [string]$ReleaseRoot = 'artifacts/releases',
    [string]$AppName = 'PublicVideoLauncher',
    [string]$Version = '1.0.0',
    [string[]]$Runtimes
)

$ErrorActionPreference = 'Stop'
New-Item -ItemType Directory -Path $ReleaseRoot -Force | Out-Null
$hashFile = Join-Path $ReleaseRoot 'SHA256SUMS.txt'
Set-Content -Path $hashFile -Value $null

if (-not $Runtimes -or $Runtimes.Count -eq 0) {
    $Runtimes = Get-ChildItem -Path $PublishRoot -Directory | Select-Object -ExpandProperty Name
}

foreach ($rid in $Runtimes) {
    $dir = Join-Path $PublishRoot $rid
    if (-not (Test-Path $dir)) {
        Write-Host "Skipping $($rid): $dir not found"
        continue
    }

    $baseName = "$AppName-$Version-$rid"
    $outFile = Join-Path $ReleaseRoot ($baseName + '.zip')
    if (Test-Path $outFile) {
        Remove-Item $outFile -Force
    }

    Compress-Archive -Path (Join-Path $dir '*') -DestinationPath $outFile -Force
    $hash = (Get-FileHash -Algorithm SHA256 -Path $outFile).Hash.ToLowerInvariant()
    Add-Content -Path $hashFile -Value ("{0}  {1}" -f $hash, (Split-Path $outFile -Leaf))
    Write-Host "Packaged $outFile"
}
