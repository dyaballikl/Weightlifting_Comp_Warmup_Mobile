param(
    [string]$MacBuildHost = "",
    [string]$MacUser = "",
    [switch]$AndroidOnly,
    [switch]$PlayStoreBundleOnly,
    [switch]$NoVersionBump
)

$ErrorActionPreference = "Stop"
$ProjectRoot = $PSScriptRoot
$MauiProject = Join-Path $ProjectRoot "src\WeightliftingCompWarmup.Maui\WeightliftingCompWarmup.Maui.csproj"
$PackagesDir = Join-Path $ProjectRoot "packages"
$AndroidOutDir = Join-Path $PackagesDir "android"
$PlayStoreOutDir = Join-Path $PackagesDir "playstore"
$IosOutDir = Join-Path $PackagesDir "ios"

New-Item -ItemType Directory -Force -Path $AndroidOutDir | Out-Null
New-Item -ItemType Directory -Force -Path $PlayStoreOutDir | Out-Null
New-Item -ItemType Directory -Force -Path $IosOutDir | Out-Null

$Utf8NoBom = New-Object System.Text.UTF8Encoding($false)

# Read and optionally auto-increment version in WeightliftingCompWarmup.Maui.csproj
$CsprojContent = [System.IO.File]::ReadAllText($MauiProject, [System.Text.Encoding]::UTF8)
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

    [System.IO.File]::WriteAllText($MauiProject, $CsprojContent, $Utf8NoBom)

    # Keep HelpAboutModalPage.cs, README.md, and STORE_LISTING.md synchronized with the bumped version
    $HelpAboutFile = Join-Path $ProjectRoot "src\WeightliftingCompWarmup.Maui\Views\HelpAboutModalPage.cs"
    if (Test-Path $HelpAboutFile) {
        $HelpContent = [System.IO.File]::ReadAllText($HelpAboutFile, [System.Text.Encoding]::UTF8)
        $HelpContent = [regex]::Replace($HelpContent, 'public const string AppVersion = "[^"]+";', "public const string AppVersion = `"$DisplayVersion`";")
        $HelpContent = [regex]::Replace($HelpContent, 'public const string AppBuild = "[^"]+";', "public const string AppBuild = `"$VersionCode`";")
        [System.IO.File]::WriteAllText($HelpAboutFile, $HelpContent, $Utf8NoBom)
    }

    $ReadmeFile = Join-Path $ProjectRoot "README.md"
    if (Test-Path $ReadmeFile) {
        $ReadmeContent = [System.IO.File]::ReadAllText($ReadmeFile, [System.Text.Encoding]::UTF8)
        $ReadmeContent = [regex]::Replace($ReadmeContent, 'version-\d+\.\d+\.\d+%20\(build%20\d+\)', "version-$DisplayVersion%20(build%20$VersionCode)")
        $ReadmeContent = [regex]::Replace($ReadmeContent, '\*\*Current Release:\*\* `v\d+\.\d+\.\d+` \(Build `\d+`\)', "**Current Release:** ``v$DisplayVersion`` (Build ``$VersionCode``)")
        [System.IO.File]::WriteAllText($ReadmeFile, $ReadmeContent, $Utf8NoBom)
    }

    $StoreListingFile = Join-Path $PlayStoreOutDir "STORE_LISTING.md"
    if (Test-Path $StoreListingFile) {
        $ListingContent = [System.IO.File]::ReadAllText($StoreListingFile, [System.Text.Encoding]::UTF8)
        $ListingContent = [regex]::Replace($ListingContent, 'WeightliftingCompWarmup-\d+\.\d+\.\d+-Signed\.aab', "WeightliftingCompWarmup-$DisplayVersion-Signed.aab")
        [System.IO.File]::WriteAllText($StoreListingFile, $ListingContent, $Utf8NoBom)
    }

    $VersionJsonFile = Join-Path $ProjectRoot "version.json"
    if (Test-Path $VersionJsonFile) {
        $VersionJsonContent = [System.IO.File]::ReadAllText($VersionJsonFile, [System.Text.Encoding]::UTF8)
        $VersionJsonContent = [regex]::Replace($VersionJsonContent, '"latestVersion":\s*"[^"]+"', "`"latestVersion`": `"$DisplayVersion`"")
        $VersionJsonContent = [regex]::Replace($VersionJsonContent, '"latestBuild":\s*\d+', "`"latestBuild`": $VersionCode")
        $VersionJsonContent = [regex]::Replace($VersionJsonContent, '"androidLatestVersion":\s*"[^"]+"', "`"androidLatestVersion`": `"$DisplayVersion`"")
        $VersionJsonContent = [regex]::Replace($VersionJsonContent, '"iosLatestVersion":\s*"[^"]+"', "`"iosLatestVersion`": `"$DisplayVersion`"")
        [System.IO.File]::WriteAllText($VersionJsonFile, $VersionJsonContent, $Utf8NoBom)
    }

    Write-Host "=== Bumped Version: $OldDisplayVersion (build $OldVersionCode) -> $DisplayVersion (build $VersionCode) ===" -ForegroundColor Magenta
} else {
    $DisplayVersion = $OldDisplayVersion
    Write-Host "=== Using Current Version: $DisplayVersion (build $VersionCode) ===" -ForegroundColor Magenta
}

# Clear cached MAUI Resizetizer and Android aapt2 flat resource archives so icon/splash changes always take effect
$AndroidObjDir = Join-Path $ProjectRoot "src\WeightliftingCompWarmup.Maui\obj\Release\net10.0-android"
foreach ($subDir in @("resizetizer", "flat", "flata", "res")) {
    $targetDir = Join-Path $AndroidObjDir $subDir
    if (Test-Path $targetDir) {
        Remove-Item -Path $targetDir -Recurse -Force -ErrorAction SilentlyContinue
    }
}

if (-not $PlayStoreBundleOnly) {
    Write-Host "=== 1. Packaging Android Release APK (v$DisplayVersion) ===" -ForegroundColor Cyan
    dotnet publish $MauiProject -f net10.0-android -c Release -p:AndroidPackageFormat=apk

    $SignedApk = Join-Path $ProjectRoot "src\WeightliftingCompWarmup.Maui\bin\Release\net10.0-android\publish\com.weightlifting.compwarmup-Signed.apk"
    if (Test-Path $SignedApk) {
        $DestApk = Join-Path $AndroidOutDir "WeightliftingCompWarmup-$DisplayVersion-Signed.apk"
        Copy-Item $SignedApk $DestApk -Force
        Write-Host "Android APK ready: $DestApk" -ForegroundColor Green
    }
}

$KeystorePath = Join-Path $PlayStoreOutDir "comp-owl-upload.keystore"
$CredsPath = Join-Path $PlayStoreOutDir "KEYSTORE_CREDENTIALS.txt"
if (Test-Path $KeystorePath) {
    Write-Host ""
    Write-Host "=== Packaging Google Play Store Release AAB (v$DisplayVersion, build $VersionCode) ===" -ForegroundColor Cyan
    $CredsRaw = Get-Content -Path $CredsPath -Raw
    $AliasMatch = [regex]::Match($CredsRaw, 'Key Alias\s*:\s*(\S+)')
    $PassMatch = [regex]::Match($CredsRaw, 'Store Password\s*:\s*(\S+)')
    if (-not $AliasMatch.Success -or -not $PassMatch.Success) {
        throw "Could not parse Key Alias or Store Password from $CredsPath"
    }
    $KeyAlias = $AliasMatch.Groups[1].Value.Trim()
    $KeyPass = $PassMatch.Groups[1].Value.Trim()

    dotnet publish $MauiProject -f net10.0-android -c Release `
        -p:AndroidPackageFormat=aab `
        -p:AndroidKeyStore=true `
        -p:AndroidSigningKeyStore="$KeystorePath" `
        -p:AndroidSigningKeyAlias="$KeyAlias" `
        -p:AndroidSigningKeyPass="$KeyPass" `
        -p:AndroidSigningStorePass="$KeyPass"

    $SignedAab = Join-Path $ProjectRoot "src\WeightliftingCompWarmup.Maui\bin\Release\net10.0-android\publish\com.weightlifting.compwarmup-Signed.aab"
    if (Test-Path $SignedAab) {
        $DestAab = Join-Path $PlayStoreOutDir "WeightliftingCompWarmup-$DisplayVersion-Signed.aab"
        Copy-Item $SignedAab $DestAab -Force
        Write-Host "Play Store AAB ready: $DestAab" -ForegroundColor Green
    }
}

if (-not $AndroidOnly -and -not $PlayStoreBundleOnly) {
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
