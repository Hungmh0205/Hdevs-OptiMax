# Automatic Build & Packaging Script for OPTIMAX (.NET Native AOT + WPF)
$ErrorActionPreference = "Stop"
$Version = "release"
$ReleaseName = "Optimax-$Version"
$OutputDir = Join-Path $PSScriptRoot "publish"
$ZipFile = Join-Path $PSScriptRoot "Optimax-$Version-Latest.zip"

Write-Host "Building OPTIMAX Release Package: $ReleaseName" -ForegroundColor Cyan
Write-Host "--------------------------------------------------"

# 0. Terminate any running instances of Optimax
Get-Process -Name Optimax, Optimax.UI -ErrorAction SilentlyContinue | Stop-Process -Force -ErrorAction SilentlyContinue
cmd.exe /c "taskkill /F /IM Optimax.exe /T 2>nul & taskkill /F /IM Optimax.UI.exe /T 2>nul & exit 0"
Start-Sleep -Milliseconds 1500

# 1. Clean previous release output
if (Test-Path $OutputDir) {
    Remove-Item -Path $OutputDir -Recurse -Force -ErrorAction SilentlyContinue
}
New-Item -ItemType Directory -Path $OutputDir -Force | Out-Null

if (Test-Path $ZipFile) {
    Remove-Item -Path $ZipFile -Force -ErrorAction SilentlyContinue
}

# 2. Build & Publish Optimax.Native (Native AOT Engine)
Write-Host "[INFO] Publishing Optimax.Native (.NET Native AOT)..." -ForegroundColor Yellow
$nativeProj = Join-Path $PSScriptRoot "Optimax.Native\Optimax.csproj"
dotnet publish $nativeProj -c Release -r win-x64 -o "$OutputDir"

$nativeExe = Join-Path $OutputDir "Optimax.exe"
if (Test-Path $nativeExe) {
    Write-Host "[SUCCESS] Compiled Optimax.exe (Native AOT Core)" -ForegroundColor Green
} else {
    Write-Host "[ERROR] Failed to build Optimax.exe" -ForegroundColor Red
    exit 1
}

# 3. Build & Publish Optimax.UI (WPF Desktop App)
Write-Host "[INFO] Publishing Optimax.UI (WPF Desktop App)..." -ForegroundColor Yellow
$uiProj = Join-Path $PSScriptRoot "Optimax.UI\Optimax.UI.csproj"
dotnet publish $uiProj -c Release -r win-x64 --self-contained true -o "$OutputDir"

$uiExe = Join-Path $OutputDir "Optimax.UI.exe"
if (Test-Path $uiExe) {
    Write-Host "[SUCCESS] Compiled Optimax.UI.exe (WPF Interface)" -ForegroundColor Green
} else {
    Write-Host "[ERROR] Failed to build Optimax.UI.exe" -ForegroundColor Red
    exit 1
}

# 4. Copy Rule Datasets & Resources
Write-Host "[INFO] Bundling rule databases and documentation..." -ForegroundColor Yellow

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
if (Test-Path $rulesSrc) {
    Copy-Item -Path $rulesSrc -Destination $OutputDir -Recurse -Force
    Write-Host "[SUCCESS] Bundled custom rules" -ForegroundColor Green
}

# 5. Compress to Zip Release archive
Write-Host "[INFO] Packaging Release Zip Archive..." -ForegroundColor Yellow
if (Test-Path $ZipFile) { Remove-Item -Path $ZipFile -Force -ErrorAction SilentlyContinue }
Compress-Archive -Path "$OutputDir\*" -DestinationPath $ZipFile -Force

Write-Host "--------------------------------------------------"
Write-Host "[SUCCESS] OPTIMAX Release Package Created Successfully" -ForegroundColor Green
Write-Host "Output Directory: $OutputDir"
Write-Host "Zip Archive:      $ZipFile"
