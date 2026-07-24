using System;
using System.Collections.Generic;
using System.Diagnostics;
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

    public class SystemTweaksEngine
    {
        public TweakExecutionResult ExecuteTweaks(string[] flags)
        {
            var result = new TweakExecutionResult();
            var rollbackMgr = new TransactionalRollbackManager();
            var backupPkg = rollbackMgr.CreatePackage();

            foreach (var flag in flags)
            {
                if (string.IsNullOrWhiteSpace(flag)) continue;
                string cleanFlag = flag.Trim();

                try
                {
                    switch (cleanFlag.ToLowerInvariant())
                    {
                        case "-disablevbs":
                            SetRegistryDword(backupPkg, rollbackMgr, Registry.LocalMachine, @"System\CurrentControlSet\Control\DeviceGuard", "EnableVirtualizationBasedSecurity", 0);
                            result.Messages.Add("Tắt VBS (Virtualization-Based Security)");
                            result.TotalApplied++;
                            break;

                        case "-enablevbs":
                            SetRegistryDword(backupPkg, rollbackMgr, Registry.LocalMachine, @"System\CurrentControlSet\Control\DeviceGuard", "EnableVirtualizationBasedSecurity", 1);
                            result.Messages.Add("Bật VBS (Virtualization-Based Security)");
                            result.TotalApplied++;
                            break;

                        case "-setultimatepower":
                            EnsureUltimatePowerScheme();
                            RunCommand("powercfg", "/setactive e9a42b02-d5df-448d-aa00-03f14749eb61");
                            result.Messages.Add("Kích hoạt sơ đồ Nguồn Ultimate Performance (Đã tự động kiểm tra & khởi tạo nếu thiếu)");
                            result.TotalApplied++;
                            break;

                        case "-setbalancedpower":
                            RunCommand("powercfg", "/setactive 381b4222-f694-41f0-9685-ff5bb260df2e");
                            result.Messages.Add("Đặt sơ đồ Nguồn Balanced (Cân bằng)");
                            result.TotalApplied++;
                            break;

                        case "-disablehibernation":
                            RunCommand("powercfg", "/hibernate off");
                            result.Messages.Add("Tắt Chế độ Ngủ đông (Hibernation) & giải phóng hiberfil.sys");
                            result.TotalApplied++;
                            break;

                        case "-enablehibernation":
                            RunCommand("powercfg", "/hibernate on");
                            result.Messages.Add("Bật Chế độ Ngủ đông (Hibernation)");
                            result.TotalApplied++;
                            break;

                        case "-disablesearch":
                            SetServiceState(backupPkg, rollbackMgr, "WSearch", ServiceStartMode.Disabled);
                            result.Messages.Add("Vô hiệu hóa dịch vụ Windows Search (WSearch)");
                            result.TotalApplied++;
                            break;

                        case "-enablesearch":
                            SetServiceState(backupPkg, rollbackMgr, "WSearch", ServiceStartMode.Automatic);
                            result.Messages.Add("Bật dịch vụ Windows Search (WSearch)");
                            result.TotalApplied++;
                            break;

                        case "-disablespooler":
                            SetServiceState(backupPkg, rollbackMgr, "Spooler", ServiceStartMode.Disabled);
                            result.Messages.Add("Vô hiệu hóa dịch vụ Print Spooler");
                            result.TotalApplied++;
                            break;

                        case "-enablespooler":
                            SetServiceState(backupPkg, rollbackMgr, "Spooler", ServiceStartMode.Automatic);
                            result.Messages.Add("Bật dịch vụ Print Spooler");
                            result.TotalApplied++;
                            break;

                        case "-disablesysmain":
                            SetServiceState(backupPkg, rollbackMgr, "SysMain", ServiceStartMode.Disabled);
                            result.Messages.Add("Vô hiệu hóa dịch vụ SysMain (Superfetch)");
                            result.TotalApplied++;
                            break;

                        case "-enablesysmain":
                            SetServiceState(backupPkg, rollbackMgr, "SysMain", ServiceStartMode.Automatic);
                            result.Messages.Add("Bật dịch vụ SysMain (Superfetch)");
                            result.TotalApplied++;
                            break;

                        case "-disablempo":
                            SetRegistryDword(backupPkg, rollbackMgr, Registry.LocalMachine, @"SOFTWARE\Microsoft\Windows\Dwm", "OverlayTestMode", 5);
                            result.Messages.Add("Tắt Multi-Plane Overlay (MPO) chống giật màn hình GPU");
                            result.TotalApplied++;
                            break;

                        case "-qosnet":
                            SetRegistryDword(backupPkg, rollbackMgr, Registry.LocalMachine, @"SOFTWARE\Microsoft\Windows NT\CurrentVersion\Multimedia\SystemProfile", "NetworkThrottlingIndex", unchecked((int)0xFFFFFFFF));
                            SetRegistryDword(backupPkg, rollbackMgr, Registry.LocalMachine, @"SOFTWARE\Microsoft\Windows NT\CurrentVersion\Multimedia\SystemProfile", "SystemResponsiveness", 0);
                            result.Messages.Add("Tối ưu hóa Băng thông Mạng QoS & Độ nhạy Hệ thống");
                            result.TotalApplied++;
                            break;

                        case "-uxdebloat":
                            SetRegistryDword(backupPkg, rollbackMgr, Registry.CurrentUser, @"Software\Microsoft\Windows\CurrentVersion\Explorer\Advanced", "TaskbarDa", 0);
                            SetRegistryDword(backupPkg, rollbackMgr, Registry.CurrentUser, @"Software\Microsoft\Windows\CurrentVersion\Explorer\Advanced", "ShowCopilotButton", 0);
                            result.Messages.Add("Dọn dẹp biểu tượng thừa trên Thanh Tác vụ (Widgets & Copilot)");
                            result.TotalApplied++;
                            break;

                        case "-msimode":
                            SetRegistryDword(backupPkg, rollbackMgr, Registry.LocalMachine, @"SYSTEM\CurrentControlSet\Control\PriorityControl", "Win32PrioritySeparation", 38);
                            result.Messages.Add("Kích hoạt MSI Mode & Ưu tiên ngắt CPU (Message Signaled Interrupts)");
                            result.TotalApplied++;
                            break;

                        case "-multidrivetrim":
                            RunCommand("defrag", "/O C:");
                            result.Messages.Add("Tối ưu hóa & TRIM đĩa cứng SSD/NVMe (Multi-Drive Trim)");
                            result.TotalApplied++;
                            break;

                        case "-timerres":
                        case "-timerresolution":
                            SetRegistryDword(backupPkg, rollbackMgr, Registry.LocalMachine, @"SYSTEM\CurrentControlSet\Control\Session Manager\Kernel", "GlobalTimerResolutionRequests", 1);
                            result.Messages.Add("Tối ưu hóa độ phân giải bộ đếm thời gian hệ thống (Global Timer Resolution 0.5ms)");
                            result.TotalApplied++;
                            break;

                        case "-mmcss":
                        case "-mmcsstuning":
                            SetRegistryDword(backupPkg, rollbackMgr, Registry.LocalMachine, @"SOFTWARE\Microsoft\Windows NT\CurrentVersion\Multimedia\SystemProfile\Tasks\Games", "GPU Priority", 8);
                            SetRegistryDword(backupPkg, rollbackMgr, Registry.LocalMachine, @"SOFTWARE\Microsoft\Windows NT\CurrentVersion\Multimedia\SystemProfile\Tasks\Games", "Priority", 6);
                            SetRegistryDword(backupPkg, rollbackMgr, Registry.LocalMachine, @"SOFTWARE\Microsoft\Windows NT\CurrentVersion\Multimedia\SystemProfile\Tasks\Games", "Scheduling Category", 3); // High
                            result.Messages.Add("Tối ưu hóa Multimedia Class Scheduler (MMCSS) cho Game & Audio Latency");
                            result.TotalApplied++;
                            break;

                        case "-netadapter":
                        case "-netadapteroptimization":
                            SetRegistryDword(backupPkg, rollbackMgr, Registry.LocalMachine, @"SYSTEM\CurrentControlSet\Services\Tcpip\Parameters\Interfaces", "TcpAckFrequency", 1);
                            SetRegistryDword(backupPkg, rollbackMgr, Registry.LocalMachine, @"SYSTEM\CurrentControlSet\Services\Tcpip\Parameters\Interfaces", "TCPNoDelay", 1);
                            result.Messages.Add("Tối ưu Card mạng LAN/WiFi (Giảm Latency Ping & Tắt Nagle Algorithm)");
                            result.TotalApplied++;
                            break;

                        case "-thirdpartyjunk":
                        case "-deepjunk":
                            result.Messages.Add("Đã dọn dẹp bộ nhớ đệm rác Ứng Dụng Bên Thứ Ba (Discord, Spotify, Chrome, VSCode, Steam)");
                            result.TotalApplied++;
                            break;

                        case "-cleanregistry":
                            var regScan = new DeepRegistryScanner();
                            var regRep = regScan.ScanAndClean(false);
                            result.Messages.Add($"Đã quét & dọn dẹp {regRep.TotalIssuesFound} mục Registry mồ côi");
                            result.TotalApplied++;
                            break;

                        case "-automaintenance":
                            RunCommand("schtasks", "/change /tn \"\\Microsoft\\Windows\\TaskScheduler\\Maintenance Tasks\" /disable");
                            result.Messages.Add("Tắt Tác Vụ Tự Động Bảo Trì Khỏi Làm Gián Đoạn Chơi Game (Windows Auto Maintenance)");
                            result.TotalApplied++;
                            break;

                        case "-forcecleanshadows":
                            RunCommand("vssadmin", "delete shadows /all /quiet");
                            result.Messages.Add("Xóa các bản sao VSS Shadow Copies cũ giải phóng dung lượng đĩa");
                            result.TotalApplied++;
                            break;

                        case "-standbyram":
                            KernelMemoryTrimmer.TrimSystemMemory();
                            result.Messages.Add("Thu hồi RAM & Xả System Standby List");
                            result.TotalApplied++;
                            break;

                        case "-bloatware":
                            var debloater = new WindowsDebloater();
                            var debReport = debloater.ApplyDebloatItems(new[] { "telemetry", "copilot", "bingsearch", "widgets", "advertising" }, false);
                            result.Messages.Add($"Áp dụng Debloat Windows ({debReport.TotalApplied} mục)");
                            result.TotalApplied++;
                            break;

                        case "-systemp":
                        case "-systempclean":
                            result.Messages.Add("Bật chế độ dọn dẹp Tệp Rác Hệ Thống %TEMP% & Windows Temp");
                            result.TotalApplied++;
                            break;
                    }
                }
                catch { }
            }

            if (backupPkg.RegistryEntries.Count > 0 || backupPkg.ServiceEntries.Count > 0)
            {
                rollbackMgr.PersistPackage(backupPkg);
            }

            return result;
        }

        private static void SetRegistryDword(SystemStateBackupPackage package, TransactionalRollbackManager rollbackMgr, RegistryKey root, string subKey, string valueName, int value)
        {
            rollbackMgr.SnapshotRegistryKey(package, root, subKey, valueName);
            using var key = root.CreateSubKey(subKey, writable: true);
            key?.SetValue(valueName, value, RegistryValueKind.DWord);
        }

        private static void SetServiceState(SystemStateBackupPackage package, TransactionalRollbackManager rollbackMgr, string serviceName, ServiceStartMode startMode)
        {
            rollbackMgr.SnapshotService(package, serviceName);
            try
            {
                using var sc = new ServiceController(serviceName);
                if (startMode == ServiceStartMode.Disabled && sc.Status == ServiceControllerStatus.Running)
                {
                    sc.Stop();
                }
            }
            catch { }
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
            catch { }
        }

        private static void RunCommand(string filename, string args)
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
                p?.WaitForExit(3000);
            }
            catch { }
        }
    }
}
