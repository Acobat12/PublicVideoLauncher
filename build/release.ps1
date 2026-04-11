param(
    [string[]]$Runtimes = @('win-x64','win-arm64','linux-x64','linux-arm64','osx-x64','osx-arm64'),
    [switch]$SelfContained,
    [switch]$SingleFile,
    [string]$Configuration = 'Release',
    [string]$Version = '1.0.2',
    [string]$AppName = 'PublicVideoLauncher',
    [string]$OutputRoot = 'artifacts/publish',
    [string]$ReleaseRoot = 'artifacts/releases',
    [string]$SourceRoot = 'artifacts/source',
    [switch]$SkipSourceArchive,
    [switch]$SkipWindowsInstaller,
    [switch]$SkipLinuxNativePackages,
    [switch]$SkipMacAppBundles,
    [switch]$SignMacApp,
    [string]$AppleSignIdentity = $env:APPLE_SIGN_IDENTITY,
    [string]$AppleEntitlements = $env:APPLE_ENTITLEMENTS
)

$ErrorActionPreference = 'Stop'

$publishArgs = @{
    Runtimes = $Runtimes
    Configuration = $Configuration
    Version = $Version
    OutputRoot = $OutputRoot
}
if ($SelfContained) { $publishArgs['SelfContained'] = $true }
if ($SingleFile) { $publishArgs['SingleFile'] = $true }

& "$PSScriptRoot/publish.ps1" @publishArgs
& "$PSScriptRoot/package.ps1" -Version $Version -AppName $AppName -PublishRoot $OutputRoot -ReleaseRoot $ReleaseRoot -Runtimes $Runtimes
if (-not $SkipSourceArchive) {
    & "$PSScriptRoot/create-source-archive.ps1" -Version $Version -AppName $AppName -OutputRoot $SourceRoot
}

if (-not $SkipWindowsInstaller -and $IsWindows -and ($Runtimes | Where-Object { $_ -like 'win-*' })) {
    & "$PSScriptRoot/windows-installer.ps1" -Version $Version -AppName $AppName -PublishRoot $OutputRoot -OutputRoot $ReleaseRoot -Runtimes ($Runtimes | Where-Object { $_ -like 'win-*' })
}

if (-not $SkipLinuxNativePackages -and $IsLinux -and ($Runtimes | Where-Object { $_ -like 'linux-*' })) {
    bash "$PSScriptRoot/linux-native-packages.sh" @($Runtimes | Where-Object { $_ -like 'linux-*' })
}

if (-not $SkipMacAppBundles -and $IsMacOS -and ($Runtimes | Where-Object { $_ -like 'osx-*' })) {
    foreach ($rid in @('osx-x64','osx-arm64')) {
        if ($Runtimes -contains $rid) {
            $publishDir = Join-Path $OutputRoot $rid
            if (Test-Path $publishDir) {
                bash "$PSScriptRoot/macos-app-bundle.sh" $publishDir $AppName 'com.example.publicvideolauncher' $Version $ReleaseRoot
                $appBundle = Join-Path $ReleaseRoot ("$AppName.app")
                $appZip = Join-Path $ReleaseRoot ("$AppName-$Version-$rid.app.zip")
                if (Test-Path $appZip) { Remove-Item $appZip -Force }
                if ($SignMacApp -and -not [string]::IsNullOrWhiteSpace($AppleSignIdentity)) {
                    bash "$PSScriptRoot/macos-sign-and-notarize.sh" $appBundle $appZip $AppleSignIdentity $AppleEntitlements
                }
                else {
                    & ditto -c -k --sequesterRsrc --keepParent $appBundle $appZip
                }
                Remove-Item $appBundle -Recurse -Force
            }
        }
    }
}

if ($SkipSourceArchive) {
    & "$PSScriptRoot/write-checksums.ps1" -OutputFile 'artifacts/SHA256SUMS.txt' -Targets @($ReleaseRoot)
}
else {
    & "$PSScriptRoot/write-checksums.ps1" -OutputFile 'artifacts/SHA256SUMS.txt' -Targets @($ReleaseRoot, $SourceRoot)
}

Write-Host "Release assets are in $ReleaseRoot"
Write-Host "Source archives are in $SourceRoot"
