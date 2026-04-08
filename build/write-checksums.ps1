param(
    [string]$OutputFile = 'artifacts/SHA256SUMS.txt',
    [string[]]$Targets = @('artifacts/releases', 'artifacts/source')
)

$ErrorActionPreference = 'Stop'

$directory = Split-Path -Parent $OutputFile
if ([string]::IsNullOrWhiteSpace($directory)) {
    $directory = '.'
}

New-Item -ItemType Directory -Path $directory -Force | Out-Null
Set-Content -Path $OutputFile -Value $null

$files = foreach ($target in $Targets) {
    if (Test-Path $target) {
        Get-ChildItem -Path $target -File -Recurse |
            Where-Object { $_.Name -ne 'SHA256SUMS.txt' }
    }
}

foreach ($file in $files | Sort-Object FullName) {
    $hash = (Get-FileHash -Algorithm SHA256 -Path $file.FullName).Hash.ToLowerInvariant()
    $relativePath = [System.IO.Path]::GetRelativePath((Get-Location).Path, $file.FullName).Replace('\', '/')
    Add-Content -Path $OutputFile -Value ("{0}  {1}" -f $hash, $relativePath)
}

Write-Host "Wrote $OutputFile"
