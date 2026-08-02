using System;
using System.Collections.Generic;
using System.IO;
using System.ServiceProcess;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Optimax.Core;

namespace Optimax.IPC
{
    /// <summary>
    /// Centralized IPC command handler — dispatches IPCRequest commands to the appropriate engine.
    /// Extracted from Program.cs to reduce main entry point size and improve separation of concerns.
    /// </summary>
    public class IpcCommandDispatcher
    {
        private CancellationTokenSource? _activeMonitorCts;

        public async Task<IPCResponse> HandleRequestAsync(
            IPCRequest req,
            Func<IPCStreamChunk, Task> sendChunk,
            string defaultRulesFile,
            bool defaultIsDryRun)
        {
            switch (req.Command)
            {
                case "scan":
                case "clean":
                    return await HandleScanCleanAsync(req, sendChunk, defaultRulesFile);

                case "schedule-daily":
                    return HandleScheduleDaily(req);

                case "schedule-weekly":
                    return HandleScheduleWeekly(req);

                case "unschedule":
                    return HandleUnschedule();

                case "start-monitor":
                    return HandleStartMonitor();

                case "stop-monitor":
                    return HandleStopMonitor();

                case "clean-registry":
                    return await HandleCleanRegistryAsync(req, sendChunk);

                case "clean-browser":
                    return await HandleCleanBrowserAsync(req, sendChunk);

                case "trim-ram":
                    return await HandleTrimRamAsync(sendChunk);

                case "get-startup":
                    return HandleGetStartup();

                case "toggle-startup":
                    return HandleToggleStartup(req);

                case "set-service":
                    return HandleSetService(req);

                case "shred":
                    return HandleShred(req);

                case "get-debloat-items":
                    return HandleGetDebloatItems();

                case "apply-debloat":
                    return HandleApplyDebloat(req);

                case "get-backups":
                    return HandleGetBackups();

                case "create-snapshot":
                    return HandleCreateSnapshot();

                case "rollback":
                    return HandleRollback(req);

                case "delete-backup":
                    return HandleDeleteBackup(req);

                case "delete-all-backups":
                    return HandleDeleteAllBackups();

                case "get-stats":
                    return HandleGetStats();

                case "monitor-event":
                    return HandleMonitorEvent(req);

                case "update-winapp2":
                    return await HandleUpdateWinapp2Async(req);

                default:
                    return new IPCResponse(false, "Unknown command", null);
            }
        }

        private async Task<IPCResponse> HandleScanCleanAsync(IPCRequest req, Func<IPCStreamChunk, Task> sendChunk, string rulesFile)
        {
            await sendChunk(new IPCStreamChunk(false, 1, 25, "Đang kết nối IPC Named Pipe Engine & Kiểm tra cờ cấu hình...", null));
            string tweakMsg = "";
            if (req.Flags != null && req.Flags.Length > 0)
            {
                await sendChunk(new IPCStreamChunk(false, 2, 45, $"Đang áp dụng {req.Flags.Length} tinh chỉnh Native OS...", null));
                var tweaksEngine = new SystemTweaksEngine();
                var tweakRes = tweaksEngine.ExecuteTweaks(req.Flags, req.IsDryRun);
                if (tweakRes.TotalApplied > 0)
                {
                    tweakMsg = $" (Đã áp dụng {tweakRes.TotalApplied} tinh chỉnh HĐH: {string.Join(", ", tweakRes.Messages)})";
                }
            }
            await sendChunk(new IPCStreamChunk(false, 3, 70, "Đang thực thi quét song song các thư mục rác hệ thống (Temp, Prefetch)...", null));
            var report = await ScanHelper.PerformScanAsync(req.IsDryRun, req.RulesFile ?? rulesFile);
            await sendChunk(new IPCStreamChunk(false, 4, 95, "Đang tổng hợp báo cáo và dọn dẹp bộ nhớ đệm...", null));
            return new IPCResponse(true, "Scan completed" + tweakMsg, JsonSerializer.Serialize(report, OptimaxJsonContext.Default.ScanReport));
        }

        private static IPCResponse HandleScheduleDaily(IPCRequest req)
        {
            string timeStr = (req.Flags != null && req.Flags.Length > 0) ? req.Flags[0] : "03:00";
            if (TimeSpan.TryParse(timeStr, out TimeSpan ts))
            {
                bool ok = TaskSchedulerEngine.ScheduleDaily(ts, req.IsDryRun);
                return new IPCResponse(ok, ok ? $"Scheduled daily task at {ts}" : "Failed to schedule daily task", null);
            }
            return new IPCResponse(false, "Invalid time format", null);
        }

        private static IPCResponse HandleScheduleWeekly(IPCRequest req)
        {
            string dayStr = (req.Flags != null && req.Flags.Length > 0) ? req.Flags[0] : "Sunday";
            string timeStr = (req.Flags != null && req.Flags.Length > 1) ? req.Flags[1] : "03:00";
            if (Enum.TryParse<DayOfWeek>(dayStr, true, out var day) && TimeSpan.TryParse(timeStr, out TimeSpan ts))
            {
                bool ok = TaskSchedulerEngine.ScheduleWeekly(day, ts, req.IsDryRun);
                return new IPCResponse(ok, ok ? $"Scheduled weekly task on {day} at {ts}" : "Failed to schedule weekly task", null);
            }
            return new IPCResponse(false, "Invalid day or time format", null);
        }

        private static IPCResponse HandleUnschedule()
        {
            bool ok = TaskSchedulerEngine.Unschedule();
            return new IPCResponse(ok, ok ? "Unscheduled daily task" : "Failed to unschedule task", null);
        }

        private IPCResponse HandleStartMonitor()
        {
            _activeMonitorCts?.Cancel();
            _activeMonitorCts = new CancellationTokenSource();
            var token = _activeMonitorCts.Token;
            _ = Task.Run(async () =>
            {
                var daemon = new RealtimeMonitorDaemon();
                await daemon.StartMonitoringAsync(token);
            }, token);
            return new IPCResponse(true, "Real-time Event Monitoring Daemon started", null);
        }

        private IPCResponse HandleStopMonitor()
        {
            _activeMonitorCts?.Cancel();
            _activeMonitorCts = null;
            return new IPCResponse(true, "Real-time Event Monitoring Daemon stopped", null);
        }

        private static async Task<IPCResponse> HandleCleanRegistryAsync(IPCRequest req, Func<IPCStreamChunk, Task> sendChunk)
        {
            await sendChunk(new IPCStreamChunk(false, 1, 35, "Đang khởi tạo Deep Registry Scanner...", null));
            var regScanner = new DeepRegistryScanner();
            await sendChunk(new IPCStreamChunk(false, 2, 70, "Đang quét và dọn dẹp các mục Registry mồ côi...", null));
            var regReport = regScanner.ScanAndClean(req.IsDryRun);
            await sendChunk(new IPCStreamChunk(false, 3, 95, "Đang tạo bản sao lưu Snapshot Registry khôi phục...", null));
            return new IPCResponse(true, "Registry scan completed", JsonSerializer.Serialize(regReport, OptimaxJsonContext.Default.RegistryScanReport));
        }

        private static async Task<IPCResponse> HandleCleanBrowserAsync(IPCRequest req, Func<IPCStreamChunk, Task> sendChunk)
        {
            await sendChunk(new IPCStreamChunk(false, 1, 40, "Đang kiểm tra tiến trình trình duyệt (Chrome, Edge, Firefox, Brave)...", null));
            var bEngine = new BrowserOptimizer();
            await sendChunk(new IPCStreamChunk(false, 2, 75, "Đang thực thi Vacuum SQLite tối ưu dung lượng cơ sở dữ liệu...", null));
            var bReport = bEngine.OptimizeAllBrowsers(req.IsDryRun);
            await sendChunk(new IPCStreamChunk(false, 3, 95, "Hoàn tất giải phóng dung lượng dữ liệu trình duyệt!", null));
            return new IPCResponse(true, "Browser SQLite optimization completed", JsonSerializer.Serialize(bReport, OptimaxJsonContext.Default.BrowserScanReport));
        }

        private static async Task<IPCResponse> HandleTrimRamAsync(Func<IPCStreamChunk, Task> sendChunk)
        {
            await sendChunk(new IPCStreamChunk(false, 1, 45, "Đang gửi tín hiệu EmptyWorkingSet tới Kernel OS...", null));
            await sendChunk(new IPCStreamChunk(false, 2, 80, "Đang xả System Standby List bộ nhớ RAM via NtSetSystemInformation...", null));
            var trimReport = KernelMemoryTrimmer.TrimSystemMemory();
            await sendChunk(new IPCStreamChunk(false, 3, 95, "Hoàn tất thu hồi bộ nhớ RAM Kernel Native!", null));
            return new IPCResponse(true, "Kernel RAM trimming completed", JsonSerializer.Serialize(trimReport, OptimaxJsonContext.Default.MemoryTrimReport));
        }

        private static IPCResponse HandleGetStartup()
        {
            var sEngine = new StartupOptimizer();
            var sReport = sEngine.GetStartupAndServiceStatus();
            return new IPCResponse(true, "Startup items retrieved", JsonSerializer.Serialize(sReport, OptimaxJsonContext.Default.StartupOptimizerReport));
        }

        private static IPCResponse HandleToggleStartup(IPCRequest req)
        {
            if (string.IsNullOrEmpty(req.TargetId))
                return new IPCResponse(false, "Missing targetId", null);
            var sEngine = new StartupOptimizer();
            bool ok = sEngine.ToggleStartupItem(req.TargetId, req.Enable);
            return new IPCResponse(ok, ok ? "Startup item toggled" : "Failed to toggle startup item", null);
        }

        private static IPCResponse HandleSetService(IPCRequest req)
        {
            if (string.IsNullOrEmpty(req.TargetId))
                return new IPCResponse(false, "Missing targetId", null);
            var sEngine = new StartupOptimizer();
            bool ok = sEngine.SetServiceStartMode(req.TargetId, (ServiceStartMode)req.ServiceStartMode);
            return new IPCResponse(ok, ok ? "Service start mode updated" : "Failed to update service", null);
        }

        private static IPCResponse HandleShred(IPCRequest req)
        {
            if (string.IsNullOrEmpty(req.TargetId))
                return new IPCResponse(false, "Missing targetId", null);
            ShredAlgorithm algo = (req.Flags != null && req.Flags.Length > 0) ? req.Flags[0].ToLowerInvariant() switch
            {
                "zero" => ShredAlgorithm.ZeroFill,
                "random" => ShredAlgorithm.RandomFill,
                _ => ShredAlgorithm.DoD5220
            } : ShredAlgorithm.DoD5220;
            var sReport = SecureFileShredder.ShredTarget(req.TargetId, algo);
            return new IPCResponse(sReport.Success, sReport.StatusMessage, JsonSerializer.Serialize(sReport, OptimaxJsonContext.Default.ShredReport));
        }

        private static IPCResponse HandleGetDebloatItems()
        {
            var debloater = new WindowsDebloater();
            var items = debloater.GetAvailableDebloatItems();
            return new IPCResponse(true, "Debloat items retrieved", JsonSerializer.Serialize(items, OptimaxJsonContext.Default.ListDebloatItemDto));
        }

        private static IPCResponse HandleApplyDebloat(IPCRequest req)
        {
            var debloater = new WindowsDebloater();
            string[] targetIds = req.Flags ?? Array.Empty<string>();
            var dReport = debloater.ApplyDebloatItems(targetIds, req.IsDryRun);
            return new IPCResponse(dReport.Success, $"Applied {dReport.TotalApplied} debloat items", JsonSerializer.Serialize(dReport, OptimaxJsonContext.Default.DebloatReport));
        }

        private static IPCResponse HandleGetBackups()
        {
            var rollbackMgr = new TransactionalRollbackManager();
            var backups = rollbackMgr.GetAvailableBackups();
            return new IPCResponse(true, "Retrieved available backups", JsonSerializer.Serialize(backups, OptimaxJsonContext.Default.ListBackupItemDto));
        }

        private static IPCResponse HandleCreateSnapshot()
        {
            var rollbackMgr = new TransactionalRollbackManager();
            string bId = rollbackMgr.CreateSystemSnapshot();
            return new IPCResponse(true, $"System snapshot created successfully (ID: {bId})", bId);
        }

        private static IPCResponse HandleRollback(IPCRequest req)
        {
            if (string.IsNullOrEmpty(req.BackupId))
                return new IPCResponse(false, "Missing backupId", null);
            var rollbackMgr = new TransactionalRollbackManager();
            bool ok = rollbackMgr.ExecuteRollback(req.BackupId);
            return new IPCResponse(ok, ok ? "Rollback succeeded" : "Rollback failed", null);
        }

        private static IPCResponse HandleDeleteBackup(IPCRequest req)
        {
            if (string.IsNullOrEmpty(req.BackupId))
                return new IPCResponse(false, "Missing backupId", null);
            var rollbackMgr = new TransactionalRollbackManager();
            bool ok = rollbackMgr.DeleteBackup(req.BackupId);
            return new IPCResponse(true, ok ? $"Đã xóa thành công Snapshot ID [{req.BackupId}]" : $"Snapshot ID [{req.BackupId}] đã được xóa trước đó", null);
        }

        private static IPCResponse HandleDeleteAllBackups()
        {
            var rollbackMgr = new TransactionalRollbackManager();
            int count = rollbackMgr.DeleteAllBackups();
            return new IPCResponse(true, $"Đã xóa toàn bộ {count} bản sao lưu Snapshot hệ thống.", count.ToString());
        }

        private static IPCResponse HandleGetStats()
        {
            var stats = SystemStatsHelper.GetSystemStats();
            return new IPCResponse(true, "System stats retrieved", JsonSerializer.Serialize(stats, OptimaxJsonContext.Default.SystemStatsReport));
        }

        private static IPCResponse HandleMonitorEvent(IPCRequest req)
        {
            string alertMsg = req.TargetId ?? "Junk threshold exceeded";
            OptimaxLogger.Warn($"[REALTIME DAEMON ALERT] {alertMsg}");

            var tweaksEngine = new SystemTweaksEngine();
            var tweakRes = tweaksEngine.ExecuteTweaks(new[] { "-systemp", "-standbyram" }, isDryRun: false);

            string summary = $"[REALTIME AUTO-CLEAN] Executed threshold auto-clean ({alertMsg}). Applied {tweakRes.TotalApplied} optimizations.";
            OptimaxLogger.Warn(summary);

            return new IPCResponse(true, summary, null);
        }

        private static async Task<IPCResponse> HandleUpdateWinapp2Async(IPCRequest req)
        {
            var report = await Winapp2Updater.UpdateAsync(isDryRun: req.IsDryRun);
            return new IPCResponse(report.Success, report.Message, JsonSerializer.Serialize(report, OptimaxJsonContext.Default.Winapp2UpdateReport));
        }
    }

    /// <summary>
    /// Shared scan helper used by both CLI and IPC modes.
    /// </summary>
    public static class ScanHelper
    {
        public static async Task<ScanReport> PerformScanAsync(bool isDryRun, string rulesFilePath)
        {
            var targetDirs = new[]
            {
                "%TEMP%",
                "C:\\Windows\\Temp",
                "C:\\Windows\\Prefetch",
                "C:\\ProgramData\\Microsoft\\Windows\\WER\\Temp"
            };

            var matchedFilesList = new List<string>();
            var ruleEngine = new DynamicRuleEngine();
            if (File.Exists(rulesFilePath))
            {
                try
                {
                    string rulesJson = await File.ReadAllTextAsync(rulesFilePath);
                    var rules = ruleEngine.LoadRules(rulesJson);
                    foreach (var r in rules)
                    {
                        matchedFilesList.AddRange(ruleEngine.ResolveMatchedFiles(r));
                    }
                }
                catch (Exception ex) { OptimaxLogger.Trace($"Failed to load custom rules from: {rulesFilePath}", ex); }
            }

            // Create Transactional Rollback Package before scanning/cleaning
            var rollbackMgr = new TransactionalRollbackManager();
            var backupPkg = rollbackMgr.CreatePackage();
            rollbackMgr.SnapshotRegistryKey(backupPkg, Microsoft.Win32.Registry.CurrentUser, "Software\\Microsoft\\Windows\\CurrentVersion\\Explorer\\RunMRU", "");
            string backupId = rollbackMgr.PersistPackage(backupPkg);

            var scanner = new ParallelScanner();
            return await scanner.ExecuteScanAsync(targetDirs, isDryRun, matchedFilesList);
        }
    }

    /// <summary>
    /// System stats collection helper — moved from Program.cs.
    /// </summary>
    public static class SystemStatsHelper
    {
        [System.Runtime.InteropServices.DllImport("kernel32.dll", SetLastError = true)]
        [return: System.Runtime.InteropServices.MarshalAs(System.Runtime.InteropServices.UnmanagedType.Bool)]
        private static extern bool GetSystemTimes(out System.Runtime.InteropServices.ComTypes.FILETIME lpIdleTime, out System.Runtime.InteropServices.ComTypes.FILETIME lpKernelTime, out System.Runtime.InteropServices.ComTypes.FILETIME lpUserTime);

        private static long _prevIdleTicks = 0;
        private static long _prevTotalTicks = 0;

        public static int GetRealCpuUsage()
        {
            try
            {
                if (GetSystemTimes(out var idleTime, out var kernelTime, out var userTime))
                {
                    long idle = ((long)idleTime.dwHighDateTime << 32) | (uint)idleTime.dwLowDateTime;
                    long kernel = ((long)kernelTime.dwHighDateTime << 32) | (uint)kernelTime.dwLowDateTime;
                    long user = ((long)userTime.dwHighDateTime << 32) | (uint)userTime.dwLowDateTime;

                    long total = kernel + user;

                    long prevIdle = System.Threading.Interlocked.Exchange(ref _prevIdleTicks, idle);
                    long prevTotal = System.Threading.Interlocked.Exchange(ref _prevTotalTicks, total);

                    if (prevTotal > 0)
                    {
                        long totalDiff = total - prevTotal;
                        long idleDiff = idle - prevIdle;

                        if (totalDiff > 0)
                        {
                            long busyDiff = totalDiff - idleDiff;
                            int pct = (int)(busyDiff * 100 / totalDiff);
                            return Math.Clamp(pct, 0, 100);
                        }
                    }
                }
            }
            catch (Exception ex) { OptimaxLogger.Trace("GetSystemTimes CPU measurement failed", ex); }
            return 12;
        }

        public static string GetActivePowerPlanName()
        {
            try
            {
                using var key = Microsoft.Win32.Registry.LocalMachine.OpenSubKey(@"SYSTEM\CurrentControlSet\Control\Power\User\PowerSchemes");
                if (key != null)
                {
                    string? activeGuid = key.GetValue("ActivePowerScheme") as string;
                    if (!string.IsNullOrEmpty(activeGuid))
                    {
                        using var schemeKey = key.OpenSubKey(activeGuid);
                        string? name = schemeKey?.GetValue("FriendlyName") as string;
                        if (!string.IsNullOrEmpty(name))
                        {
                            if (activeGuid.Equals("8c5e7fda-e8bf-4a96-9a85-a6e23a8c635c", StringComparison.OrdinalIgnoreCase)) return "HIGH PERFORMANCE";
                            if (activeGuid.Equals("381b4222-f694-41f0-9685-ff5bb260df2e", StringComparison.OrdinalIgnoreCase)) return "BALANCED";
                            if (activeGuid.Equals("a1841308-3541-4fab-bc81-f71556f20b4a", StringComparison.OrdinalIgnoreCase)) return "POWER SAVER";
                            if (activeGuid.Equals("e9a42b02-d5df-448d-aa00-03f14749eb61", StringComparison.OrdinalIgnoreCase)) return "ULTIMATE PERFORMANCE";
                            return name.Trim();
                        }
                    }
                }
            }
            catch (Exception ex) { OptimaxLogger.Trace("Active power plan registry read failed", ex); }
            return "BALANCED";
        }

        public static SystemStatsReport GetSystemStats()
        {
            double ramTotalGB = 0;
            double ramFreeGB = 0;
            int ramUsagePct = 0;

            try
            {
                Optimax.Core.MEMORYSTATUSEX memStatus = default;
                memStatus.Init();
                if (Optimax.Core.ScmServiceManager.GlobalMemoryStatusEx(ref memStatus))
                {
                    ramTotalGB = Math.Round((double)memStatus.ullTotalPhys / (1024 * 1024 * 1024), 2);
                    ramFreeGB = Math.Round((double)memStatus.ullAvailPhys / (1024 * 1024 * 1024), 2);
                    ramUsagePct = (int)memStatus.dwMemoryLoad;
                }
            }
            catch (Exception ex) { OptimaxLogger.Trace("GlobalMemoryStatusEx failed in GetSystemStats", ex); }

            double diskTotalGB = 0;
            double diskFreeGB = 0;
            int diskUsedPct = 0;

            try
            {
                var drive = new DriveInfo("C");
                if (drive.IsReady)
                {
                    diskTotalGB = Math.Round((double)drive.TotalSize / (1024 * 1024 * 1024), 1);
                    diskFreeGB = Math.Round((double)drive.AvailableFreeSpace / (1024 * 1024 * 1024), 1);
                    diskUsedPct = (int)Math.Round((double)(drive.TotalSize - drive.AvailableFreeSpace) / drive.TotalSize * 100);
                }
            }
            catch (Exception ex) { OptimaxLogger.Trace("Disk info read failed for drive C:", ex); }

            bool isAdmin = false;
            try
            {
                using var identity = System.Security.Principal.WindowsIdentity.GetCurrent();
                var principal = new System.Security.Principal.WindowsPrincipal(identity);
                isAdmin = principal.IsInRole(System.Security.Principal.WindowsBuiltInRole.Administrator);
            }
            catch (Exception ex) { OptimaxLogger.Trace("Admin role check failed", ex); }

            return new SystemStatsReport(
                CpuUsagePct: GetRealCpuUsage(),
                RamUsagePct: ramUsagePct,
                RamFreeGB: ramFreeGB,
                RamTotalGB: ramTotalGB,
                DiskFreeGB: diskFreeGB,
                DiskTotalGB: diskTotalGB,
                DiskUsedPct: diskUsedPct,
                PowerPlan: GetActivePowerPlanName(),
                Hostname: Environment.MachineName,
                IsAdmin: isAdmin
            );
        }
    }
}
