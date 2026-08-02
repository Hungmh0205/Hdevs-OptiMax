using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.ServiceProcess;
using Microsoft.Win32;
using Optimax.IPC;

namespace Optimax.Core
{
    public class TweakExecutionResult
    {
        public int TotalApplied { get; set; }
        public List<string> Messages { get; set; } = new();
    }

    public class SystemTweaksEngine : ISystemTweaksEngine
    {
        public TweakExecutionResult ExecuteTweaks(string[] flags, bool isDryRun = false)
        {
            var result = new TweakExecutionResult();
            var rollbackMgr = new TransactionalRollbackManager();
            using var txScope = new TransactionalScope(rollbackMgr);
            var backupPkg = txScope.Package;

            if (!isDryRun && flags != null && flags.Length > 0)
            {
                if (NativeSystemRestore.CreateRestorePoint("OptiMax Pre-Tweak Safety Checkpoint", out long seq))
                {
                    result.Messages.Add($"[SAFETY] Đã tạo điểm khôi phục Windows Restore Point (Seq: {seq}).");
                }
            }

            if (flags == null || flags.Length == 0) return result;


            foreach (var flag in flags)
            {
                if (string.IsNullOrWhiteSpace(flag)) continue;
                string cleanFlag = flag.Trim();

                try
                {
                    switch (cleanFlag.ToLowerInvariant())
                    {
                        case "-disablevbs":
                            if (!CpuTopologyDetector.IsVirtualMachine())
                            {
                                result.Messages.Add("[SAFETY GATED] Tắt VBS đã bị bỏ qua trên hệ thống vật lý để bảo vệ tính năng HVCI & Credential Guard. Chỉ cho phép trong máy ảo (VM).");
                            }
                            else
                            {
                                if (!isDryRun) SetRegistryDword(backupPkg, rollbackMgr, Registry.LocalMachine, @"System\CurrentControlSet\Control\DeviceGuard", "EnableVirtualizationBasedSecurity", 0);
                                result.Messages.Add((isDryRun ? "[DRY-RUN] Sẽ " : "") + "Tắt VBS (Virtualization-Based Security)");
                                result.TotalApplied++;
                            }
                            break;

                        case "-enablevbs":
                            if (!isDryRun) SetRegistryDword(backupPkg, rollbackMgr, Registry.LocalMachine, @"System\CurrentControlSet\Control\DeviceGuard", "EnableVirtualizationBasedSecurity", 1);
                            result.Messages.Add((isDryRun ? "[DRY-RUN] Sẽ " : "") + "Bật VBS (Virtualization-Based Security)");
                            result.TotalApplied++;
                            break;

                        case "-powerultimate":
                        case "-setultimatepower":
                            if (CpuTopologyDetector.IsOnBatteryPower())
                            {
                                result.Messages.Add("[SAFETY GATED] Đã bỏ qua kích hoạt Ultimate Performance do thiết bị đang dùng Pin để tránh chai pin và quá nhiệt.");
                            }
                            else
                            {
                                if (!isDryRun)
                                {
                                    EnsureUltimatePowerScheme();
                                    RunCommand("powercfg", "/setactive e9a42b02-d5df-448d-aa00-03f14749eb61");
                                }
                                result.Messages.Add((isDryRun ? "[DRY-RUN] Sẽ " : "") + "Kích hoạt sơ đồ Nguồn Ultimate Performance");
                                result.TotalApplied++;
                            }
                            break;

                        case "-setbalancedpower":
                            if (!isDryRun) RunCommand("powercfg", "/setactive 381b4222-f694-41f0-9685-ff5bb260df2e");
                            result.Messages.Add((isDryRun ? "[DRY-RUN] Sẽ " : "") + "Đặt sơ đồ Nguồn Balanced (Cân bằng)");
                            result.TotalApplied++;
                            break;

                        case "-disablehiber":
                        case "-disablehibernation":
                            if (!isDryRun) RunCommand("powercfg", "/hibernate off");
                            result.Messages.Add((isDryRun ? "[DRY-RUN] Sẽ " : "") + "Tắt Chế độ Ngủ đông (Hibernation) & giải phóng hiberfil.sys");
                            result.TotalApplied++;
                            break;

                        case "-enablehiber":
                        case "-enablehibernation":
                            if (!isDryRun) RunCommand("powercfg", "/hibernate on");
                            result.Messages.Add((isDryRun ? "[DRY-RUN] Sẽ " : "") + "Bật Chế độ Ngủ đông (Hibernation)");
                            result.TotalApplied++;
                            break;

                        case "-disablesearch":
                        case "-disablewsearch":
                            if (!isDryRun) SetServiceState(backupPkg, rollbackMgr, "WSearch", ServiceStartMode.Disabled);
                            result.Messages.Add((isDryRun ? "[DRY-RUN] Sẽ " : "") + "Vô hiệu hóa dịch vụ Windows Search (WSearch)");
                            result.TotalApplied++;
                            break;

                        case "-enablesearch":
                        case "-enablewsearch":
                            if (!isDryRun) SetServiceState(backupPkg, rollbackMgr, "WSearch", ServiceStartMode.Automatic);
                            result.Messages.Add((isDryRun ? "[DRY-RUN] Sẽ " : "") + "Bật dịch vụ Windows Search (WSearch)");
                            result.TotalApplied++;
                            break;

                        case "-disablespooler":
                        case "-disableprintspooler":
                            if (!isDryRun) SetServiceState(backupPkg, rollbackMgr, "Spooler", ServiceStartMode.Disabled);
                            result.Messages.Add((isDryRun ? "[DRY-RUN] Sẽ " : "") + "Vô hiệu hóa dịch vụ Print Spooler");
                            result.TotalApplied++;
                            break;

                        case "-enablespooler":
                        case "-enableprintspooler":
                            if (!isDryRun) SetServiceState(backupPkg, rollbackMgr, "Spooler", ServiceStartMode.Automatic);
                            result.Messages.Add((isDryRun ? "[DRY-RUN] Sẽ " : "") + "Bật dịch vụ Print Spooler");
                            result.TotalApplied++;
                            break;

                        case "-disablesysmain":
                        case "-disablesuperfetch":
                            if (!isDryRun) SetServiceState(backupPkg, rollbackMgr, "SysMain", ServiceStartMode.Disabled);
                            result.Messages.Add((isDryRun ? "[DRY-RUN] Sẽ " : "") + "Vô hiệu hóa dịch vụ SysMain (Superfetch)");
                            result.TotalApplied++;
                            break;

                        case "-enablesysmain":
                        case "-enablesuperfetch":
                            if (!isDryRun) SetServiceState(backupPkg, rollbackMgr, "SysMain", ServiceStartMode.Automatic);
                            result.Messages.Add((isDryRun ? "[DRY-RUN] Sẽ " : "") + "Bật dịch vụ SysMain (Superfetch)");
                            result.TotalApplied++;
                            break;

                        case "-disablempo":
                            if (!isDryRun) SetRegistryDword(backupPkg, rollbackMgr, Registry.LocalMachine, @"SOFTWARE\Microsoft\Windows\Dwm", "OverlayTestMode", 5);
                            result.Messages.Add((isDryRun ? "[DRY-RUN] Sẽ " : "") + "Tắt Multi-Plane Overlay (MPO) chống giật màn hình GPU");
                            result.TotalApplied++;
                            break;

                        case "-qosnet":
                            if (!isDryRun)
                            {
                                SetRegistryDword(backupPkg, rollbackMgr, Registry.LocalMachine, @"SOFTWARE\Microsoft\Windows NT\CurrentVersion\Multimedia\SystemProfile", "NetworkThrottlingIndex", unchecked((int)0xFFFFFFFF));
                                SetRegistryDword(backupPkg, rollbackMgr, Registry.LocalMachine, @"SOFTWARE\Microsoft\Windows NT\CurrentVersion\Multimedia\SystemProfile", "SystemResponsiveness", 0);
                            }
                            result.Messages.Add((isDryRun ? "[DRY-RUN] Sẽ " : "") + "Tối ưu hóa Băng thông Mạng QoS & Độ nhạy Hệ thống");
                            result.TotalApplied++;
                            break;

                        case "-uxdebloat":
                            if (!isDryRun)
                            {
                                SetRegistryDword(backupPkg, rollbackMgr, Registry.CurrentUser, @"Software\Microsoft\Windows\CurrentVersion\Explorer\Advanced", "TaskbarDa", 0);
                                SetRegistryDword(backupPkg, rollbackMgr, Registry.CurrentUser, @"Software\Microsoft\Windows\CurrentVersion\Explorer\Advanced", "ShowCopilotButton", 0);
                            }
                            result.Messages.Add((isDryRun ? "[DRY-RUN] Sẽ " : "") + "Dọn dẹp biểu tượng thừa trên Thanh Tác vụ (Widgets & Copilot)");
                            result.TotalApplied++;
                            break;

                        case "-cpuboost":
                        case "-cpupriority":
                            if (CpuTopologyDetector.IsHybridOrArm64Topology())
                            {
                                result.Messages.Add("[SAFETY GATED] Đã bỏ qua Win32PrioritySeparation trên CPU Hybrid/ARM64 để tránh hiện tượng Thread Starvation và giật 1% Low FPS.");
                            }
                            else
                            {
                                if (!isDryRun) SetRegistryDword(backupPkg, rollbackMgr, Registry.LocalMachine, @"SYSTEM\CurrentControlSet\Control\PriorityControl", "Win32PrioritySeparation", 38);
                                result.Messages.Add((isDryRun ? "[DRY-RUN] Sẽ " : "") + "Tối ưu CPU Scheduling (Short Quantum, Variable, Foreground Boost)");
                                result.TotalApplied++;
                            }
                            break;

                        case "-msimode":
                            if (!isDryRun) EnableMsiModeForPciDevices(backupPkg, rollbackMgr);
                            result.Messages.Add((isDryRun ? "[DRY-RUN] Sẽ " : "") + "Kích hoạt MSI Mode (Message Signaled Interrupts) cho các thiết bị PCI hỗ trợ");
                            result.TotalApplied++;
                            break;

                        case "-multidrivetrim":
                            if (!isDryRun) RunCommand("defrag", "/O C:");
                            result.Messages.Add((isDryRun ? "[DRY-RUN] Sẽ " : "") + "Tối ưu hóa & TRIM đĩa cứng SSD/NVMe (Multi-Drive Trim)");
                            result.TotalApplied++;
                            break;

                        case "-timerres":
                        case "-timerresolution":
                            if (CpuTopologyDetector.IsWindows10Build2004OrNewer())
                            {
                                result.Messages.Add("[SAFETY GATED] Bỏ qua cờ Registry GlobalTimerResolution do Windows 10 2004+ đã thay đổi cơ chế timer per-process.");
                            }
                            else
                            {
                                if (!isDryRun) SetRegistryDword(backupPkg, rollbackMgr, Registry.LocalMachine, @"SYSTEM\CurrentControlSet\Control\Session Manager\Kernel", "GlobalTimerResolutionRequests", 1);
                                result.Messages.Add((isDryRun ? "[DRY-RUN] Sẽ " : "") + "Tối ưu hóa độ phân giải bộ đếm thời gian hệ thống (Global Timer Resolution 0.5ms)");
                                result.TotalApplied++;
                            }
                            break;

                        case "-mmcss":
                        case "-mmcsstuning":
                            if (!isDryRun)
                            {
                                SetRegistryDword(backupPkg, rollbackMgr, Registry.LocalMachine, @"SOFTWARE\Microsoft\Windows NT\CurrentVersion\Multimedia\SystemProfile\Tasks\Games", "GPU Priority", 8);
                                SetRegistryDword(backupPkg, rollbackMgr, Registry.LocalMachine, @"SOFTWARE\Microsoft\Windows NT\CurrentVersion\Multimedia\SystemProfile\Tasks\Games", "Priority", 6);
                                SetRegistryString(backupPkg, rollbackMgr, Registry.LocalMachine, @"SOFTWARE\Microsoft\Windows NT\CurrentVersion\Multimedia\SystemProfile\Tasks\Games", "Scheduling Category", "High");
                            }
                            result.Messages.Add((isDryRun ? "[DRY-RUN] Sẽ " : "") + "Tối ưu hóa Multimedia Class Scheduler (MMCSS) cho Game & Audio Latency");
                            result.TotalApplied++;
                            break;

                        case "-netadapter":
                        case "-netadapteroptimization":
                            if (!isDryRun) SetNetworkAdapterOptimization(backupPkg, rollbackMgr);
                            result.Messages.Add((isDryRun ? "[DRY-RUN] Sẽ " : "") + "Tối ưu Card mạng LAN/WiFi (Giảm Latency Ping & Tắt Nagle Algorithm)");
                            result.TotalApplied++;
                            break;

                        case "-thirdpartyjunk":
                        case "-deepjunk":
                            int junkCount = isDryRun ? 0 : CleanThirdPartyJunkFolders();
                            result.Messages.Add((isDryRun ? "[DRY-RUN] Sẽ " : "") + $"Dọn dẹp {(isDryRun ? "các" : junkCount.ToString())} tệp bộ nhớ đệm rác Ứng Dụng Bên Thứ Ba (Discord, Spotify, Chrome, VSCode, Steam)");
                            result.TotalApplied++;
                            break;

                        case "-cleanregistry":
                            var regScan = new DeepRegistryScanner();
                            var regRep = regScan.ScanAndClean(isDryRun);
                            result.Messages.Add((isDryRun ? "[DRY-RUN] Sẽ " : "") + $"Quét & dọn dẹp {regRep.TotalIssuesFound} mục Registry mồ côi");
                            result.TotalApplied++;
                            break;

                        case "-automaintenance":
                            if (!isDryRun) RunCommand("schtasks", "/change /tn \"\\Microsoft\\Windows\\TaskScheduler\\Maintenance Tasks\" /disable");
                            result.Messages.Add((isDryRun ? "[DRY-RUN] Sẽ " : "") + "Tắt Tác Vụ Tự Động Bảo Trì Khỏi Làm Gián Đoạn Chơi Game (Windows Auto Maintenance)");
                            result.TotalApplied++;
                            break;

                        case "-forcecleanshadows":
                            result.Messages.Add("[SAFETY DISABLED] Lệnh xóa VSS Shadow Copies (vssadmin) đã bị vô hiệu hóa hoàn toàn để bảo vệ các điểm khôi phục hệ thống.");
                            break;

                        case "-standbyram":
                            if (!isDryRun) KernelMemoryTrimmer.TrimSystemMemory(forceDeepPurge: true);
                            result.Messages.Add((isDryRun ? "[DRY-RUN] Sẽ " : "") + "Thu hồi RAM & Xả System Standby List");
                            result.TotalApplied++;
                            break;

                        case "-bloatware":
                            var debloater = new WindowsDebloater();
                            var debReport = debloater.ApplyDebloatItems(new[] { "telemetry", "copilot", "bingsearch", "widgets", "advertising" }, isDryRun);
                            result.Messages.Add((isDryRun ? "[DRY-RUN] Sẽ " : "") + $"Áp dụng Debloat Windows ({debReport.TotalApplied} mục)");
                            result.TotalApplied++;
                            break;

                        case "-systemp":
                        case "-systempclean":
                            int sysTempCount = isDryRun ? 0 : CleanSystemTempFolders();
                            result.Messages.Add((isDryRun ? "[DRY-RUN] Sẽ " : "") + $"Dọn dẹp Tệp Rác Hệ Thống (TEMP, Prefetch, WER) ({sysTempCount} tệp)");
                            result.TotalApplied++;
                            break;
                    }
                }
                catch (Exception ex) { OptimaxLogger.Warn($"Tweak '{cleanFlag}' failed", ex); }
            }

            if (!isDryRun && (backupPkg.RegistryEntries.Count > 0 || backupPkg.ServiceEntries.Count > 0))
            {
                txScope.Commit();
            }

            return result;
        }


        private static void SetRegistryDword(SystemStateBackupPackage package, TransactionalRollbackManager rollbackMgr, RegistryKey root, string subKey, string valueName, int value)
        {
            rollbackMgr.SnapshotRegistryKey(package, root, subKey, valueName);
            using var key = root.CreateSubKey(subKey, writable: true);
            key?.SetValue(valueName, value, RegistryValueKind.DWord);
        }

        private static void SetRegistryString(SystemStateBackupPackage package, TransactionalRollbackManager rollbackMgr, RegistryKey root, string subKey, string valueName, string value)
        {
            rollbackMgr.SnapshotRegistryKey(package, root, subKey, valueName);
            using var key = root.CreateSubKey(subKey, writable: true);
            key?.SetValue(valueName, value, RegistryValueKind.String);
        }

        private static void SetNetworkAdapterOptimization(SystemStateBackupPackage package, TransactionalRollbackManager rollbackMgr)
        {
            try
            {
                string basePath = @"SYSTEM\CurrentControlSet\Services\Tcpip\Parameters\Interfaces";
                using var key = Registry.LocalMachine.OpenSubKey(basePath, writable: true);
                if (key != null)
                {
                    foreach (var subName in key.GetSubKeyNames())
                    {
                        string subPath = $"{basePath}\\{subName}";
                        SetRegistryDword(package, rollbackMgr, Registry.LocalMachine, subPath, "TcpAckFrequency", 1);
                        SetRegistryDword(package, rollbackMgr, Registry.LocalMachine, subPath, "TCPNoDelay", 1);
                    }
                }
            }
            catch (Exception ex) { OptimaxLogger.Warn("Network adapter optimization failed", ex); }
        }

        private static bool IsReparsePoint(string path)
        {
            try
            {
                var attr = File.GetAttributes(path);
                return (attr & FileAttributes.ReparsePoint) != 0;
            }
            catch
            {
                return true; // Skip if attributes cannot be checked
            }
        }

        private static int CleanThirdPartyJunkFolders()
        {
            int cleanedFiles = 0;
            string localAppData = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
            string appData = Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData);

            string[] junkDirs = new[]
            {
                Path.Combine(appData, "discord", "Cache"),
                Path.Combine(appData, "discord", "Code Cache"),
                Path.Combine(localAppData, "Spotify", "Storage"),
                Path.Combine(localAppData, "Google", "Chrome", "User Data", "Default", "Cache"),
                Path.Combine(localAppData, "Microsoft", "Edge", "User Data", "Default", "Cache"),
                Path.Combine(appData, "Code", "Cache"),
                Path.Combine(appData, "Code", "CachedData")
            };

            foreach (var dir in junkDirs)
            {
                if (Directory.Exists(dir) && !IsReparsePoint(dir))
                {
                    try
                    {
                        foreach (var file in Directory.GetFiles(dir, "*", SearchOption.AllDirectories))
                        {
                            try
                            {
                                if (!IsReparsePoint(file))
                                {
                                    File.Delete(file);
                                    cleanedFiles++;
                                }
                            }
                            catch (Exception ex) { OptimaxLogger.Trace($"Failed to delete junk file: {file}", ex); }
                        }
                    }
                    catch (Exception ex) { OptimaxLogger.Trace($"Failed to enumerate junk directory: {dir}", ex); }
                }
            }
            return cleanedFiles;
        }

        private static void SetServiceState(SystemStateBackupPackage package, TransactionalRollbackManager rollbackMgr, string serviceName, ServiceStartMode startMode)
        {
            rollbackMgr.SnapshotService(package, serviceName);
            try
            {
                using var sc = new ServiceController(serviceName);
                if (startMode == ServiceStartMode.Disabled && sc.Status == ServiceControllerStatus.Running)
                {
                    try { sc.Stop(); } catch (Exception ex) { OptimaxLogger.Warn($"Failed to stop service '{serviceName}'", ex); }
                }
            }
            catch (Exception ex) { OptimaxLogger.Trace($"Service '{serviceName}' not accessible for stop", ex); }

            try
            {
                using var key = Registry.LocalMachine.OpenSubKey($@"SYSTEM\CurrentControlSet\Services\{serviceName}", writable: true);
                if (key != null)
                {
                    int startVal = startMode switch
                    {
                        ServiceStartMode.Automatic => 2,
                        ServiceStartMode.Manual => 3,
                        ServiceStartMode.Disabled => 4,
                        _ => 3
                    };
                    key.SetValue("Start", startVal, RegistryValueKind.DWord);
                }
            }
            catch (Exception ex) { OptimaxLogger.Warn($"Failed to set service '{serviceName}' start mode via registry", ex); }
        }


        private static void EnsureUltimatePowerScheme()
        {
            try
            {
                var psi = new ProcessStartInfo
                {
                    FileName = "powercfg",
                    Arguments = "/list",
                    CreateNoWindow = true,
                    UseShellExecute = false,
                    RedirectStandardOutput = true
                };
                using var p = Process.Start(psi);
                string output = p?.StandardOutput.ReadToEnd() ?? "";
                p?.WaitForExit(3000);

                if (!output.Contains("e9a42b02-d5df-448d-aa00-03f14749eb61", StringComparison.OrdinalIgnoreCase))
                {
                    RunCommand("powercfg", "-duplicatescheme e9a42b02-d5df-448d-aa00-03f14749eb61");
                }
            }
            catch (Exception ex) { OptimaxLogger.Trace("Failed to check Ultimate Power Scheme availability", ex); }
        }

        private static void RunCommand(string filename, string args, int timeoutMs = 30000)
        {
            try
            {
                var psi = new ProcessStartInfo
                {
                    FileName = filename,
                    Arguments = args,
                    CreateNoWindow = true,
                    UseShellExecute = false
                };
                using var p = Process.Start(psi);
                p?.WaitForExit(timeoutMs);
            }
            catch (Exception ex) { OptimaxLogger.Warn($"Failed to run command: {filename} {args}", ex); }
        }

        /// <summary>
        /// Enable MSI Mode (Message Signaled Interrupts) for PCI devices that support it.
        /// Enumerates HKLM\SYSTEM\CurrentControlSet\Enum\PCI and sets MSISupported=1
        /// only for devices that already have the MessageSignaledInterruptProperties key.
        /// </summary>
        private static void EnableMsiModeForPciDevices(SystemStateBackupPackage package, TransactionalRollbackManager rollbackMgr)
        {
            try
            {
                string pciEnumPath = @"SYSTEM\CurrentControlSet\Enum\PCI";
                using var pciKey = Registry.LocalMachine.OpenSubKey(pciEnumPath);
                if (pciKey == null) return;

                foreach (var deviceId in pciKey.GetSubKeyNames())
                {
                    using var deviceKey = pciKey.OpenSubKey(deviceId);
                    if (deviceKey == null) continue;

                    foreach (var instanceId in deviceKey.GetSubKeyNames())
                    {
                        string msiSubPath = $@"{pciEnumPath}\{deviceId}\{instanceId}\Device Parameters\Interrupt Management\MessageSignaledInterruptProperties";
                        try
                        {
                            using var msiKey = Registry.LocalMachine.OpenSubKey(msiSubPath, writable: true);
                            if (msiKey != null)
                            {
                                // Device supports MSI — snapshot current state and enable
                                rollbackMgr.SnapshotRegistryKey(package, Registry.LocalMachine, msiSubPath, "MSISupported");
                                msiKey.SetValue("MSISupported", 1, RegistryValueKind.DWord);
                            }
                        }
                        catch (Exception ex) { OptimaxLogger.Trace($"MSI Mode: skipped device {deviceId}\\{instanceId}", ex); }
                    }
                }
            }
            catch (Exception ex) { OptimaxLogger.Warn("MSI Mode PCI enumeration failed", ex); }
        }

        /// <summary>
        /// Clean system temp directories (TEMP, Windows\Temp, Prefetch, WER).
        /// Separate from CleanThirdPartyJunkFolders which handles app-specific caches.
        /// </summary>
        private static int CleanSystemTempFolders()
        {
            int cleanedFiles = 0;
            string winDir = Environment.GetFolderPath(Environment.SpecialFolder.Windows);
            if (string.IsNullOrEmpty(winDir)) winDir = "C:\\Windows";

            string[] systemTempDirs = new[]
            {
                Environment.ExpandEnvironmentVariables("%TEMP%"),
                Path.Combine(winDir, "Temp"),
                Path.Combine(winDir, "Prefetch"),
                Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Microsoft\\Windows\\WER\\Temp"),
                Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Microsoft\\Windows\\WER\\ReportArchive"),
                Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Microsoft\\Windows\\INetCache")
            };

            foreach (var dir in systemTempDirs)
            {
                if (!Directory.Exists(dir) || IsReparsePoint(dir)) continue;
                try
                {
                    foreach (var file in Directory.EnumerateFiles(dir, "*", SearchOption.AllDirectories))
                    {
                        try
                        {
                            if (!IsReparsePoint(file))
                            {
                                File.Delete(file);
                                cleanedFiles++;
                            }
                        }
                        catch (Exception ex) { OptimaxLogger.Trace($"Failed to delete system temp file: {file}", ex); }
                    }
                }
                catch (Exception ex) { OptimaxLogger.Trace($"Failed to enumerate system temp directory: {dir}", ex); }
            }
            return cleanedFiles;
        }
    }
}
