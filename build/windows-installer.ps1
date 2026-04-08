param(
    [string[]]$Runtimes = @('win-x64','win-arm64'),
    [string]$PublishRoot = 'artifacts/publish',
    [string]$OutputRoot = 'artifacts/releases',
    [string]$AppName = 'PublicVideoLauncher',
    [string]$Version = '1.0.0',
    [string]$Publisher = 'PublicVideoLauncher',
    [string]$AppUrl = 'https://example.com/PublicVideoLauncher',
    [string]$AppId = 'PublicVideoLauncher'
)

$ErrorActionPreference = 'Stop'
New-Item -ItemType Directory -Path $OutputRoot -Force | Out-Null

function Get-IsccPath {
    $candidates = @(
        "${env:ProgramFiles(x86)}\Inno Setup 6\ISCC.exe",
        "$env:ProgramFiles\Inno Setup 6\ISCC.exe"
    )

    foreach ($candidate in $candidates) {
        if ($candidate -and (Test-Path $candidate)) {
            return $candidate
        }
    }

    $command = Get-Command ISCC.exe -ErrorAction SilentlyContinue
    if ($command) {
        return $command.Source
    }

    throw 'ISCC.exe was not found. Install Inno Setup 6 first.'
}

$iscc = Get-IsccPath
$template = Join-Path $PSScriptRoot 'assets/windows-installer.iss.in'
if (-not (Test-Path $template)) {
    throw "Template not found: $template"
}

$repoRoot = Split-Path $PSScriptRoot -Parent
$setupIcon = Join-Path $repoRoot 'Assets/AppIcon.ico'

foreach ($rid in $Runtimes) {
    if ($rid -notlike 'win-*') {
        Write-Host "Skipping non-Windows runtime $rid"
        continue
    }

    $publishDir = Join-Path $PublishRoot $rid
    if (-not (Test-Path $publishDir)) {
        Write-Host "Skipping $($rid): $publishDir not found"
        continue
    }

    $resolvedPublishDir = (Resolve-Path $publishDir).Path

    $tempDir = Join-Path ([IO.Path]::GetTempPath()) ("pvl-installer-" + [guid]::NewGuid().ToString('N'))
    New-Item -ItemType Directory -Path $tempDir -Force | Out-Null

    try {
        $scriptPath = Join-Path $tempDir "$AppName-$rid.iss"
        $outputBasename = "$AppName-$Version-$rid-setup"
        $escapedPublish = $resolvedPublishDir.Replace('\', '\\')
        $escapedOutput = (Resolve-Path $OutputRoot).Path.Replace('\', '\\')
        $content = Get-Content -Raw -Path $template
        $content = $content.Replace('@APP_NAME@', $AppName)
        $content = $content.Replace('@APP_VERSION@', $Version)
        $content = $content.Replace('@APP_PUBLISHER@', $Publisher)
        $content = $content.Replace('@APP_URL@', $AppUrl)
        $content = $content.Replace('@APP_ID@', "$AppId.$rid")
        $content = $content.Replace('@PUBLISH_DIR@', $escapedPublish)
        $content = $content.Replace('@OUTPUT_DIR@', $escapedOutput)

        $architectureAllowed = if ($rid -eq 'win-arm64') { 'arm64' } else { 'x64compatible' }

        $content = $content.Replace('@OUTPUT_BASENAME@', $outputBasename)
        $content = $content.Replace('@ARCH_ALLOWED@', $architectureAllowed)
        $content = $content.Replace('@ARCH_INSTALL_MODE@', $architectureAllowed)

        if (Test-Path $setupIcon) {
            $escapedSetupIcon = (Resolve-Path $setupIcon).Path.Replace('\', '\\')
            $content = $content.Replace('@SETUP_ICON_LINE@', "SetupIconFile=$escapedSetupIcon")
        }
        else {
            $content = $content.Replace('@SETUP_ICON_LINE@', '')
        }

        Set-Content -Path $scriptPath -Value $content -Encoding UTF8

        & $iscc $scriptPath
        if ($LASTEXITCODE -ne 0) {
            throw "ISCC failed for $rid"
        }

        Write-Host "Built installer for $rid"
    }
    finally {
        Remove-Item -Recurse -Force $tempDir -ErrorAction SilentlyContinue
    }
}