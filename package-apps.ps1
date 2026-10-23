param(
    [string]$MacBuildHost = "",
    [string]$MacUser = "",
    [switch]$AndroidOnly,
    [switch]$NoVersionBump
)

$ErrorActionPreference = "Stop"
$ProjectRoot = $PSScriptRoot
$MauiProject = Join-Path $ProjectRoot "src\WeightliftingCompWarmup.Maui\WeightliftingCompWarmup.Maui.csproj"
$PackagesDir = Join-Path $ProjectRoot "packages"
$AndroidOutDir = Join-Path $PackagesDir "android"
$IosOutDir = Join-Path $PackagesDir "ios"

New-Item -ItemType Directory -Force -Path $AndroidOutDir | Out-Null
New-Item -ItemType Directory -Force -Path $IosOutDir | Out-Null

# Read and optionally auto-increment version in WeightliftingCompWarmup.Maui.csproj
$CsprojContent = Get-Content -Path $MauiProject -Raw
$DisplayMatch = [regex]::Match($CsprojContent, '<ApplicationDisplayVersion>(\d+)\.(\d+)\.(\d+)</ApplicationDisplayVersion>')
$CodeMatch = [regex]::Match($CsprojContent, '<ApplicationVersion>(\d+)</ApplicationVersion>')

if (-not $DisplayMatch.Success -or -not $CodeMatch.Success) {
    throw "Could not find <ApplicationDisplayVersion> or <ApplicationVersion> in $MauiProject"
}

$Major = [int]$DisplayMatch.Groups[1].Value
$Minor = [int]$DisplayMatch.Groups[2].Value
$Patch = [int]$DisplayMatch.Groups[3].Value
$VersionCode = [int]$CodeMatch.Groups[1].Value
$OldDisplayVersion = "$Major.$Minor.$Patch"
$OldVersionCode = $VersionCode

if (-not $NoVersionBump) {
    $Patch += 1
    $VersionCode += 1
    $DisplayVersion = "$Major.$Minor.$Patch"

    $CsprojContent = [regex]::Replace(
        $CsprojContent,
        '<ApplicationDisplayVersion>\d+\.\d+\.\d+</ApplicationDisplayVersion>',
        "<ApplicationDisplayVersion>$DisplayVersion</ApplicationDisplayVersion>")
    $CsprojContent = [regex]::Replace(
        $CsprojContent,
        '<ApplicationVersion>\d+</ApplicationVersion>',
        "<ApplicationVersion>$VersionCode</ApplicationVersion>")

    Set-Content -Path $MauiProject -Value $CsprojContent -NoNewline -Encoding UTF8
    Write-Host "=== Bumped Version: $OldDisplayVersion (build $OldVersionCode) -> $DisplayVersion (build $VersionCode) ===" -ForegroundColor Magenta
} else {
    $DisplayVersion = $OldDisplayVersion
    Write-Host "=== Using Current Version: $DisplayVersion (build $VersionCode) ===" -ForegroundColor Magenta
}

Write-Host "=== 1. Packaging Android Release APK (v$DisplayVersion) ===" -ForegroundColor Cyan
dotnet publish $MauiProject -f net10.0-android -c Release -p:AndroidPackageFormat=apk

$SignedApk = Join-Path $ProjectRoot "src\WeightliftingCompWarmup.Maui\bin\Release\net10.0-android\publish\com.weightlifting.compwarmup-Signed.apk"
if (Test-Path $SignedApk) {
    $DestApk = Join-Path $AndroidOutDir "WeightliftingCompWarmup-$DisplayVersion-Signed.apk"
    Copy-Item $SignedApk $DestApk -Force
    Write-Host "Android APK ready: $DestApk" -ForegroundColor Green
}

if (-not $AndroidOnly) {
    Write-Host ""
    Write-Host "=== 2. Packaging iOS Release IPA (v$DisplayVersion) ===" -ForegroundColor Cyan
    if ($IsMacOS) {
        dotnet publish $MauiProject -f net10.0-ios -c Release -p:ArchiveOnBuild=true -p:BuildIpa=true
    } elseif ($MacBuildHost -ne "" -and $MacUser -ne "") {
        dotnet publish $MauiProject -f net10.0-ios -c Release -p:BuildIpa=true -p:ServerAddress=$MacBuildHost -p:ServerUser=$MacUser
    } else {
        Write-Host "Note: Building a native iOS .ipa from Windows requires either:" -ForegroundColor Yellow
        Write-Host "  1. A paired Mac on your network: .\package-apps.ps1 -MacBuildHost <MacIP> -MacUser <Username>" -ForegroundColor Yellow
        Write-Host "  2. Or pushing to GitHub to run the included .github/workflows/package-mobile.yml on macos-latest." -ForegroundColor Yellow
    }
}
