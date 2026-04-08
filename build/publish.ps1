param(
    [string[]]$Runtimes = @('win-x64','win-arm64','linux-x64','linux-arm64','osx-x64','osx-arm64'),
    [switch]$SelfContained,
    [switch]$SingleFile,
    [string]$Configuration = 'Release',
    [string]$Version = '1.0.0',
    [string]$OutputRoot = 'artifacts/publish',
    [string]$ProjectPath = 'PublicVideoLauncher.csproj'
)

$ErrorActionPreference = 'Stop'

$projectFullPath = Join-Path (Get-Location) $ProjectPath
$outputFullRoot = Join-Path (Get-Location) $OutputRoot

New-Item -ItemType Directory -Force -Path $outputFullRoot | Out-Null

foreach ($rid in $Runtimes) {
    $publishDir = Join-Path $outputFullRoot $rid
    New-Item -ItemType Directory -Force -Path $publishDir | Out-Null

    Write-Host "Publishing $rid -> $publishDir"

    $args = @(
        'publish'
        $projectFullPath
        '-c'
        $Configuration
        '-r'
        $rid
        '-o'
        $publishDir
        "-p:Version=$Version"
        '-p:DOTNET_CLI_DISABLE_PUBLISH_AND_PACK_RELEASE=true'
    )

    if ($SelfContained) {
        $args += '--self-contained'
        $args += 'true'
    }
    else {
        $args += '--self-contained'
        $args += 'false'
    }

    if ($SingleFile) {
        $args += '-p:PublishSingleFile=true'
        $args += '-p:IncludeNativeLibrariesForSelfExtract=true'
    }

    & dotnet @args
    if ($LASTEXITCODE -ne 0) {
        throw "dotnet publish failed for $rid"
    }
}