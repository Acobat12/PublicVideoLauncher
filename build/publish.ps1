param(
    [string[]]$Runtimes = @('win-x64','win-arm64','linux-x64','linux-arm64','osx-x64','osx-arm64'),
    [switch]$SelfContained,
    [switch]$SingleFile,
    [string]$Configuration = 'Release',
    [string]$Project = 'PublicVideoLauncher.csproj',
    [string]$OutputRoot = 'artifacts/publish',
    [string]$Version = '1.0.0'
)

$ErrorActionPreference = 'Stop'

foreach ($rid in $Runtimes) {
    $output = Join-Path $OutputRoot $rid
    $args = @('publish', $Project, '-c', $Configuration, '-r', $rid, '-o', $output, '-p:Version=' + $Version)

    if ($SelfContained) {
        $args += @('--self-contained', 'true')
    }
    else {
        $args += @('--self-contained', 'false')
    }

    if ($SingleFile) {
        $args += '-p:PublishSingleFile=true'
    }

    Write-Host "Publishing $rid -> $output"
    & dotnet @args
}
