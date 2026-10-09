<#
.SYNOPSIS
    Builds the freeserf.net Android Release APK or AAB.

.DESCRIPTION
    Canonical build entry point for the Android port. Wraps `dotnet build` with
    all flags required on this machine (see Android.md): single MSBuild node,
    no node reuse, trimming/AOT disabled. The type-registration table
    (libxamarin-app.so) is force-regenerated on every build by the
    ForceFreshTypeRegistration target in FreeserfNet.Android.csproj, so the
    n_onResume/n_loadLibraries UnsatisfiedLinkError (Crash 2) cannot occur.

    Signing: the gitignored FreeserfNet.Android\signing.local.props (see
    signing.local.props.example) is imported automatically when present. Pass
    -KeyStore/-KeyAlias/-KeyPass/-StorePass to override it for a one-off build.
    Without any signing configuration the package is debug-signed and rejected
    by Google Play.

.PARAMETER Clean
    Delete FreeserfNet.Android\bin and FreeserfNet.Android\obj before building
    (full clean rebuild). Only needed when a full rebuild is desired; the
    default incremental build is already safe against Crash 2.

.PARAMETER FreeserfGameDataPath
    Optional path to a SPAE.PA game data file to bundle into the APK. Without
    it the APK ships without game data and the user imports it via the file
    picker on first start.

.PARAMETER PackageFormat
    Package format to build: "apk" (default) or "aab" (Android App Bundle,
    required by Google Play for new apps).

.PARAMETER KeyStore
    Optional path to the signing keystore (.jks/.keystore). Overrides
    signing.local.props.

.PARAMETER KeyAlias
    Optional alias of the signing key. Overrides signing.local.props.

.PARAMETER KeyPass
    Optional password of the signing key. Overrides signing.local.props.

.PARAMETER StorePass
    Optional password of the keystore. Overrides signing.local.props.

.EXAMPLE
    .\build-android.ps1
    .\build-android.ps1 -Clean
    .\build-android.ps1 -FreeserfGameDataPath "D:\freeserf.net\SPAE.PA"
    .\build-android.ps1 -PackageFormat aab
    .\build-android.ps1 -PackageFormat aab -KeyStore "D:\keys\app.jks" -KeyAlias freeserf -KeyPass ... -StorePass ...
#>
param(
    [switch]$Clean,
    [string]$FreeserfGameDataPath = "",
    [ValidateSet("apk", "aab")]
    [string]$PackageFormat = "apk",
    [string]$KeyStore = "",
    [string]$KeyAlias = "",
    [string]$KeyPass = "",
    [string]$StorePass = ""
)

$ErrorActionPreference = "Stop"
$env:MSBUILDDISABLENODEREUSE = 1

$proj = Join-Path $PSScriptRoot "FreeserfNet.Android\FreeserfNet.Android.csproj"
$props = Join-Path $PSScriptRoot "FreeserfNet.Android\signing.local.props"

if ($Clean) {
    Write-Host "Full clean: deleting FreeserfNet.Android\bin and obj..." -ForegroundColor Yellow
    Remove-Item -Recurse -Force (Join-Path $PSScriptRoot "FreeserfNet.Android\bin") -ErrorAction SilentlyContinue
    Remove-Item -Recurse -Force (Join-Path $PSScriptRoot "FreeserfNet.Android\obj") -ErrorAction SilentlyContinue
}

# Signing: either the gitignored signing.local.props file or explicit -Key*
# parameters. Without either, the package is signed with the debug keystore
# and Google Play rejects it.
$hasSigning = (Test-Path $props) -or $KeyStore -or $KeyAlias -or $KeyPass -or $StorePass
if (-not $hasSigning) {
    Write-Host "WARNING: No signing configuration found (signing.local.props or -Key* parameters)." -ForegroundColor Yellow
    Write-Host "         The package will be DEBUG-signed and rejected by Google Play." -ForegroundColor Yellow
    Write-Host "         See FreeserfNet.Android\signing.local.props.example and Android.md." -ForegroundColor Yellow
}

$args = @("build", $proj, "-c", "Release", "-m:1", "-nodeReuse:false",
    "-p:PublishTrimmed=false", "-p:RunAOTCompilation=false",
    "-p:AndroidPackageFormat=$PackageFormat")
if ($FreeserfGameDataPath) {
    $args += "-p:FreeserfGameDataPath=$FreeserfGameDataPath"
}
if ($KeyStore) { $args += "-p:AndroidSigningKeyStore=$KeyStore" }
if ($KeyAlias) { $args += "-p:AndroidSigningKeyAlias=$KeyAlias" }
if ($KeyPass) { $args += "-p:AndroidSigningKeyPass=$KeyPass" }
if ($StorePass) { $args += "-p:AndroidSigningStorePass=$StorePass" }

Write-Host "Building Android $PackageFormat..." -ForegroundColor Cyan
& dotnet @args
if ($LASTEXITCODE -ne 0) {
    Write-Host "Build FAILED (exit code $LASTEXITCODE)." -ForegroundColor Red
    exit $LASTEXITCODE
}

$pkg = Join-Path $PSScriptRoot "FreeserfNet.Android\bin\Release\net10.0-android\net.freeserf.android-Signed.$PackageFormat"
if (Test-Path $pkg) {
    Write-Host "$($PackageFormat.ToUpper()): $pkg" -ForegroundColor Green
    if ($PackageFormat -eq "apk") {
        Write-Host "Smoke test: .\test-android.ps1" -ForegroundColor Green
    } else {
        Write-Host "Upload $pkg to Google Play." -ForegroundColor Green
    }
} else {
    Write-Host "Build succeeded but package not found at expected path: $pkg" -ForegroundColor Yellow
}
