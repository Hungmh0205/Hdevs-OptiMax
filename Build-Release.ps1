<#
.SYNOPSIS
    Automated Production Build & Packaging Script for OPTIMAX (.NET Native AOT + WPF)
.DESCRIPTION
    Builds Optimax.Native (Native AOT Core) and Optimax.UI (WPF), bundles rule sets,
    generates SHA256 integrity checksums, and packages release archives.
#>
param(
    [string]$Version = "release",
    [string]$Architecture = "win-x64",
    [string]$Configuration = "Release",
    [string]$OutputDir = "",
    [switch]$SkipZip = $false
)

$ErrorActionPreference = "Stop"
$ReleaseName = "Optimax-$Version-$Architecture"

if ([string]::IsNullOrWhiteSpace($OutputDir)) {
    $OutputDir = Join-Path $PSScriptRoot "publish"
}

$ZipFile = Join-Path $PSScriptRoot "Optimax-$Version-$Architecture-Latest.zip"
$ChecksumFile = "$ZipFile.sha256"

Write-Host "==================================================" -ForegroundColor Cyan
Write-Host " Building OPTIMAX Production Package: $ReleaseName" -ForegroundColor Cyan
Write-Host " Target Architecture: $Architecture | Config: $Configuration" -ForegroundColor Cyan
Write-Host " Output Directory:    $OutputDir" -ForegroundColor Cyan
Write-Host "==================================================" -ForegroundColor Cyan

# 1. Terminate any running instances of Optimax processes to unlock output files
Write-Host "[INFO] Terminating running Optimax processes..." -ForegroundColor Yellow
try {
    Get-Process -Name "Optimax", "Optimax.UI" -ErrorAction SilentlyContinue | Stop-Process -Force -ErrorAction SilentlyContinue
    wmic process where "name='Optimax.exe' or name='Optimax.UI.exe'" call terminate 2>$null | Out-Null
    Start-Process -FilePath "taskkill.exe" -ArgumentList "/F /IM Optimax.exe /T" -WindowStyle Hidden -Wait -ErrorAction SilentlyContinue
    Start-Process -FilePath "taskkill.exe" -ArgumentList "/F /IM Optimax.UI.exe /T" -WindowStyle Hidden -Wait -ErrorAction SilentlyContinue
    Start-Sleep -Milliseconds 800
} catch {
    Write-Host "[WARN] Process termination warning: $_" -ForegroundColor Yellow
}

# 2. Clean previous release output directory (STRICTLY 'publish')
Write-Host "[INFO] Preparing publish output directory..." -ForegroundColor Yellow
$OutputDir = Join-Path $PSScriptRoot "publish"
$publishReleaseDir = Join-Path $PSScriptRoot "publish_release"

if (Test-Path $publishReleaseDir) {
    Remove-Item -Path $publishReleaseDir -Recurse -Force -ErrorAction SilentlyContinue
}

if (Test-Path $OutputDir) {
    try {
        Remove-Item -Path $OutputDir -Recurse -Force -ErrorAction Stop
    } catch {
        Write-Host "[WARN] Standard process kill failed (elevated processes running). Requesting elevated process termination..." -ForegroundColor Yellow
        try {
            Start-Process powershell -Verb RunAs -ArgumentList "-NoProfile -Command `\"taskkill /F /IM Optimax.exe /T; taskkill /F /IM Optimax.UI.exe /T`\"" -Wait -WindowStyle Hidden -ErrorAction SilentlyContinue
            Start-Sleep -Seconds 1
        } catch { }
        Remove-Item -Path $OutputDir -Recurse -Force -ErrorAction SilentlyContinue
    }
}
New-Item -ItemType Directory -Path $OutputDir -Force | Out-Null

if (Test-Path $ZipFile) { Remove-Item -Path $ZipFile -Force -ErrorAction SilentlyContinue }
if (Test-Path $ChecksumFile) { Remove-Item -Path $ChecksumFile -Force -ErrorAction SilentlyContinue }

# 2.5 Run Automated Unit Tests Gate
Write-Host "[INFO] Running Automated Unit Tests Gate (Optimax.Tests)..." -ForegroundColor Yellow
$testsProj = Join-Path $PSScriptRoot "Optimax.Tests\Optimax.Tests.csproj"
if (Test-Path $testsProj) {
    dotnet test $testsProj -c $Configuration --no-restore
    if ($LASTEXITCODE -ne 0) {
        Write-Host "[ERROR] Unit tests failed! Aborting release build." -ForegroundColor Red
        exit 1
    }
    Write-Host "[SUCCESS] All unit tests passed cleanly!" -ForegroundColor Green
} else {
    Write-Host "[WARN] Optimax.Tests project not found, skipping unit test gate." -ForegroundColor Yellow
}

# 3. Build & Publish Optimax.Native (Native AOT Engine)
Write-Host "[INFO] Publishing Optimax.Native (.NET Native AOT Core)..." -ForegroundColor Yellow
$nativeProj = Join-Path $PSScriptRoot "Optimax.Native\Optimax.csproj"
dotnet publish $nativeProj -c $Configuration -r $Architecture -o "$OutputDir"

$nativeExe = Join-Path $OutputDir "Optimax.exe"
if (Test-Path $nativeExe) {
    Write-Host "[SUCCESS] Compiled Optimax.exe (Native AOT Core)" -ForegroundColor Green
} else {
    Write-Host "[ERROR] Failed to build Optimax.exe" -ForegroundColor Red
    exit 1
}

# 4. Build & Publish Optimax.UI (WPF Desktop App)
Write-Host "[INFO] Publishing Optimax.UI (WPF Desktop App)..." -ForegroundColor Yellow
$uiProj = Join-Path $PSScriptRoot "Optimax.UI\Optimax.UI.csproj"
dotnet publish $uiProj -c $Configuration -r $Architecture --self-contained true -o "$OutputDir"

$uiExe = Join-Path $OutputDir "Optimax.UI.exe"
if (Test-Path $uiExe) {
    Write-Host "[SUCCESS] Compiled Optimax.UI.exe (WPF Interface)" -ForegroundColor Green
} else {
    Write-Host "[ERROR] Failed to build Optimax.UI.exe" -ForegroundColor Red
    exit 1
}

# 5. Copy Rule Datasets & Resources
Write-Host "[INFO] Bundling rule databases, assets, and documentation..." -ForegroundColor Yellow

$winapp2Src = Join-Path $PSScriptRoot "Winapp2.ini"
if (Test-Path $winapp2Src) {
    Copy-Item -Path $winapp2Src -Destination $OutputDir -Force
    Write-Host "[SUCCESS] Bundled Winapp2.ini" -ForegroundColor Green
}

$sqliteDll = Join-Path $PSScriptRoot "sqlite3.dll"
if (Test-Path $sqliteDll) {
    Copy-Item -Path $sqliteDll -Destination $OutputDir -Force
    Write-Host "[SUCCESS] Bundled sqlite3.dll" -ForegroundColor Green
}

$readmeSrc = Join-Path $PSScriptRoot "README.md"
if (Test-Path $readmeSrc) {
    Copy-Item -Path $readmeSrc -Destination $OutputDir -Force
    Write-Host "[SUCCESS] Bundled README.md" -ForegroundColor Green
}

$rulesSrc = Join-Path $PSScriptRoot "Optimax.Native\rules"
$targetRulesDir = Join-Path $OutputDir "rules"
New-Item -ItemType Directory -Path $targetRulesDir -Force | Out-Null
if (Test-Path $rulesSrc) {
    Copy-Item -Path "$rulesSrc\*" -Destination $targetRulesDir -Recurse -Force -ErrorAction SilentlyContinue
    Write-Host "[SUCCESS] Bundled custom rules" -ForegroundColor Green
}

# 6. Compress to Zip Release Archive & Generate Checksum
if (-not $SkipZip) {
    Write-Host "[INFO] Packaging Release Zip Archive..." -ForegroundColor Yellow
    Compress-Archive -Path "$OutputDir\*" -DestinationPath $ZipFile -Force
    
    if (Test-Path $ZipFile) {
        Write-Host "[INFO] Generating SHA256 integrity checksum..." -ForegroundColor Yellow
        $hash = (Get-FileHash -Path $ZipFile -Algorithm SHA256).Hash
        Set-Content -Path $ChecksumFile -Value $hash -Encoding UTF8
        Write-Host "[SUCCESS] SHA256 Hash: $hash" -ForegroundColor Green
    }
}

Write-Host "==================================================" -ForegroundColor Cyan
Write-Host "[SUCCESS] OPTIMAX Production Build Completed!" -ForegroundColor Green
Write-Host " Output Directory: $OutputDir"
if (-not $SkipZip) {
    Write-Host " Zip Archive:      $ZipFile"
    Write-Host " SHA256 Checksum:  $ChecksumFile"
}
Write-Host "==================================================" -ForegroundColor Cyan
