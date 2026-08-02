using System;
using System.Collections.Generic;
using System.IO;
using System.ServiceProcess;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Optimax.Core;
using Optimax.IPC;

namespace Optimax
{
    internal class Program
    {
        private static CancellationTokenSource? _activeMonitorCts;

        private static async Task<int> Main(string[] args)
        {
            try
            {
                Console.OutputEncoding = System.Text.Encoding.UTF8;
                Console.InputEncoding = System.Text.Encoding.UTF8;
            }
            catch
            {
                // Console encoding setup is non-critical, expected to fail in some environments
            }

            bool isDryRun = false;
            string? rollbackId = null;
            string rulesFile = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "rules", "custom_rules.json");
            bool isIpcService = false;
            bool isCleanRegistry = false;
            bool isCleanBrowser = false;
            bool isListStartup = false;
            bool isMonitor = false;
            bool isTrimRam = false;
            string? scheduleDailyTime = null;
            string? scheduleWeeklyDay = null;
            string? scheduleWeeklyTime = null;
            bool isUnschedule = false;
            string? importWinApp2Path = null;
            bool isGetStats = false;

            string? shredPath = null;
            string shredModeStr = "dod";
            bool isDebloatList = false;
            bool isDebloatApply = false;

            var cliFlags = new List<string>();

            bool isGetBackups = false;
            bool isCreateSnapshot = false;
            string? deleteBackupId = null;

            for (int i = 0; i < args.Length; i++)
            {
                string arg = args[i].ToLowerInvariant();
                if (arg == "--flags" && i + 1 < args.Length)
                {
                    while (i + 1 < args.Length && !args[i + 1].StartsWith("--"))
                    {
                        cliFlags.Add(args[++i]);
                    }
                }
                else if (arg.StartsWith("-") && !arg.StartsWith("--"))
                {
                    cliFlags.Add(args[i]);
                }
                else if (arg == "--get-stats")
                {
                    isGetStats = true;
                }
                else if (arg == "--get-backups")
                {
                    isGetBackups = true;
                }
                else if (arg == "--create-snapshot")
                {
                    isCreateSnapshot = true;
                }
                else if (arg == "--delete-backup" && i + 1 < args.Length)
                {
                    deleteBackupId = args[++i];
                }
                else if (arg == "--import-winapp2" && i + 1 < args.Length)
                {
                    importWinApp2Path = args[++i];
                }
                else if (arg == "--dry-run")
                {
                    isDryRun = true;
                }
                else if (arg == "--rollback" && i + 1 < args.Length)
                {
                    rollbackId = args[++i];
                }
                else if (arg == "--rules" && i + 1 < args.Length)
                {
                    rulesFile = args[++i];
                }
                else if (arg == "--ipc-service")
                {
                    isIpcService = true;
                }
                else if (arg == "--clean-registry")
                {
                    isCleanRegistry = true;
                }
                else if (arg == "--clean-browser")
                {
                    isCleanBrowser = true;
                }
                else if (arg == "--list-startup")
                {
                    isListStartup = true;
                }
                else if (arg == "--monitor")
                {
                    isMonitor = true;
                }
                else if (arg == "--trim-ram")
                {
                    isTrimRam = true;
                }
                else if (arg == "--shred" && i + 1 < args.Length)
                {
                    shredPath = args[++i];
                }
                else if (arg == "--shred-mode" && i + 1 < args.Length)
                {
                    shredModeStr = args[++i];
                }
                else if (arg == "--debloat-list")
                {
                    isDebloatList = true;
                }
                else if (arg == "--debloat")
                {
                    isDebloatApply = true;
                }
                else if (arg == "--schedule-daily" && i + 1 < args.Length)
                {
                    scheduleDailyTime = args[++i];
                }
                else if (arg == "--schedule-weekly" && i + 2 < args.Length)
                {
                    scheduleWeeklyDay = args[++i];
                    scheduleWeeklyTime = args[++i];
                }
                else if (arg == "--unschedule")
                {
                    isUnschedule = true;
                }
            }

            // 0. WinApp2.ini Importer Mode
            if (!string.IsNullOrEmpty(importWinApp2Path))
            {
                Console.WriteLine($"[OPTIMAX NATIVE] Importing WinApp2.ini ruleset from '{importWinApp2Path}'...");
                var parser = new WinApp2IniParser();
                var parsedRules = parser.ParseIniFile(importWinApp2Path);
                Console.WriteLine($"[WINAPP2 IMPORTER] Successfully parsed {parsedRules.Count} application cleaning rules!");

                string targetJson = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "rules", "custom_rules.json");
                Directory.CreateDirectory(Path.GetDirectoryName(targetJson)!);

                string json = JsonSerializer.Serialize(parsedRules, OptimaxJsonContext.Default.ListDynamicCleaningRule);
                File.WriteAllText(targetJson, json);
                Console.WriteLine($"[WINAPP2 IMPORTER] Saved merged ruleset to '{targetJson}'.");
                return 0;
            }

            // 1. Task Scheduler Operations
            if (!string.IsNullOrEmpty(scheduleDailyTime))
            {
                if (TimeSpan.TryParse(scheduleDailyTime, out TimeSpan time))
                {
                    Console.WriteLine($"[OPTIMAX NATIVE] Scheduling daily task at {time}...");
                    bool ok = TaskSchedulerEngine.ScheduleDaily(time, isDryRun);
                    Console.WriteLine(ok ? "[TASK SCHEDULER] Daily task scheduled successfully!" : "[TASK SCHEDULER] Failed to schedule daily task.");
                    return ok ? 0 : 1;
                }
                Console.Error.WriteLine("[TASK SCHEDULER] Invalid time format. Use HH:mm format (e.g. 03:00).");
                return 1;
            }

            if (!string.IsNullOrEmpty(scheduleWeeklyTime) && !string.IsNullOrEmpty(scheduleWeeklyDay))
            {
                if (Enum.TryParse<DayOfWeek>(scheduleWeeklyDay, true, out var day) && TimeSpan.TryParse(scheduleWeeklyTime, out TimeSpan time))
                {
                    Console.WriteLine($"[OPTIMAX NATIVE] Scheduling weekly task on {day} at {time}...");
                    bool ok = TaskSchedulerEngine.ScheduleWeekly(day, time, isDryRun);
                    Console.WriteLine(ok ? "[TASK SCHEDULER] Weekly task scheduled successfully!" : "[TASK SCHEDULER] Failed to schedule weekly task.");
                    return ok ? 0 : 1;
                }
                Console.Error.WriteLine("[TASK SCHEDULER] Invalid day or time format (e.g. Sunday 03:00).");
                return 1;
            }

            if (isUnschedule)
            {
                Console.WriteLine("[OPTIMAX NATIVE] Removing Optimax Scheduled Task...");
                bool ok = TaskSchedulerEngine.Unschedule();
                Console.WriteLine(ok ? "[TASK SCHEDULER] Task unscheduled successfully!" : "[TASK SCHEDULER] Failed to unschedule task.");
                return ok ? 0 : 1;
            }

            if (isGetStats)
            {
                var stats = GetSystemStats();
                string json = JsonSerializer.Serialize(stats, OptimaxJsonContext.Default.SystemStatsReport);
                Console.WriteLine(json);
                return 0;
            }

            if (isGetBackups)
            {
                var rollbackMgr = new TransactionalRollbackManager();
                var backups = rollbackMgr.GetAvailableBackups();
                string json = JsonSerializer.Serialize(backups, OptimaxJsonContext.Default.ListBackupItemDto);
                Console.WriteLine(json);
                return 0;
            }

            if (isCreateSnapshot)
            {
                var rollbackMgr = new TransactionalRollbackManager();
                string bId = rollbackMgr.CreateSystemSnapshot();
                Console.WriteLine(bId);
                return 0;
            }

            // 2. Kernel RAM Trimmer Mode
            if (isTrimRam)
            {
                Console.WriteLine("[OPTIMAX NATIVE] Executing Kernel Memory Trimming & Standby List Purge...");
                var trimReport = KernelMemoryTrimmer.TrimSystemMemory();
                string json = JsonSerializer.Serialize(trimReport, OptimaxJsonContext.Default.MemoryTrimReport);
                Console.WriteLine("---PAYLOAD_START---");
                Console.WriteLine(json);
                Console.WriteLine("---PAYLOAD_END---");
                return 0;
            }

            // 3. Real-Time Monitor Mode
            if (isMonitor)
            {
                Console.WriteLine("[OPTIMAX NATIVE] Starting Real-time Event Monitoring Daemon...");
                var cts = new CancellationTokenSource();
                Console.CancelKeyPress += (s, e) => { e.Cancel = true; cts.Cancel(); };
                var daemon = new RealtimeMonitorDaemon();
                await daemon.StartMonitoringAsync(cts.Token);
                return 0;
            }

            // 4. Deep Safe Registry Cleaner Mode
            if (isCleanRegistry)
            {
                Console.WriteLine($"[OPTIMAX NATIVE] Executing Deep Safe Registry Scan (Dry-Run = {isDryRun})...");
                var regScanner = new DeepRegistryScanner();
                var regReport = regScanner.ScanAndClean(isDryRun);
                Console.WriteLine($"[OPTIMAX NATIVE] Đã dọn dẹp {regReport.TotalIssuesFound} mục Registry mồ côi.");
                string json = JsonSerializer.Serialize(regReport, OptimaxJsonContext.Default.RegistryScanReport);
                Console.WriteLine("---PAYLOAD_START---");
                Console.WriteLine(json);
                Console.WriteLine("---PAYLOAD_END---");
                return 0;
            }

            // 5. Browser SQLite Optimization Mode
            if (isCleanBrowser)
            {
                Console.WriteLine($"[OPTIMAX NATIVE] Executing Browser SQLite Optimization (Dry-Run = {isDryRun})...");
                var bEngine = new BrowserOptimizer();
                var bReport = bEngine.OptimizeAllBrowsers(isDryRun);
                Console.WriteLine($"[OPTIMAX NATIVE] Đã tối ưu {bReport.TotalBytesReclaimed / 1024} KB trên {bReport.TotalDatabasesScanned} cơ sở dữ liệu trình duyệt.");
                string json = JsonSerializer.Serialize(bReport, OptimaxJsonContext.Default.BrowserScanReport);
                Console.WriteLine("---PAYLOAD_START---");
                Console.WriteLine(json);
                Console.WriteLine("---PAYLOAD_END---");
                return 0;
            }

            // 6. Startup & Service Manager List Mode
            if (isListStartup)
            {
                Console.WriteLine("[OPTIMAX NATIVE] Fetching Startup Items and Services...");
                var sEngine = new StartupOptimizer();
                var sReport = sEngine.GetStartupAndServiceStatus();
                string json = JsonSerializer.Serialize(sReport, OptimaxJsonContext.Default.StartupOptimizerReport);
                Console.WriteLine(json);
                return 0;
            }

            // 7. Secure File Shredder CLI Mode
            if (!string.IsNullOrEmpty(shredPath))
            {
                ShredAlgorithm algo = shredModeStr.ToLowerInvariant() switch
                {
                    "zero" => ShredAlgorithm.ZeroFill,
                    "random" => ShredAlgorithm.RandomFill,
                    _ => ShredAlgorithm.DoD5220
                };
                Console.WriteLine($"[OPTIMAX NATIVE] Shredding target '{shredPath}' using algorithm {algo}...");
                var report = SecureFileShredder.ShredTarget(shredPath, algo);
                string json = JsonSerializer.Serialize(report, OptimaxJsonContext.Default.ShredReport);
                Console.WriteLine(json);
                return report.Success ? 0 : 1;
            }

            // 8. Windows Debloater CLI Mode
            if (isDebloatList)
            {
                Console.WriteLine("[OPTIMAX NATIVE] Fetching available Windows Debloat items...");
                var debloater = new WindowsDebloater();
                var items = debloater.GetAvailableDebloatItems();
                string json = JsonSerializer.Serialize(items, OptimaxJsonContext.Default.ListDebloatItemDto);
                Console.WriteLine(json);
                return 0;
            }

            if (isDebloatApply)
            {
                Console.WriteLine($"[OPTIMAX NATIVE] Applying Windows Debloat tweaks (Dry-Run = {isDryRun})...");
                var debloater = new WindowsDebloater();
                var allItems = debloater.GetAvailableDebloatItems();
                var ids = new List<string>();
                foreach (var item in allItems) ids.Add(item.Id);
                var report = debloater.ApplyDebloatItems(ids.ToArray(), isDryRun);
                string json = JsonSerializer.Serialize(report, OptimaxJsonContext.Default.DebloatReport);
                Console.WriteLine(json);
                return 0;
            }

            // 9. Rollback Mode
            if (!string.IsNullOrEmpty(rollbackId))
            {
                var rollbackMgr = new TransactionalRollbackManager();
                Console.WriteLine($"[OPTIMAX NATIVE] Initiating 1-Click Rollback for Backup ID: {rollbackId}...");
                bool ok = rollbackMgr.ExecuteRollback(rollbackId);
                if (ok)
                {
                    Console.WriteLine("[OPTIMAX NATIVE] Rollback completed successfully!");
                    return 0;
                }
                else
                {
                    Console.Error.WriteLine("[OPTIMAX NATIVE] Rollback failed or snapshot not found.");
                    return 1;
                }
            }

            if (!string.IsNullOrEmpty(deleteBackupId))
            {
                var rollbackMgr = new TransactionalRollbackManager();
                bool ok = rollbackMgr.DeleteBackup(deleteBackupId);
                if (ok)
                {
                    Console.WriteLine($"[OPTIMAX NATIVE] Đã xóa thành công bản sao lưu Snapshot ID [{deleteBackupId}].");
                }
                else
                {
                    Console.WriteLine($"[OPTIMAX NATIVE] Bản sao lưu Snapshot ID [{deleteBackupId}] không tồn tại hoặc đã được xóa trước đó.");
                }
                return 0;
            }

            // 10. Named Pipe Service Mode
            if (isIpcService)
            {
                Console.WriteLine("[OPTIMAX NATIVE] Starting Secure Named Pipe IPC Service (\\\\.\\pipe\\OptimaxIPC)...");
                var cts = new CancellationTokenSource();
                Console.CancelKeyPress += (s, e) => { e.Cancel = true; cts.Cancel(); };

                var ipcServer = new NamedPipeServer();
                await ipcServer.StartServerStreamAsync(async (req, sendChunk) =>
                {
                    if (req.Command == "scan" || req.Command == "clean")
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
                        var report = await PerformScanAsync(req.IsDryRun, req.RulesFile ?? rulesFile);
                        await sendChunk(new IPCStreamChunk(false, 4, 95, "Đang tổng hợp báo cáo và dọn dẹp bộ nhớ đệm...", null));
                        return new IPCResponse(true, "Scan completed" + tweakMsg, JsonSerializer.Serialize(report, OptimaxJsonContext.Default.ScanReport));
                    }
                    else if (req.Command == "schedule-daily")
                    {
                        string timeStr = (req.Flags != null && req.Flags.Length > 0) ? req.Flags[0] : "03:00";
                        if (TimeSpan.TryParse(timeStr, out TimeSpan ts))
                        {
                            bool ok = TaskSchedulerEngine.ScheduleDaily(ts, req.IsDryRun);
                            return new IPCResponse(ok, ok ? $"Scheduled daily task at {ts}" : "Failed to schedule daily task", null);
                        }
                        return new IPCResponse(false, "Invalid time format", null);
                    }
                    else if (req.Command == "schedule-weekly")
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
                    else if (req.Command == "unschedule")
                    {
                        bool ok = TaskSchedulerEngine.Unschedule();
                        return new IPCResponse(ok, ok ? "Unscheduled daily task" : "Failed to unschedule task", null);
                    }
                    else if (req.Command == "start-monitor")
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
                    else if (req.Command == "stop-monitor")
                    {
                        _activeMonitorCts?.Cancel();
                        _activeMonitorCts = null;
                        return new IPCResponse(true, "Real-time Event Monitoring Daemon stopped", null);
                    }
                    else if (req.Command == "clean-registry")
                    {
                        await sendChunk(new IPCStreamChunk(false, 1, 35, "Đang khởi tạo Deep Registry Scanner...", null));
                        var regScanner = new DeepRegistryScanner();
                        await sendChunk(new IPCStreamChunk(false, 2, 70, "Đang quét và dọn dẹp các mục Registry mồ côi...", null));
                        var regReport = regScanner.ScanAndClean(req.IsDryRun);
                        await sendChunk(new IPCStreamChunk(false, 3, 95, "Đang tạo bản sao lưu Snapshot Registry khôi phục...", null));
                        return new IPCResponse(true, "Registry scan completed", JsonSerializer.Serialize(regReport, OptimaxJsonContext.Default.RegistryScanReport));
                    }
                    else if (req.Command == "clean-browser")
                    {
                        await sendChunk(new IPCStreamChunk(false, 1, 40, "Đang kiểm tra tiến trình trình duyệt (Chrome, Edge, Firefox, Brave)...", null));
                        var bEngine = new BrowserOptimizer();
                        await sendChunk(new IPCStreamChunk(false, 2, 75, "Đang thực thi Vacuum SQLite tối ưu dung lượng cơ sở dữ liệu...", null));
                        var bReport = bEngine.OptimizeAllBrowsers(req.IsDryRun);
                        await sendChunk(new IPCStreamChunk(false, 3, 95, "Hoàn tất giải phóng dung lượng dữ liệu trình duyệt!", null));
                        return new IPCResponse(true, "Browser SQLite optimization completed", JsonSerializer.Serialize(bReport, OptimaxJsonContext.Default.BrowserScanReport));
                    }
                    else if (req.Command == "trim-ram")
                    {
                        await sendChunk(new IPCStreamChunk(false, 1, 45, "Đang gửi tín hiệu EmptyWorkingSet tới Kernel OS...", null));
                        await sendChunk(new IPCStreamChunk(false, 2, 80, "Đang xả System Standby List bộ nhớ RAM...", null));
                        var trimReport = KernelMemoryTrimmer.TrimSystemMemory();
                        await sendChunk(new IPCStreamChunk(false, 3, 95, "Hoàn tất thu hồi bộ nhớ RAM Kernel Native!", null));
                        return new IPCResponse(true, "Kernel RAM trimming completed", JsonSerializer.Serialize(trimReport, OptimaxJsonContext.Default.MemoryTrimReport));
                    }
                    else if (req.Command == "get-startup")
                    {
                        var sEngine = new StartupOptimizer();
                        var sReport = sEngine.GetStartupAndServiceStatus();
                        return new IPCResponse(true, "Startup items retrieved", JsonSerializer.Serialize(sReport, OptimaxJsonContext.Default.StartupOptimizerReport));
                    }
                    else if (req.Command == "toggle-startup" && !string.IsNullOrEmpty(req.TargetId))
                    {
                        var sEngine = new StartupOptimizer();
                        bool ok = sEngine.ToggleStartupItem(req.TargetId, req.Enable);
                        return new IPCResponse(ok, ok ? "Startup item toggled" : "Failed to toggle startup item", null);
                    }
                    else if (req.Command == "set-service" && !string.IsNullOrEmpty(req.TargetId))
                    {
                        var sEngine = new StartupOptimizer();
                        bool ok = sEngine.SetServiceStartMode(req.TargetId, (ServiceStartMode)req.ServiceStartMode);
                        return new IPCResponse(ok, ok ? "Service start mode updated" : "Failed to update service", null);
                    }
                    else if (req.Command == "shred" && !string.IsNullOrEmpty(req.TargetId))
                    {
                        ShredAlgorithm algo = (req.Flags != null && req.Flags.Length > 0) ? req.Flags[0].ToLowerInvariant() switch
                        {
                            "zero" => ShredAlgorithm.ZeroFill,
                            "random" => ShredAlgorithm.RandomFill,
                            _ => ShredAlgorithm.DoD5220
                        } : ShredAlgorithm.DoD5220;

                        var sReport = SecureFileShredder.ShredTarget(req.TargetId, algo);
                        return new IPCResponse(sReport.Success, sReport.StatusMessage, JsonSerializer.Serialize(sReport, OptimaxJsonContext.Default.ShredReport));
                    }
                    else if (req.Command == "get-debloat-items")
                    {
                        var debloater = new WindowsDebloater();
                        var items = debloater.GetAvailableDebloatItems();
                        return new IPCResponse(true, "Debloat items retrieved", JsonSerializer.Serialize(items, OptimaxJsonContext.Default.ListDebloatItemDto));
                    }
                    else if (req.Command == "apply-debloat")
                    {
                        var debloater = new WindowsDebloater();
                        string[] targetIds = req.Flags ?? Array.Empty<string>();
                        var dReport = debloater.ApplyDebloatItems(targetIds, req.IsDryRun);
                        return new IPCResponse(dReport.Success, $"Applied {dReport.TotalApplied} debloat items", JsonSerializer.Serialize(dReport, OptimaxJsonContext.Default.DebloatReport));
                    }
                    else if (req.Command == "get-backups")
                    {
                        var rollbackMgr = new TransactionalRollbackManager();
                        var backups = rollbackMgr.GetAvailableBackups();
                        return new IPCResponse(true, "Retrieved available backups", JsonSerializer.Serialize(backups, OptimaxJsonContext.Default.ListBackupItemDto));
                    }
                    else if (req.Command == "create-snapshot")
                    {
                        var rollbackMgr = new TransactionalRollbackManager();
                        string bId = rollbackMgr.CreateSystemSnapshot();
                        return new IPCResponse(true, $"System snapshot created successfully (ID: {bId})", bId);
                    }
                    else if (req.Command == "rollback" && !string.IsNullOrEmpty(req.BackupId))
                    {
                        var rollbackMgr = new TransactionalRollbackManager();
                        bool ok = rollbackMgr.ExecuteRollback(req.BackupId);
                        return new IPCResponse(ok, ok ? "Rollback succeeded" : "Rollback failed", null);
                    }
                    else if (req.Command == "delete-backup" && !string.IsNullOrEmpty(req.BackupId))
                    {
                        var rollbackMgr = new TransactionalRollbackManager();
                        bool ok = rollbackMgr.DeleteBackup(req.BackupId);
                        return new IPCResponse(true, ok ? $"Đã xóa thành công Snapshot ID [{req.BackupId}]" : $"Snapshot ID [{req.BackupId}] đã được xóa trước đó", null);
                    }
                    else if (req.Command == "get-stats")
                    {
                        var stats = GetSystemStats();
                        return new IPCResponse(true, "System stats retrieved", JsonSerializer.Serialize(stats, OptimaxJsonContext.Default.SystemStatsReport));
                    }
                    else if (req.Command == "monitor-event")
                    {
                        string alertMsg = req.TargetId ?? "Junk threshold exceeded";
                        OptimaxLogger.Warn($"[REALTIME DAEMON ALERT] {alertMsg}");

                        var tweaksEngine = new SystemTweaksEngine();
                        var tweakRes = tweaksEngine.ExecuteTweaks(new[] { "-systemp", "-standbyram" }, isDryRun: false);

                        string summary = $"[REALTIME AUTO-CLEAN] Executed threshold auto-clean ({alertMsg}). Applied {tweakRes.TotalApplied} optimizations.";
                        OptimaxLogger.Warn(summary);

                        return new IPCResponse(true, summary, null);
                    }
                    return new IPCResponse(false, "Unknown command", null);
                }, cts.Token);

                return 0;
            }

            // 11. Direct CLI File System Scan & System Tweaks Mode (Default)
            Console.WriteLine($"[OPTIMAX NATIVE] Running Native System Optimizer (Dry-Run = {isDryRun})...");
            if (cliFlags.Count > 0)
            {
                var tweaksEngine = new SystemTweaksEngine();
                var tweakRes = tweaksEngine.ExecuteTweaks(cliFlags.ToArray(), isDryRun);
                if (tweakRes.TotalApplied > 0)
                {
                    Console.WriteLine($"[OPTIMAX NATIVE] Đã áp dụng {tweakRes.TotalApplied} tinh chỉnh hệ thống:");
                    foreach (var msg in tweakRes.Messages)
                    {
                        Console.WriteLine($" [✓] {msg}");
                    }
                }
            }

            var scanReport = await PerformScanAsync(isDryRun, rulesFile);

            string jsonReport = JsonSerializer.Serialize(scanReport, OptimaxJsonContext.Default.ScanReport);
            Console.WriteLine("---PAYLOAD_START---");
            Console.WriteLine(jsonReport);
            Console.WriteLine("---PAYLOAD_END---");

            return 0;
        }

        private static async Task<ScanReport> PerformScanAsync(bool isDryRun, string rulesFilePath)
        {
            var targetDirs = new[]
            {
                "%TEMP%",
                "C:\\Windows\\Temp",
                "C:\\Windows\\Prefetch",
                "C:\\ProgramData\\Microsoft\\Windows\\WER\\Temp"
            };

            var matchedFilesList = new System.Collections.Generic.List<string>();
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



        [System.Runtime.InteropServices.DllImport("kernel32.dll", SetLastError = true)]
        [return: System.Runtime.InteropServices.MarshalAs(System.Runtime.InteropServices.UnmanagedType.Bool)]
        private static extern bool GetSystemTimes(out System.Runtime.InteropServices.ComTypes.FILETIME lpIdleTime, out System.Runtime.InteropServices.ComTypes.FILETIME lpKernelTime, out System.Runtime.InteropServices.ComTypes.FILETIME lpUserTime);

        private static ulong _prevIdle = 0;
        private static ulong _prevKernel = 0;
        private static ulong _prevUser = 0;

        private static int GetRealCpuUsage()
        {
            try
            {
                if (GetSystemTimes(out var idleTime, out var kernelTime, out var userTime))
                {
                    ulong idle = ((ulong)idleTime.dwHighDateTime << 32) | (uint)idleTime.dwLowDateTime;
                    ulong kernel = ((ulong)kernelTime.dwHighDateTime << 32) | (uint)kernelTime.dwLowDateTime;
                    ulong user = ((ulong)userTime.dwHighDateTime << 32) | (uint)userTime.dwLowDateTime;

                    if (_prevIdle != 0)
                    {
                        ulong idleDiff = idle - _prevIdle;
                        ulong kernelDiff = kernel - _prevKernel;
                        ulong userDiff = user - _prevUser;

                        ulong totalDiff = kernelDiff + userDiff;
                        if (totalDiff > 0)
                        {
                            ulong busyDiff = totalDiff - idleDiff;
                            int pct = (int)(busyDiff * 100 / totalDiff);
                            _prevIdle = idle; _prevKernel = kernel; _prevUser = user;
                            return Math.Clamp(pct, 0, 100);
                        }
                    }

                    _prevIdle = idle; _prevKernel = kernel; _prevUser = user;
                }
            }
            catch (Exception ex) { OptimaxLogger.Trace("GetSystemTimes CPU measurement failed", ex); }
            return 12;
        }

        private static string GetActivePowerPlanName()
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

        private static SystemStatsReport GetSystemStats()
        {
            double ramTotalGB = 0;
            double ramFreeGB = 0;
            int ramUsagePct = 0;

            try
            {
                Optimax.Core.MEMORYSTATUSEX memStatus = new Optimax.Core.MEMORYSTATUSEX();
                if (Optimax.Core.ScmServiceManager.GlobalMemoryStatusEx(memStatus))
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
