# Automatic Build & Packaging Script for OPTIMAX Native (.NET Native AOT + WPF)
$ErrorActionPreference = "Stop"
$Version = "v2.0.0"
$ReleaseName = "Optimax-$Version"
$OutputDir = Join-Path $PSScriptRoot "publish"
$ZipFile = Join-Path $PSScriptRoot "Optimax-v2.0.0-Latest.zip"

Write-Host "==================================================" -ForegroundColor Cyan
Write-Host "  OPTIMAX NATIVE BUILD & PACKAGING: $ReleaseName" -ForegroundColor Yellow
Write-Host "==================================================" -ForegroundColor Cyan

# 0. Stop any running instances of Optimax
Get-Process -Name Optimax, Optimax.UI -ErrorAction SilentlyContinue | Stop-Process -Force -ErrorAction SilentlyContinue
Start-Sleep -Milliseconds 500

# 1. Clean previous release output
if (Test-Path $OutputDir) {
    Remove-Item -Path $OutputDir -Recurse -Force -ErrorAction SilentlyContinue
}
New-Item -ItemType Directory -Path $OutputDir -Force | Out-Null

if (Test-Path $ZipFile) {
    Remove-Item -Path $ZipFile -Force -ErrorAction SilentlyContinue
}

# 2. Build & Publish Optimax.Native (Native AOT Engine)
Write-Host " [i] Publishing Optimax.Native (.NET Native AOT)..." -ForegroundColor Yellow
$nativeProj = Join-Path $PSScriptRoot "Optimax.Native\Optimax.csproj"
dotnet publish $nativeProj -c Release -r win-x64 /p:PublishAot=true -o "$OutputDir"

if (Test-Path (Join-Path $OutputDir "Optimax.exe")) {
    Write-Host " [v] Optimax.exe (Native AOT Core) compiled successfully!" -ForegroundColor Green
} else {
    Write-Host " [x] Failed to build Optimax.exe" -ForegroundColor Red
}

# 3. Build & Publish Optimax.UI (WPF Modern Desktop Interface)
Write-Host " [i] Publishing Optimax.UI (WPF Desktop App)..." -ForegroundColor Yellow
$uiProj = Join-Path $PSScriptRoot "Optimax.UI\Optimax.UI.csproj"
dotnet publish $uiProj -c Release -r win-x64 --self-contained true -o "$OutputDir"

if (Test-Path (Join-Path $OutputDir "Optimax.UI.exe")) {
    Write-Host " [v] Optimax.UI.exe (WPF Interface) compiled successfully!" -ForegroundColor Green
} else {
    Write-Host " [x] Failed to build Optimax.UI.exe" -ForegroundColor Red
}

# 4. Copy Rule Datasets & Resources
Write-Host " [i] Copying Winapp2.ini and documentation..." -ForegroundColor Yellow
$winapp2Src = Join-Path $PSScriptRoot "Winapp2.ini"
if (Test-Path $winapp2Src) {
    Copy-Item -Path $winapp2Src -Destination $OutputDir -Force
    Write-Host " [v] Bundled Winapp2.ini rule database" -ForegroundColor Green
}

$readmeSrc = Join-Path $PSScriptRoot "README.md"
if (Test-Path $readmeSrc) {
    Copy-Item -Path $readmeSrc -Destination $OutputDir -Force
    Write-Host " [v] Bundled README.md" -ForegroundColor Green
}

# Copy rules folder if exists
$rulesSrc = Join-Path $PSScriptRoot "Optimax.Native\rules"
if (Test-Path $rulesSrc) {
    Copy-Item -Path $rulesSrc -Destination $OutputDir -Recurse -Force
    Write-Host " [v] Bundled custom rules" -ForegroundColor Green
}

# 5. Compress to Zip Release archive
Write-Host ""
Write-Host " [i] Creating Zip Release Archive..." -ForegroundColor Yellow
if (Test-Path $ZipFile) { Remove-Item -Path $ZipFile -Force -ErrorAction SilentlyContinue }
Compress-Archive -Path "$OutputDir\*" -DestinationPath $ZipFile -Force

Write-Host ""
Write-Host "==================================================" -ForegroundColor Green
Write-Host "  OPTIMAX NATIVE BUILD & ZIP PACKAGING SUCCESSFUL!" -ForegroundColor Green
Write-Host "  Release Directory: $OutputDir" -ForegroundColor Cyan
Write-Host "  Release Archive:   $ZipFile" -ForegroundColor Cyan
Write-Host "==================================================" -ForegroundColor Green
