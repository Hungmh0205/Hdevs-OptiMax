# Automatic EXE Build & Packaging Script for Optimax
$Version = "v2.0.0"
$ReleaseName = "Optimax-$Version"
$OutputDir = Join-Path $PSScriptRoot "release"
$ZipFile = Join-Path $PSScriptRoot "$ReleaseName.zip"

Write-Host "==================================================" -ForegroundColor Cyan
Write-Host "  OPTIMAX EXE BUILD & PACKAGING: $ReleaseName" -ForegroundColor Yellow
Write-Host "==================================================" -ForegroundColor Cyan

# 1. Compile C# Elevated Launcher (Start-Optimax.exe)
$cscPath = "C:\Windows\Microsoft.NET\Framework64\v4.0.30319\csc.exe"
$csSource = Join-Path $PSScriptRoot "Start-Optimax.cs"
$exeLauncher = Join-Path $PSScriptRoot "Start-Optimax.exe"

if (Test-Path $csSource) {
    Write-Host " [i] Compiling native Admin launcher (Start-Optimax.exe)..." -ForegroundColor Yellow
    & $cscPath /target:winexe /out:$exeLauncher $csSource | Out-Null
    if (Test-Path $exeLauncher) {
        Write-Host " [v] Start-Optimax.exe compiled successfully!" -ForegroundColor Green
    } else {
        Write-Host " [x] Failed to compile Start-Optimax.exe" -ForegroundColor Red
    }
}

# 2. Clean or create output directory
if (Test-Path $OutputDir) {
    Remove-Item -Path $OutputDir -Recurse -Force -ErrorAction SilentlyContinue
}
New-Item -ItemType Directory -Path $OutputDir -Force | Out-Null

# 3. Remove old zip if exists
if (Test-Path $ZipFile) {
    Remove-Item -Path $ZipFile -Force -ErrorAction SilentlyContinue
}

# 4. Copy files to Release directory
$filesToCopy = @(
    'Start-Optimax.exe',
    'OptimaxServer.exe',
    'Optimax.ps1',
    'server.js',
    'app.js',
    'index.html',
    'style.css',
    'Start-ServerAsAdmin.bat',
    'RunOptimizerOnBoot.vbs',
    'README.md'
)

foreach ($file in $filesToCopy) {
    $src = Join-Path $PSScriptRoot $file
    if (Test-Path $src) {
        Copy-Item -Path $src -Destination $OutputDir -Force
        Write-Host " [v] Added: $file" -ForegroundColor Green
    } else {
        Write-Host " [!] Optional file not present: $file" -ForegroundColor Yellow
    }
}

# 5. Compress to Zip Release archive
Write-Host ""
Write-Host " [i] Creating Zip Release Archive..." -ForegroundColor Yellow
Compress-Archive -Path "$OutputDir\*" -DestinationPath $ZipFile -Force

Write-Host ""
Write-Host "==================================================" -ForegroundColor Green
Write-Host "  OPTIMAX EXE BUILD & ZIP PACKAGING SUCCESSFUL!" -ForegroundColor Green
Write-Host "  Release Archive: $ZipFile" -ForegroundColor Cyan
Write-Host "==================================================" -ForegroundColor Green
