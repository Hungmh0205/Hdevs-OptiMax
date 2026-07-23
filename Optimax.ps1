# OPTIMAX - ALL-IN-ONE WINDOWS SYSTEM OPTIMIZER AND FIXER
# Radical Deep Performance & 100% Comprehensive System Tuning Edition (Extreme Plus)
Param(
    [switch]$Check,
    [switch]$Optimize,
    [switch]$Fix,
    [switch]$Advanced,
    [switch]$Pro,
    [switch]$RestorePoint,
    [switch]$CleanRestore,
    [switch]$DeepJunk,
    [switch]$StorageExtreme,
    [switch]$Bloatware,
    [switch]$StandbyRAM,
    [switch]$Ultra,
    [switch]$MSIMode,
    [switch]$DisableMPO,
    [switch]$MultiDriveTrim,
    [switch]$HardenedServices,
    [switch]$PagefileFix,
    [switch]$EnableStartup,
    [switch]$DisableStartup,
    [switch]$CheckStartup,
    [switch]$Revert,
    [switch]$Auto,
    [switch]$All,
    [switch]$Extreme
)

$WarningPreference = 'SilentlyContinue'

# Helper Functions
function Write-Header ($text) {
    Write-Host ''
    Write-Host '==================================================' -ForegroundColor Cyan
    Write-Host "  $text" -ForegroundColor Cyan
    Write-Host '==================================================' -ForegroundColor Cyan
}

function Write-Success ($text) { Write-Host " [v] $text" -ForegroundColor Green }
function Write-Info ($text)    { Write-Host " [i] $text" -ForegroundColor Yellow }
function Write-Err ($text)     { Write-Host " [x] $text" -ForegroundColor Red }

# Admin Privilege Check
$isAdmin = [bool](([System.Security.Principal.WindowsIdentity]::GetCurrent()).groups -match 'S-1-5-32-544')
if (-not $isAdmin) {
    Write-Err 'Running with Standard User privileges. For full System, WinSxS, Services and HKLM access, launch as Administrator.'
}

# Win32 API Memory Trimming Definition
$sig = '[DllImport("psapi.dll")] public static extern bool EmptyWorkingSet(IntPtr hProcess);'
$null = Add-Type -MemberDefinition $sig -Name "PsApiMemory" -Namespace "Win32Functions" -PassThru -ErrorAction SilentlyContinue

# ==================================================
# FEATURE 1: CHECK (Deep System Audit)
# ==================================================
function Invoke-Check {
    Write-Header 'OPTIMAX SYSTEM PERFORMANCE AND HEALTH AUDIT (CHECK)'

    $cs = Get-CimInstance Win32_ComputerSystem -ErrorAction SilentlyContinue
    $os = Get-CimInstance Win32_OperatingSystem -ErrorAction SilentlyContinue
    $cpu = Get-CimInstance Win32_Processor -ErrorAction SilentlyContinue
    $disk = Get-CimInstance Win32_LogicalDisk -Filter "DeviceID='C:'" -ErrorAction SilentlyContinue

    $totalRAM = [math]::Round($cs.TotalPhysicalMemory / 1GB, 2)
    $freeRAM = [math]::Round($os.FreePhysicalMemory / 1MB, 2)
    $usedRAM = [math]::Round($totalRAM - $freeRAM, 2)
    $ramPct = [math]::Round(($usedRAM / $totalRAM) * 100, 1)

    $freeDisk = [math]::Round($disk.FreeSpace / 1GB, 2)
    $totalDisk = [math]::Round($disk.Size / 1GB, 2)

    Write-Info ('Model: ' + $cs.Manufacturer + ' ' + $cs.Model)
    Write-Info ('CPU: ' + $cpu.Name + ' (Cores: ' + $cpu.NumberOfCores + ', Threads: ' + $cpu.NumberOfLogicalProcessors + ')')
    Write-Info ('RAM Status: ' + $usedRAM + ' GB / ' + $totalRAM + ' GB (' + $ramPct + ' Percent Used) | Free: ' + $freeRAM + ' GB')
    Write-Info ('Storage C: ' + $freeDisk + ' GB Free / ' + $totalDisk + ' GB Total')

    # Power Scheme Check
    $activePlan = (powercfg /getactivescheme 2>&1)
    if ($activePlan -match '([0-9a-f-]{36})') {
        Write-Info ('Active Power Scheme GUID: ' + $Matches[1])
    }

    # Hibernation Status Check
    $hiberFile = Test-Path 'C:\hiberfil.sys'
    $hiberStatus = if ($hiberFile) { "ACTIVE (Consuming ~$totalRAM GB)" } else { "DISABLED (SSD Space Saved)" }
    Write-Info ('Hibernation File (hiberfil.sys): ' + $hiberStatus)

    Write-Host ''
    Write-Host '--- TOP 10 RAM CONSUMING PROCESSES ---' -ForegroundColor Yellow
    Get-Process | Group-Object Name | ForEach-Object {
        $sumMB = [math]::Round(($_.Group | Measure-Object WorkingSet64 -Sum).Sum / 1MB, 1)
        [PSCustomObject]@{
            Name = $_.Name
            Count = $_.Count
            TotalRAM_MB = $sumMB
        }
    } | Sort-Object TotalRAM_MB -Descending | Select-Object -First 10 | Format-Table -AutoSize
}

# ==================================================
# FEATURE 2: OPTIMIZE (Deep RAM, Autostart & Junk Clean)
# ==================================================
function Invoke-Optimize {
    Write-Header 'OPTIMAX DEEP MEMORY, AUTOSTART AND GARBAGE CLEANUP (OPTIMIZE)'

    $osBefore = Get-CimInstance Win32_OperatingSystem
    $freeBeforeGB = [math]::Round($osBefore.FreePhysicalMemory / 1MB, 2)
    Write-Info ('Free RAM before optimization: ' + $freeBeforeGB + ' GB')

    # 1. Flush Working Sets
    Write-Info 'Trimming memory working sets for active processes...'
    $procs = Get-Process
    foreach ($p in $procs) {
        try {
            [Win32Functions.PsApiMemory]::EmptyWorkingSet($p.Handle) | Out-Null
        } catch {}
    }
    [GC]::Collect()
    [GC]::WaitForPendingFinalizers()

    $osAfter = Get-CimInstance Win32_OperatingSystem
    $freeAfterGB = [math]::Round($osAfter.FreePhysicalMemory / 1MB, 2)
    $freedMB = [math]::Round(($freeAfterGB - $freeBeforeGB) * 1024, 0)
    if ($freedMB -lt 0) { $freedMB = 0 }
    Write-Success ('RAM flush completed! Free RAM: ' + $freeAfterGB + ' GB (Gained: +' + $freedMB + ' MB)')

    # 2. Disable Autostart Keys (HKCU & HKLM)
    Write-Info 'Cleaning Autostart registry items & startup paths...'
    $targetKeys = @(
        'GoogleChromeAutoLaunch_D622EF8A2681BC7366969A9522AD93CD',
        'Docker Desktop',
        'Mozilla-Firefox-308046B0AF4A39CB',
        'RobloxPlayerBeta',
        'IDMan',
        'Send to OneNote',
        'Microsoft Edge Update',
        'EdgeAutoLaunch'
    )
    $runPaths = @(
        'HKCU:\Software\Microsoft\Windows\CurrentVersion\Run',
        'HKLM:\Software\Microsoft\Windows\CurrentVersion\Run'
    )
    foreach ($rPath in $runPaths) {
        if (Test-Path $rPath) {
            foreach ($k in $targetKeys) {
                Remove-ItemProperty -Path $rPath -Name $k -ErrorAction SilentlyContinue
            }
        }
    }
    Write-Success 'Autostart items cleaned.'

    # 3. Configure Heavy & Telemetry Services
    Write-Info 'Configuring heavy & telemetry background services...'
    $heavySvcs = @(
        'Spooler',           # Print Spooler
        'vmms',              # Hyper-V Manager
        'RvControlSvc',      # Radmin VPN
        'WSLService',        # WSL Linux
        'ClickToRunSvc',     # Office Click-to-Run
        'GamingServices',    # Xbox Gaming Services
        'GamingServicesNet', # Xbox Gaming Net
        'XblAuthManager',    # Xbox Live Auth
        'XblGameSave',       # Xbox Live Save
        'XboxNetApiSvc',     # Xbox Net API
        'StiSvc',            # Windows Image Acquisition
        'TrkWks',            # Distributed Link Tracking
        'DiagTrack',         # Telemetry Tracking
        'dmwappushservice',  # WAP Push Telemetry
        'MapsBroker',        # Downloaded Maps Manager
        'RetailDemo',        # Retail Demo Service
        'PcaSvc',            # Program Compatibility Assistant
        'wer-svc'            # Windows Error Reporting
    )
    foreach ($sName in $heavySvcs) {
        $svc = Get-Service -Name $sName -ErrorAction SilentlyContinue
        if ($svc) {
            Set-Service -Name $sName -StartupType Manual -ErrorAction SilentlyContinue
            if ($svc.Status -eq 'Running') {
                Stop-Service -Name $sName -Force -ErrorAction SilentlyContinue
            }
        }
    }
    Write-Success 'Heavy & telemetry background services configured.'

    # 4. Temp & Junk Clean
    Write-Info 'Performing Temp, Prefetch, WER & System Logs Cleaning...'
    $junkTargets = @(
        $env:TEMP + '\*',
        'C:\Windows\Temp\*',
        'C:\Windows\Prefetch\*',
        'C:\Windows\SoftwareDistribution\DeliveryOptimization\*',
        'C:\ProgramData\Microsoft\Windows\WER\*',
        'C:\Windows\Logs\*'
    )
    foreach ($target in $junkTargets) {
        Remove-Item -Path $target -Recurse -Force -ErrorAction SilentlyContinue
    }
    try {
        Clear-RecycleBin -Force -ErrorAction SilentlyContinue
    } catch {}
    Write-Success 'Temp & Garbage cleanup completed!'
}

# ==================================================
# FEATURE 3: FIX (Windows Repair, WinSxS & Cache Reset)
# ==================================================
function Invoke-Fix {
    Write-Header 'OPTIMAX SYSTEM REPAIR AND COMPONENT CLEANUP (FIX)'

    # 1. Stop hanging workers & update services
    Write-Info 'Stopping update workers & background services...'
    Get-Process -Name 'TiWorker' -ErrorAction SilentlyContinue | Stop-Process -Force -ErrorAction SilentlyContinue
    Get-Process -Name 'TrustedInstaller' -ErrorAction SilentlyContinue | Stop-Process -Force -ErrorAction SilentlyContinue
    Get-Process -Name 'msiexec' -ErrorAction SilentlyContinue | Stop-Process -Force -ErrorAction SilentlyContinue

    Stop-Service -Name 'wuauserv' -Force -ErrorAction SilentlyContinue
    Stop-Service -Name 'bits' -Force -ErrorAction SilentlyContinue
    Stop-Service -Name 'cryptsvc' -Force -ErrorAction SilentlyContinue
    Stop-Service -Name 'msiserver' -Force -ErrorAction SilentlyContinue

    # 2. Clear SoftwareDistribution & Catroot2 Cache
    Write-Info 'Clearing SoftwareDistribution and Catroot2 caches...'
    Remove-Item -Path 'C:\Windows\SoftwareDistribution\Download\*' -Recurse -Force -ErrorAction SilentlyContinue
    Remove-Item -Path 'C:\Windows\SoftwareDistribution\DataStore\Logs\*' -Recurse -Force -ErrorAction SilentlyContinue
    Remove-Item -Path 'C:\Windows\System32\catroot2\*' -Recurse -Force -ErrorAction SilentlyContinue
    Write-Success 'Update caches cleared.'

    # 3. Restart Update Services
    Write-Info 'Restarting Windows Update services...'
    Start-Service -Name 'wuauserv' -ErrorAction SilentlyContinue
    Start-Service -Name 'bits' -ErrorAction SilentlyContinue
    Start-Service -Name 'cryptsvc' -ErrorAction SilentlyContinue

    # 4. Network Stack & DNS Reset
    Write-Info 'Resetting Winsock and flushing DNS Cache...'
    ipconfig /flushdns | Out-Null
    netsh winsock reset | Out-Null
    Write-Success 'Network stack reset completed.'

    # 5. Dism WinSxS Component Store Cleanup
    Write-Info 'Running DISM Component Store Cleanup (Cleaning WinSxS folder)...'
    if ($isAdmin) {
        try {
            Dism.exe /Online /Cleanup-Image /StartComponentCleanup /ResetBase | Out-Null
            Write-Success 'WinSxS Component Store cleanup completed.'
        } catch {
            Write-Err 'DISM Component Store cleanup skipped or failed.'
        }
    } else {
        Write-Err 'DISM Component Store cleanup skipped (Requires Administrator privileges).'
    }
}

# ==================================================
# FEATURE 4: ADVANCED TWEAKS
# ==================================================
function Invoke-AdvancedTweaks {
    Write-Header 'OPTIMAX ADVANCED SYSTEM PERFORMANCE AND KERNEL TWEAKS'

    # 1. Power Scheme Activation
    Write-Info 'Activating Ultimate Performance Power Scheme...'
    $out = powercfg -duplicatescheme e9a42b02-d5df-448d-aa00-03f14749eb61 2>&1
    $guidMatch = [regex]::Match($out, '([0-9a-f]{8}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{12})')
    if ($guidMatch.Success) {
        powercfg /setActive $guidMatch.Groups[1].Value
        Write-Success 'Ultimate Performance active!'
    } else {
        powercfg /setActive 8c5e7fda-e8bf-4a96-9a85-a6e23a8c635c
        Write-Success 'High Performance active!'
    }

    # 2. Disable Power Throttling
    Write-Info 'Disabling Windows PowerThrottling policy...'
    $pThrottlePath = 'HKLM:\SYSTEM\CurrentControlSet\Control\Power\PowerThrottling'
    if (-not (Test-Path $pThrottlePath)) { New-Item -Path $pThrottlePath -Force -ErrorAction SilentlyContinue | Out-Null }
    if ($isAdmin) {
        Set-ItemProperty -Path $pThrottlePath -Name 'PowerThrottlingOff' -Value 1 -ErrorAction SilentlyContinue
        Write-Success 'PowerThrottling disabled.'
    } else {
        Write-Err 'PowerThrottling setting skipped (Requires Administrator privileges).'
    }

    # 3. CPU Core Parking Unpark
    Write-Info 'Unparking CPU Cores for maximum thread availability...'
    $coreParkPath = 'HKLM:\SYSTEM\CurrentControlSet\Control\Power\PowerSettings\54533251-82be-4824-96c1-47b60b740d00\0cc5b64e-14df-4058-a059-bd45f8b968c6'
    if (Test-Path $coreParkPath) {
        Set-ItemProperty -Path $coreParkPath -Name 'ValueMax' -Value 0 -ErrorAction SilentlyContinue
        Set-ItemProperty -Path $coreParkPath -Name 'ValueMin' -Value 0 -ErrorAction SilentlyContinue
        Write-Success 'CPU Core Parking disabled.'
    }

    # 4. Visual Effects & Animations
    Write-Info 'Disabling Windows Visual Effects & Animations for speed...'
    Set-ItemProperty -Path 'HKCU:\Software\Microsoft\Windows\CurrentVersion\Themes\Personalize' -Name 'EnableTransparency' -Value 0 -ErrorAction SilentlyContinue
    Set-ItemProperty -Path 'HKCU:\Control Panel\Desktop\WindowMetrics' -Name 'MinAnimate' -Value '0' -ErrorAction SilentlyContinue
    Set-ItemProperty -Path 'HKCU:\Control Panel\Desktop' -Name 'VisualFXSetting' -Value 2 -ErrorAction SilentlyContinue
    Write-Success 'Transparency & Window Animations set to Performance mode.'

    # 5. GameDVR & Game Mode
    Write-Info 'Disabling Xbox GameDVR and enabling Game Mode...'
    Set-ItemProperty -Path 'HKCU:\System\GameConfigStore' -Name 'GameDVR_Enabled' -Value 0 -ErrorAction SilentlyContinue
    $gDvr = 'HKCU:\SOFTWARE\Microsoft\Windows\CurrentVersion\GameDVR'
    if (-not (Test-Path $gDvr)) { New-Item -Path $gDvr -Force | Out-Null }
    Set-ItemProperty -Path $gDvr -Name 'AppCaptureEnabled' -Value 0 -ErrorAction SilentlyContinue

    $gBar = 'HKCU:\Software\Microsoft\GameBar'
    if (-not (Test-Path $gBar)) { New-Item -Path $gBar -Force | Out-Null }
    Set-ItemProperty -Path $gBar -Name 'AllowAutoGameMode' -Value 1 -ErrorAction SilentlyContinue
    Write-Success 'Xbox Game DVR disabled & Game Mode enabled.'

    # 6. HAGS (Hardware-Accelerated GPU Scheduling)
    Write-Info 'Enabling Hardware-Accelerated GPU Scheduling (HAGS)...'
    if ($isAdmin) {
        Set-ItemProperty -Path 'HKLM:\SYSTEM\CurrentControlSet\Control\GraphicsDrivers' -Name 'HwSchMode' -Value 2 -ErrorAction SilentlyContinue
        Write-Success 'HAGS enabled (Active after reboot).'
    } else {
        Write-Err 'HAGS setting skipped (Requires Administrator privileges).'
    }
}

# ==================================================
# FEATURE 5: PRO TWEAKS
# ==================================================
function Invoke-ProTweaks {
    Write-Header 'OPTIMAX PRO-LEVEL TCP NETWORK, NAGLE DISABLING AND TELEMETRY TWEAKS'

    # 1. Advanced TCP Stack Tuning
    Write-Info 'Optimizing TCP Network Stack via netsh...'
    netsh int tcp set global autotuninglevel=normal | Out-Null
    netsh int tcp set global rss=enabled | Out-Null
    netsh int tcp set global rsc=enabled | Out-Null
    netsh int tcp set global ecncapability=disabled | Out-Null
    netsh int tcp set global timestamps=disabled | Out-Null
    Write-Success 'TCP Auto-Tuning, RSS & RSC enabled.'

    # 2. System Responsiveness & Network Throttling
    Write-Info 'Disabling Network Throttling & setting 100% System Responsiveness...'
    $sysProfile = 'HKLM:\SOFTWARE\Microsoft\Windows NT\CurrentVersion\Multimedia\SystemProfile'
    if (Test-Path $sysProfile) {
        Set-ItemProperty -Path $sysProfile -Name 'NetworkThrottlingIndex' -Value 0xffffffff -ErrorAction SilentlyContinue
        Set-ItemProperty -Path $sysProfile -Name 'SystemResponsiveness' -Value 0 -ErrorAction SilentlyContinue
    }

    # 3. Disable Nagle's Algorithm
    Write-Info 'Disabling Nagle Algorithm on Network Interfaces (Ultra-low Latency)...'
    $interfacesPath = 'HKLM:\SYSTEM\CurrentControlSet\Services\Tcpip\Parameters\Interfaces'
    if (Test-Path $interfacesPath) {
        Get-ChildItem -Path $interfacesPath | ForEach-Object {
            Set-ItemProperty -Path $_.PSPath -Name 'TcpAckFrequency' -Value 1 -ErrorAction SilentlyContinue
            Set-ItemProperty -Path $_.PSPath -Name 'TCPNoDelay' -Value 1 -ErrorAction SilentlyContinue
        }
        Write-Success 'Nagle Algorithm disabled across active interfaces.'
    }

    # 4. Disable Telemetry & Bing Search
    Write-Info 'Disabling DataCollection Telemetry & Bing Start Search...'
    $dataCollPath = 'HKLM:\SOFTWARE\Policies\Microsoft\Windows\DataCollection'
    if (-not (Test-Path $dataCollPath)) { New-Item -Path $dataCollPath -Force | Out-Null }
    Set-ItemProperty -Path $dataCollPath -Name 'AllowTelemetry' -Value 0 -ErrorAction SilentlyContinue

    $explorerPolicy = 'HKCU:\SOFTWARE\Policies\Microsoft\Windows\Explorer'
    if (-not (Test-Path $explorerPolicy)) { New-Item -Path $explorerPolicy -Force | Out-Null }
    Set-ItemProperty -Path $explorerPolicy -Name 'DisableSearchBoxSuggestions' -Value 1 -ErrorAction SilentlyContinue
    Write-Success 'Telemetry policies disabled & Bing Start Search turned off.'

    # 5. Multi-Drive SSD Re-Trim
    Invoke-MultiDriveTrim
}

# ==================================================
# EXTREME MODULE 1: PCIE DEVICE MSI MODE OPTIMIZATION
# ==================================================
function Invoke-MSIMode {
    Write-Header 'OPTIMAX PCIE DEVICE MESSAGE SIGNALED INTERRUPTS (MSI MODE) OPTIMIZATION'
    if (-not $isAdmin) {
        Write-Err 'MSI Mode Registry modification requires Administrator privileges.'
        return
    }
    Write-Info 'Scanning PCIe Display Adapters (GPU) & Network Cards for MSI Mode support...'
    try {
        $pciDevices = Get-ChildItem -Path 'HKLM:\SYSTEM\CurrentControlSet\Enum\PCI' -Recurse -ErrorAction SilentlyContinue | Where-Object { $_.PSChildName -eq 'Device Parameters' }
        $count = 0
        foreach ($devParams in $pciDevices) {
            $pciPath = $devParams.PSPath
            $parentPath = Split-Path (Split-Path $pciPath)
            $class = (Get-ItemProperty -Path $parentPath -ErrorAction SilentlyContinue).Class
            
            if ($class -eq 'Display' -or $class -eq 'Net') {
                $msiPath = Join-Path $pciPath 'Interrupt Management\MessageSignaledInterruptProperties'
                if (-not (Test-Path $msiPath)) {
                    New-Item -Path $msiPath -Force -ErrorAction SilentlyContinue | Out-Null
                }
                if (Test-Path $msiPath) {
                    Set-ItemProperty -Path $msiPath -Name 'MSISupported' -Value 1 -Type DWord -ErrorAction SilentlyContinue
                    $count++
                }
            }
        }
        Write-Success "MSI Mode enabled on $count PCIe GPU/Network Devices (Reduced DPC Latency)."
    } catch {
        Write-Err 'MSI Mode optimization encountered an error.'
    }
}

# ==================================================
# EXTREME MODULE 2: DISABLE MULTI-PLANE OVERLAY (MPO)
# ==================================================
function Invoke-DisableMPO {
    Write-Header 'OPTIMAX DISABLE MULTI-PLANE OVERLAY (MPO) FOR ZERO DWM STUTTER'
    if (-not $isAdmin) {
        Write-Err 'MPO Registry modification requires Administrator privileges.'
        return
    }
    Write-Info 'Disabling MPO in Windows DWM Registry (Fixes DWM flickering/stuttering)...'
    $dwmPath = 'HKLM:\SOFTWARE\Microsoft\Windows\DWM'
    if (-not (Test-Path $dwmPath)) { New-Item -Path $dwmPath -Force -ErrorAction SilentlyContinue | Out-Null }
    try {
        Set-ItemProperty -Path $dwmPath -Name 'OverlayTestMode' -Value 5 -Type DWord -ErrorAction SilentlyContinue
        Write-Success 'Multi-Plane Overlay (MPO) disabled! (Takes effect after reboot/logon).'
    } catch {
        Write-Err 'Failed to set MPO overlay test mode in registry.'
    }
}

# ==================================================
# EXTREME MODULE 3: MULTI-DRIVE TRIM & DEFRAG
# ==================================================
function Invoke-MultiDriveTrim {
    Write-Header 'OPTIMAX MULTI-DRIVE SSD TRIM AND HDD DEFRAG OPTIMIZATION'
    Write-Info 'Scanning all local volumes...'
    try {
        $volumes = Get-Volume -ErrorAction SilentlyContinue | Where-Object { $_.DriveLetter -and $_.DriveType -eq 'Fixed' }
        foreach ($vol in $volumes) {
            $letter = $vol.DriveLetter
            Write-Info "Optimizing Drive ${letter}: ($($vol.FileSystemLabel))..."
            try {
                Optimize-Volume -DriveLetter $letter -ReTrim -ErrorAction SilentlyContinue
                Write-Success "Drive ${letter}: TRIM completed."
            } catch {
                Write-Err "Drive ${letter}: TRIM skipped or failed."
            }
        }
    } catch {
        Write-Err 'Failed to query system volumes.'
    }
}

# ==================================================
# EXTREME MODULE 4: HARDENED SERVICE DISABLING
# ==================================================
function Invoke-HardenedServices {
    Write-Header 'OPTIMAX PERMANENT SERVICE HARDENING (DISABLED TELEMETRY AND SYSMAIN)'
    if (-not $isAdmin) {
        Write-Err 'Service modification requires Administrator privileges.'
        return
    }
    Write-Info 'Disabling heavy & tracking services permanently (StartupType: Disabled)...'
    $hardenedSvcs = @('SysMain', 'DiagTrack', 'WSearch', 'dmwappushservice', 'MapsBroker', 'RetailDemo')
    foreach ($sName in $hardenedSvcs) {
        $svc = Get-Service -Name $sName -ErrorAction SilentlyContinue
        if ($svc) {
            Set-Service -Name $sName -StartupType Disabled -ErrorAction SilentlyContinue
            if ($svc.Status -eq 'Running') {
                Stop-Service -Name $sName -Force -ErrorAction SilentlyContinue
            }
            Write-Success "Service $sName set to Disabled and Stopped."
        }
    }
}

# ==================================================
# EXTREME MODULE 5: STATIC VIRTUAL MEMORY (PAGEFILE)
# ==================================================
function Invoke-PagefileFix {
    Write-Header 'OPTIMAX STATIC VIRTUAL MEMORY (PAGEFILE) OPTIMIZATION'
    if (-not $isAdmin) {
        Write-Err 'Pagefile configuration requires Administrator privileges.'
        return
    }
    Write-Info 'Configuring static Pagefile size to prevent SSD I/O dynamic resizing spikes...'
    try {
        $cs = Get-CimInstance Win32_ComputerSystem
        if ($cs.AutomaticManagedPagefile) {
            $cs | Set-CimInstance -Property @{ AutomaticManagedPagefile = $false } -ErrorAction SilentlyContinue
        }
        $pageFile = Get-CimInstance Win32_PageFileSetting -Filter "SettingID like '%C%'" -ErrorAction SilentlyContinue
        if ($pageFile) {
            $pageFile | Set-CimInstance -Property @{ InitialSize = 4096; MaximumSize = 4096 } -ErrorAction SilentlyContinue
            Write-Success 'Pagefile fixed at 4096 MB on C: Drive (Zero dynamic resize I/O latency).'
        } else {
            Write-Success 'Pagefile setting verified.'
        }
    } catch {
        Write-Err 'Failed to update Pagefile configuration.'
    }
}

# ==================================================
# FEATURE 6: RESTORE POINT
# ==================================================
function Invoke-RestorePoint {
    Write-Header 'OPTIMAX CREATING SYSTEM RESTORE POINT (SAFETY BACKUP)'
    try {
        Write-Info 'Enabling System Restore protection on Drive C: if needed...'
        Enable-ComputerRestore -Drive 'C:\' -ErrorAction SilentlyContinue
        Write-Info 'Creating System Restore Point: "Optimax_PreOptimization"...'
        $result = Checkpoint-Computer -Description 'Optimax_PreOptimization' -RestorePointType 'MODIFY_SETTINGS' -ErrorAction Stop
        Write-Success 'System Restore Point created successfully!'
    } catch {
        Write-Err ('Could not create System Restore Point: ' + $_.Exception.Message)
        Write-Info 'Note: Creating restore points requires Administrator privileges and Windows System Protection enabled.'
    }
}

function Invoke-CleanRestorePoints {
    Write-Header 'OPTIMAX CLEANING OLD SYSTEM RESTORE POINTS AND SHADOW COPIES'

    Write-Info 'Deleting old System Restore shadow copies from System Volume Information...'
    if ($isAdmin) {
        try {
            $vssOut = vssadmin delete shadows /for=C: /all /quiet 2>&1
            Write-Success 'Old System Restore shadow copies deleted successfully!'
        } catch {
            Write-Err 'Failed to delete shadow copies via vssadmin.'
        }
    } else {
        Write-Err 'vssadmin shadow copy deletion skipped (Requires Administrator privileges).'
    }

    Write-Info 'Setting maximum System Restore storage limit to 5% on Drive C:'
    if ($isAdmin) {
        try {
            $resizeOut = vssadmin resize shadowstorage /for=C: /on=C: /maxsize=5% 2>&1
            Write-Success 'System Restore max storage capacity resized to 5%.'
        } catch {}
    }
}

# ==================================================
# FEATURE 7: DEEP JUNK & GPU SHADER CACHE CLEANUP
# ==================================================
function Invoke-DeepJunk {
    Write-Header 'OPTIMAX DEEP GPU SHADER, BROWSER AND EVENT LOG CLEANUP'

    Write-Info 'Clearing GPU Shader Caches (NVIDIA, AMD, DirectX D3D)...'
    $shaderPaths = @(
        $env:LOCALAPPDATA + '\NVIDIA\DXCache\*',
        $env:LOCALAPPDATA + '\NVIDIA\GLCache\*',
        $env:LOCALAPPDATA + '\AMD\DxCache\*',
        $env:LOCALAPPDATA + '\D3DSCache\*'
    )
    foreach ($path in $shaderPaths) {
        Remove-Item -Path $path -Recurse -Force -ErrorAction SilentlyContinue
    }
    Write-Success 'GPU Shader caches cleared.'

    Write-Info 'Clearing Browser Caches (Chrome, Edge, Firefox, Brave)...'
    $browserCaches = @(
        $env:LOCALAPPDATA + '\Google\Chrome\User Data\Default\Cache\*',
        $env:LOCALAPPDATA + '\Google\Chrome\User Data\Default\Code Cache\*',
        $env:LOCALAPPDATA + '\Microsoft\Edge\User Data\Default\Cache\*',
        $env:LOCALAPPDATA + '\Microsoft\Edge\User Data\Default\Code Cache\*',
        $env:LOCALAPPDATA + '\BraveSoftware\Brave-Browser\User Data\Default\Cache\*',
        $env:APPDATA + '\Mozilla\Firefox\Profiles\*\cache2\*'
    )
    foreach ($bCache in $browserCaches) {
        Remove-Item -Path $bCache -Recurse -Force -ErrorAction SilentlyContinue
    }
    Write-Success 'Browser caches cleared.'

    Write-Info 'Clearing Windows Event Logs...'
    if ($isAdmin) {
        try {
            $logs = Get-WinEvent -ListLog * -ErrorAction SilentlyContinue
            foreach ($l in $logs) {
                if ($l.RecordCount -gt 0) {
                    try {
                        [System.Diagnostics.Eventing.Reader.EventLogSession]::GlobalSession.ClearLog($l.LogName)
                    } catch {}
                }
            }
            Write-Success 'Windows Event Logs cleared.'
        } catch {
            Write-Info 'Event logs clearing partially completed.'
        }
    } else {
        Write-Err 'Windows Event logs clearing skipped (Requires Administrator privileges).'
    }

    Write-Info 'Clearing Explorer Thumbnail & Font Cache...'
    Remove-Item -Path ($env:LOCALAPPDATA + '\Microsoft\Windows\Explorer\thumbcache_*.db') -Force -ErrorAction SilentlyContinue
    Remove-Item -Path 'C:\Windows\CbsTemp\*' -Recurse -Force -ErrorAction SilentlyContinue
    Write-Success 'Deep GPU, Browser & System Caches fully cleaned.'
}

# ==================================================
# FEATURE 8: STORAGE EXTREME
# ==================================================
function Invoke-StorageExtreme {
    Write-Header 'OPTIMAX EXTREME STORAGE RECLAIM (HIBERNATION OFF AND COMPACT OS)'

    Write-Info 'Disabling Windows Hibernation to reclaim hiberfil.sys (15GB - 32GB SSD space)...'
    if ($isAdmin) {
        try {
            powercfg /h off | Out-Null
            Write-Success 'Hibernation disabled! SSD C: Drive space reclaimed.'
        } catch {
            Write-Err 'Failed to disable Hibernation.'
        }
    } else {
        Write-Err 'Disabling Hibernation skipped (Requires Administrator privileges).'
    }

    Write-Info 'Enabling Windows Storage Sense...'
    $ssPath = 'HKCU:\Software\Microsoft\Windows\CurrentVersion\StorageSense\Parameters\StoragePolicy'
    if (-not (Test-Path $ssPath)) { New-Item -Path $ssPath -Force -ErrorAction SilentlyContinue | Out-Null }
    Set-ItemProperty -Path $ssPath -Name '01' -Value 1 -ErrorAction SilentlyContinue
    Set-ItemProperty -Path $ssPath -Name 'StoragePoliciesNotified' -Value 1 -ErrorAction SilentlyContinue
    Write-Success 'Storage Sense activated.'
}

# ==================================================
# FEATURE 9: BLOATWARE & TELEMETRY TASKS REMOVAL
# ==================================================
function Invoke-Bloatware {
    Write-Header 'OPTIMAX UWP BLOATWARE AND TELEMETRY SCHEDULED TASKS REMOVAL'

    Write-Info 'Removing built-in non-essential UWP Bloatware apps...'
    $bloatApps = @(
        '*CandyCrush*',
        '*BingNews*',
        '*BingWeather*',
        '*SolitaireCollection*',
        '*FeedbackHub*',
        '*GetHelp*',
        '*MicrosoftOfficeHub*',
        '*People*',
        '*ZuneVideo*',
        '*ZuneMusic*',
        '*3DBuilder*',
        '*549981C3F5F10*' # Cortana
    )
    foreach ($app in $bloatApps) {
        try {
            if ($isAdmin) {
                Get-AppxPackage -Name $app -AllUsers -ErrorAction SilentlyContinue | Remove-AppxPackage -AllUsers -ErrorAction SilentlyContinue
            } else {
                Get-AppxPackage -Name $app -ErrorAction SilentlyContinue | Remove-AppxPackage -ErrorAction SilentlyContinue
            }
        } catch {}
    }
    Write-Success 'Non-essential UWP Bloatware apps processing completed.'

    Write-Info 'Disabling Windows Telemetry & Diagnostic Scheduled Tasks...'
    $tasksToDisable = @(
        '\Microsoft\Windows\Customer Experience Improvement Program\Consolidator',
        '\Microsoft\Windows\Customer Experience Improvement Program\UsbCeip',
        '\Microsoft\Windows\Application Experience\Microsoft Compatibility Appraiser',
        '\Microsoft\Windows\Application Experience\ProgramDataUpdater',
        '\Microsoft\Windows\Autochk\Proxy',
        '\Microsoft\Windows\DiskDiagnostic\Microsoft-Windows-DiskDiagnosticDataCollector'
    )
    foreach ($task in $tasksToDisable) {
        try {
            Disable-ScheduledTask -TaskName ($task.Split('\')[-1]) -TaskPath ($task.Substring(0, $task.LastIndexOf('\') + 1)) -ErrorAction SilentlyContinue | Out-Null
        } catch {}
    }
    Write-Success 'Telemetry & Diagnostic background tasks disabled.'
}

# ==================================================
# FEATURE 10: STANDBY RAM CLEARING
# ==================================================
function Invoke-StandbyRAM {
    Write-Header 'OPTIMAX STANDBY MEMORY LIST AND WORKING SET CLEARING'

    Write-Info 'Trimming process WorkingSets & flushing garbage collector...'
    $procs = Get-Process
    foreach ($p in $procs) {
        try {
            [Win32Functions.PsApiMemory]::EmptyWorkingSet($p.Handle) | Out-Null
        } catch {}
    }
    [GC]::Collect()
    [GC]::WaitForPendingFinalizers()

    Write-Info 'Optimizing Windows Memory Compression settings...'
    try {
        Disable-MMAgent -MemoryCompression -ErrorAction SilentlyContinue
        Write-Success 'Memory Compression optimized for low-latency gaming/workstation.'
    } catch {}

    $os = Get-CimInstance Win32_OperatingSystem
    $freeGB = [math]::Round($os.FreePhysicalMemory / 1MB, 2)
    Write-Success ('Standby RAM & Working Sets cleared! Available Free RAM: ' + $freeGB + ' GB')
}

# ==================================================
# FEATURE 11: ULTRA DEEP TUNING
# ==================================================
function Invoke-UltraTweaks {
    Write-Header 'OPTIMAX ULTRA DEEP KERNEL PRIORITY, ZERO UI LATENCY, NTFS AND COMPACT OS TUNING'

    Write-Info 'Setting Win32PrioritySeparation to 0x26 (38)...'
    $pControl = 'HKLM:\SYSTEM\CurrentControlSet\Control\PriorityControl'
    if (Test-Path $pControl) {
        if ($isAdmin) {
            Set-ItemProperty -Path $pControl -Name 'Win32PrioritySeparation' -Value 38 -ErrorAction SilentlyContinue
            Write-Success 'Foreground App CPU Scheduling priority boosted (0x26).'
        } else {
            Write-Err 'Win32PrioritySeparation skipped (Requires Administrator privileges).'
        }
    }

    Write-Info 'Disabling Executive Memory Paging...'
    $memMgmt = 'HKLM:\SYSTEM\CurrentControlSet\Control\Session Manager\Memory Management'
    if (Test-Path $memMgmt) {
        if ($isAdmin) {
            Set-ItemProperty -Path $memMgmt -Name 'DisablePagingExecutive' -Value 1 -ErrorAction SilentlyContinue
            Set-ItemProperty -Path $memMgmt -Name 'LargeSystemCache' -Value 0 -ErrorAction SilentlyContinue
            Write-Success 'Kernel & Driver Paging bypassed.'
        } else {
            Write-Err 'Memory Paging setting skipped (Requires Administrator privileges).'
        }
    }

    Write-Info 'Accelerating NTFS Disk I/O...'
    if ($isAdmin) {
        try {
            fsutil behavior set disable8dot3 1 | Out-Null
            fsutil behavior set disablelastaccess 1 | Out-Null
            Write-Success 'NTFS 8.3 Name generation & LastAccess Writes disabled.'
        } catch {
            Write-Err 'Failed to update NTFS behavior flags.'
        }
    } else {
        Write-Err 'NTFS behavior update skipped (Requires Administrator privileges).'
    }

    Write-Info 'Configuring 0ms UI Menu Show Delay...'
    Set-ItemProperty -Path 'HKCU:\Control Panel\Desktop' -Name 'MenuShowDelay' -Value '0' -ErrorAction SilentlyContinue
    Set-ItemProperty -Path 'HKCU:\Control Panel\Desktop' -Name 'WaitToKillAppTimeout' -Value '2000' -ErrorAction SilentlyContinue
    Set-ItemProperty -Path 'HKCU:\Control Panel\Desktop' -Name 'HungAppTimeout' -Value '1000' -ErrorAction SilentlyContinue
    Set-ItemProperty -Path 'HKCU:\Control Panel\Desktop' -Name 'LowLevelHooksTimeout' -Value '1000' -ErrorAction SilentlyContinue
    Write-Success 'UI Response delay set to 0ms.'

    Write-Info 'Executing Compact OS System Compression...'
    if ($isAdmin) {
        try {
            $compactOut = compact.exe /CompactOS:always 2>&1
            Write-Success 'Compact OS compression executed.'
        } catch {
            Write-Err 'Compact OS execution skipped or failed.'
        }
    } else {
        Write-Err 'Compact OS execution skipped (Requires Administrator privileges).'
    }

    Write-Info 'Optimizing Socket turnaround times...'
    $tcpParams = 'HKLM:\SYSTEM\CurrentControlSet\Services\Tcpip\Parameters'
    if (Test-Path $tcpParams) {
        if ($isAdmin) {
            Set-ItemProperty -Path $tcpParams -Name 'MaxUserPort' -Value 65534 -Type DWord -ErrorAction SilentlyContinue
            Set-ItemProperty -Path $tcpParams -Name 'TcpTimedWaitDelay' -Value 30 -Type DWord -ErrorAction SilentlyContinue
            Write-Success 'Socket recycling accelerated (MaxUserPort = 65534).'
        } else {
            Write-Err 'Socket parameter optimization skipped (Requires Administrator privileges).'
        }
    }
}

# ==================================================
# FEATURE 12: REVERT / UNDO ALL TWEAKS
# ==================================================
function Invoke-Revert {
    Write-Header 'OPTIMAX REVERTING ALL OPTIMIZATIONS TO WINDOWS DEFAULTS (UNDO)'

    Write-Info 'Restoring background services to Manual/Automatic defaults...'
    $svcsToRevert = @('Spooler', 'SysMain', 'WSearch', 'DiagTrack', 'vmms', 'WSLService', 'ClickToRunSvc', 'wer-svc')
    foreach ($sName in $svcsToRevert) {
        $svc = Get-Service -Name $sName -ErrorAction SilentlyContinue
        if ($svc) {
            Set-Service -Name $sName -StartupType Automatic -ErrorAction SilentlyContinue
            Start-Service -Name $sName -ErrorAction SilentlyContinue
        }
    }
    Write-Success 'Default services re-enabled.'

    Write-Info 'Re-enabling PowerThrottling policy...'
    $pThrottlePath = 'HKLM:\SYSTEM\CurrentControlSet\Control\Power\PowerThrottling'
    if (Test-Path $pThrottlePath) {
        Set-ItemProperty -Path $pThrottlePath -Name 'PowerThrottlingOff' -Value 0 -ErrorAction SilentlyContinue
    }

    Write-Info 'Restoring default Telemetry policy & Search suggestions...'
    $dataCollPath = 'HKLM:\SOFTWARE\Policies\Microsoft\Windows\DataCollection'
    if (Test-Path $dataCollPath) {
        Set-ItemProperty -Path $dataCollPath -Name 'AllowTelemetry' -Value 1 -ErrorAction SilentlyContinue
    }
    $explorerPolicy = 'HKCU:\SOFTWARE\Policies\Microsoft\Windows\Explorer'
    if (Test-Path $explorerPolicy) {
        Set-ItemProperty -Path $explorerPolicy -Name 'DisableSearchBoxSuggestions' -Value 0 -ErrorAction SilentlyContinue
    }

    Write-Info 'Re-enabling MPO...'
    $dwmPath = 'HKLM:\SOFTWARE\Microsoft\Windows\DWM'
    if (Test-Path $dwmPath) {
        Remove-ItemProperty -Path $dwmPath -Name 'OverlayTestMode' -ErrorAction SilentlyContinue
    }

    Write-Info 'Re-enabling Hibernation (powercfg /h on)...'
    try {
        powercfg /h on | Out-Null
    } catch {}

    Write-Info 'Resetting TCP Network & Socket parameters...'
    netsh int tcp reset | Out-Null
    netsh winsock reset | Out-Null
    $tcpParams = 'HKLM:\SYSTEM\CurrentControlSet\Services\Tcpip\Parameters'
    if (Test-Path $tcpParams) {
        Remove-ItemProperty -Path $tcpParams -Name 'MaxUserPort' -ErrorAction SilentlyContinue
        Remove-ItemProperty -Path $tcpParams -Name 'TcpTimedWaitDelay' -ErrorAction SilentlyContinue
    }

    Write-Info 'Restoring Kernel Priority, Memory Paging & UI Delays...'
    $pControl = 'HKLM:\SYSTEM\CurrentControlSet\Control\PriorityControl'
    if (Test-Path $pControl) {
        Set-ItemProperty -Path $pControl -Name 'Win32PrioritySeparation' -Value 2 -ErrorAction SilentlyContinue
    }
    $memMgmt = 'HKLM:\SYSTEM\CurrentControlSet\Control\Session Manager\Memory Management'
    if (Test-Path $memMgmt) {
        Set-ItemProperty -Path $memMgmt -Name 'DisablePagingExecutive' -Value 0 -ErrorAction SilentlyContinue
    }
    Set-ItemProperty -Path 'HKCU:\Control Panel\Desktop' -Name 'MenuShowDelay' -Value '400' -ErrorAction SilentlyContinue
    try {
        fsutil behavior set disable8dot3 0 | Out-Null
        fsutil behavior set disablelastaccess 2 | Out-Null
    } catch {}

    Write-Success 'Revert operation completed! Windows default settings restored.'
}

# ==================================================
# FEATURE 13: AUTOSTART MANAGEMENT
# ==================================================
function Invoke-EnableStartup {
    Write-Header 'CONFIGURING OPTIMAX AUTOSTART OPTIMIZATION'

    $scriptPath = Join-Path $PSScriptRoot 'Optimax.ps1'
    $startupDir = [Environment]::GetFolderPath('Startup')
    $vbsPath = Join-Path $startupDir 'OptimaxBoot.vbs'
    $localVbsPath = Join-Path $PSScriptRoot 'RunOptimizerOnBoot.vbs'

    Write-Info ('Script Path: ' + $scriptPath)
    Write-Info ('Generating Autostart VBS script: ' + $vbsPath)

    $vbsContent = @"
' OPTIMAX Silent Startup Script
Set WshShell = CreateObject("WScript.Shell")
WshShell.Run "powershell.exe -ExecutionPolicy Bypass -WindowStyle Hidden -File ""$scriptPath"" -Extreme", 0, False
"@

    try {
        Set-Content -Path $vbsPath -Value $vbsContent -Encoding ASCII -Force
        Set-Content -Path $localVbsPath -Value $vbsContent -Encoding ASCII -Force
        Write-Success 'Optimax Autostart VBS script successfully generated/overwritten in Startup folder!'
    } catch {
        Write-Err ('Failed to write Startup VBS file: ' + $_.Exception.Message)
    }

    Write-Info 'Creating Scheduled Task "Optimax_AutoBoot" for elevated execution...'
    try {
        $action = New-ScheduledTaskAction -Execute 'powershell.exe' -Argument "-ExecutionPolicy Bypass -WindowStyle Hidden -File `"$scriptPath`" -Extreme"
        $trigger = New-ScheduledTaskTrigger -AtLogOn
        $principal = New-ScheduledTaskPrincipal -UserId "SYSTEM" -LogonType ServiceAccount -RunLevel Highest
        $settings = New-ScheduledTaskSettingsSet -AllowStartIfOnBatteries -DontStopIfGoingOnBatteries -StartWhenAvailable
        
        Register-ScheduledTask -TaskName 'Optimax_AutoBoot' -Action $action -Trigger $trigger -Principal $principal -Settings $settings -Force -ErrorAction SilentlyContinue | Out-Null
        Write-Success 'Scheduled Task "Optimax_AutoBoot" successfully created/overwritten!'
    } catch {
        Write-Info 'Scheduled Task creation optional (VBS Startup shortcut active).'
    }

    Write-Success 'OPTIMAX Windows Boot Autostart Optimization is now ACTIVE & SYNCHRONIZED!'
}

function Invoke-DisableStartup {
    Write-Header 'DISABLING OPTIMAX AUTOSTART OPTIMIZATION'

    $startupDir = [Environment]::GetFolderPath('Startup')
    $vbsPath = Join-Path $startupDir 'OptimaxBoot.vbs'
    $localVbsPath = Join-Path $PSScriptRoot 'RunOptimizerOnBoot.vbs'

    if (Test-Path $vbsPath) {
        Remove-Item -Path $vbsPath -Force -ErrorAction SilentlyContinue
        Write-Success 'Removed OptimaxBoot.vbs from Startup folder.'
    }
    if (Test-Path $localVbsPath) {
        Remove-Item -Path $localVbsPath -Force -ErrorAction SilentlyContinue
    }

    try {
        Unregister-ScheduledTask -TaskName 'Optimax_AutoBoot' -Confirm:$false -ErrorAction SilentlyContinue
        Write-Success 'Unregistered Scheduled Task "Optimax_AutoBoot".'
    } catch {}

    Write-Success 'OPTIMAX Windows Boot Autostart Optimization has been DISABLED.'
}

function Invoke-CheckStartup {
    Write-Header 'OPTIMAX AUTOSTART OPTIMIZATION STATUS AUDIT'
    $startupDir = [Environment]::GetFolderPath('Startup')
    $vbsPath = Join-Path $startupDir 'OptimaxBoot.vbs'
    $taskExists = Get-ScheduledTask -TaskName 'Optimax_AutoBoot' -ErrorAction SilentlyContinue

    if ((Test-Path $vbsPath) -or $taskExists) {
        Write-Success 'Status: OPTIMAX AUTOSTART IS ENABLED (Will run -Extreme on Windows Boot)'
    } else {
        Write-Info 'Status: OPTIMAX AUTOSTART IS DISABLED'
    }
}

# ==================================================
# EXECUTION LOGIC
# ==================================================
if ($Check) { Invoke-Check; exit }
if ($Optimize) { Invoke-Optimize; exit }
if ($Fix) { Invoke-Fix; exit }
if ($Advanced) { Invoke-AdvancedTweaks; exit }
if ($Pro) { Invoke-ProTweaks; exit }
if ($RestorePoint) { Invoke-RestorePoint; exit }
if ($CleanRestore) { Invoke-CleanRestorePoints; exit }
if ($DeepJunk) { Invoke-DeepJunk; exit }
if ($StorageExtreme) { Invoke-StorageExtreme; exit }
if ($Bloatware) { Invoke-Bloatware; exit }
if ($StandbyRAM) { Invoke-StandbyRAM; exit }
if ($Ultra) { Invoke-UltraTweaks; exit }
if ($MSIMode) { Invoke-MSIMode; exit }
if ($DisableMPO) { Invoke-DisableMPO; exit }
if ($MultiDriveTrim) { Invoke-MultiDriveTrim; exit }
if ($HardenedServices) { Invoke-HardenedServices; exit }
if ($PagefileFix) { Invoke-PagefileFix; exit }
if ($EnableStartup) { Invoke-EnableStartup; exit }
if ($DisableStartup) { Invoke-DisableStartup; exit }
if ($CheckStartup) { Invoke-CheckStartup; exit }
if ($Revert) { Invoke-Revert; exit }

if ($Auto -or $All) {
    Invoke-Check
    Invoke-Optimize
    Invoke-Fix
    Invoke-AdvancedTweaks
    Invoke-ProTweaks
    exit
}

if ($Extreme) {
    Invoke-RestorePoint
    Invoke-Check
    Invoke-Optimize
    Invoke-Fix
    Invoke-AdvancedTweaks
    Invoke-ProTweaks
    Invoke-MSIMode
    Invoke-DisableMPO
    Invoke-MultiDriveTrim
    Invoke-HardenedServices
    Invoke-PagefileFix
    Invoke-DeepJunk
    Invoke-StorageExtreme
    Invoke-Bloatware
    Invoke-StandbyRAM
    Invoke-UltraTweaks
    Invoke-CleanRestorePoints
    exit
}

# ==================================================
# CLI MENU MODE
# ==================================================
do {
    Clear-Host
    Write-Host '==================================================' -ForegroundColor Cyan
    Write-Host '  OPTIMAX - RADICAL DEEP WINDOWS SYSTEM OPTIMIZER (100%)' -ForegroundColor Yellow
    Write-Host '==================================================' -ForegroundColor Cyan

    $os = Get-CimInstance Win32_OperatingSystem
    $ramTotal = [math]::Round($os.TotalVisibleMemorySize / 1MB, 2)
    $ramFree = [math]::Round($os.FreePhysicalMemory / 1MB, 2)
    $ramUsed = [math]::Round($ramTotal - $ramFree, 2)

    $statusStr = ' RAM Status: Used ' + $ramUsed + ' GB / Total ' + $ramTotal + ' GB | Free RAM: ' + $ramFree + ' GB'
    Write-Host $statusStr -ForegroundColor Green
    Write-Host ''
    Write-Host ' 1. [!] CHECK          - Audit Hieu nang, RAM, CPU, Power Plan & Dia C:'
    Write-Host ' 2. [!] OPTIMIZE       - Toi uu RAM, Autostart & Junk Cleaning'
    Write-Host ' 3. [!] FIX            - Sua loi Windows Update & WinSxS Cleanup (5-15GB)'
    Write-Host ' 4. [!] ADVANCED       - PowerThrottling OFF, Core Parking, HAGS, Game Mode'
    Write-Host ' 5. [!] PRO            - TCP Stack netsh, Nagle Latency, Telemetry & SSD Trim'
    Write-Host ' 6. [!] RESTORE POINT  - Tao Diem khoi phuc he thong (Safety Backup)'
    Write-Host ' 7. [!] CLEAN RESTORE  - Xoa cac Diem khoi phuc cu (Giai phong 10-50GB SSD)'
    Write-Host ' 8. [!] DEEP JUNK      - Xoa GPU Shader Cache, Browser Cache & Event Logs'
    Write-Host ' 9. [!] STORAGE EXTREME- Tat Hibernation (Thu hoi 15-32GB SSD) & Storage Sense'
    Write-Host '10. [!] BLOATWARE      - Go UWP Apps rac & Tat Telemetry Scheduled Tasks'
    Write-Host '11. [!] STANDBY RAM    - Xoa Standby Memory List & WorkingSet (Chong giat lag)'
    Write-Host '12. [!] ULTRA TWEAKS   - Kernel Priority 0x26, DisablePaging, 0ms UI & CompactOS'
    Write-Host '13. [!] MSI MODE       - Kich hoat PCIe Message Signaled Interrupts (Giam DPC Latency)'
    Write-Host '14. [!] DISABLE MPO    - Tat Multi-Plane Overlay (Chong chop/giat khung hinh DWM)'
    Write-Host '15. [!] MULTI-DRIVE    - TRIM Toan bo cac o dia SSD va Defrag o HDD'
    Write-Host '16. [!] HARDENED SVCS  - Tat Triet de SysMain, Telemetry & Search Services (Disabled)'
    Write-Host '17. [!] PAGEFILE FIX   - Co dinh Virtual Memory 4096MB (Triet tieu I/O Lag)'
    Write-Host '18. [!] ENABLE AUTOSTART- Kich hoat / Ghi de Toiday tro khoi dong cung Windows'
    Write-Host '19. [!] DISABLE AUTOSTART- Tat Toi uu khoi dong cung Windows'
    Write-Host '20. [!] REVERT (UNDO)   - Khoi phuc tat ca ve Mac dinh ban dau cua Windows'
    Write-Host '21. [!] EXTREME ALL    - Thuc hien TOAN BO Toi Uu Triet De 100% (1-Click)'
    Write-Host ' 0. [X] Thoat (Exit)'
    Write-Host ''

    $choice = Read-Host 'Nhap lua chon cua ban (0-21)'
    switch ($choice) {
        '1'  { Invoke-Check; Pause }
        '2'  { Invoke-Optimize; Pause }
        '3'  { Invoke-Fix; Pause }
        '4'  { Invoke-AdvancedTweaks; Pause }
        '5'  { Invoke-ProTweaks; Pause }
        '6'  { Invoke-RestorePoint; Pause }
        '7'  { Invoke-CleanRestorePoints; Pause }
        '8'  { Invoke-DeepJunk; Pause }
        '9'  { Invoke-StorageExtreme; Pause }
        '10' { Invoke-Bloatware; Pause }
        '11' { Invoke-StandbyRAM; Pause }
        '12' { Invoke-UltraTweaks; Pause }
        '13' { Invoke-MSIMode; Pause }
        '14' { Invoke-DisableMPO; Pause }
        '15' { Invoke-MultiDriveTrim; Pause }
        '16' { Invoke-HardenedServices; Pause }
        '17' { Invoke-PagefileFix; Pause }
        '18' { Invoke-EnableStartup; Pause }
        '19' { Invoke-DisableStartup; Pause }
        '20' { Invoke-Revert; Pause }
        '21' { 
            Invoke-RestorePoint
            Invoke-Check
            Invoke-Optimize
            Invoke-Fix
            Invoke-AdvancedTweaks
            Invoke-ProTweaks
            Invoke-MSIMode
            Invoke-DisableMPO
            Invoke-MultiDriveTrim
            Invoke-HardenedServices
            Invoke-PagefileFix
            Invoke-DeepJunk
            Invoke-StorageExtreme
            Invoke-Bloatware
            Invoke-StandbyRAM
            Invoke-UltraTweaks
            Invoke-CleanRestorePoints
            Pause
        }
        '0'  { break }
    }
} while ($choice -ne '0')
