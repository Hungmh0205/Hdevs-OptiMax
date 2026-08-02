using System;
using System.IO;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Optimax.Core;
using Optimax.IPC;

namespace Optimax
{
    internal class Program
    {
        private static async Task<int> Main(string[] args)
        {
            try
            {
                Console.OutputEncoding = System.Text.Encoding.UTF8;
                Console.InputEncoding = System.Text.Encoding.UTF8;
            }
            catch
            {
                // Console encoding setup is non-critical
            }

            var opts = CliCommandRouter.ParseArguments(args);

            // 0. WinApp2.ini Importer Mode
            if (!string.IsNullOrEmpty(opts.ImportWinApp2Path))
            {
                Console.WriteLine($"[OPTIMAX NATIVE] Importing WinApp2.ini ruleset from '{opts.ImportWinApp2Path}'...");
                var parser = new WinApp2IniParser();
                var parsedRules = parser.ParseIniFile(opts.ImportWinApp2Path);
                Console.WriteLine($"[WINAPP2 IMPORTER] Successfully parsed {parsedRules.Count} application cleaning rules!");

                string targetJson = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "rules", "custom_rules.json");
                Directory.CreateDirectory(Path.GetDirectoryName(targetJson)!);

                string json = JsonSerializer.Serialize(parsedRules, OptimaxJsonContext.Default.ListDynamicCleaningRule);
                File.WriteAllText(targetJson, json);
                Console.WriteLine($"[WINAPP2 IMPORTER] Saved merged ruleset to '{targetJson}'.");
                return 0;
            }

            // Auto-update Winapp2.ini CLI Mode
            if (opts.IsUpdateWinapp2)
            {
                Console.WriteLine("[OPTIMAX NATIVE] Checking and updating Winapp2.ini ruleset from GitHub community repository...");
                var updateReport = await Winapp2Updater.UpdateAsync(isDryRun: opts.IsDryRun);
                Console.WriteLine($"[WINAPP2 UPDATER] {updateReport.Message}");
                string json = JsonSerializer.Serialize(updateReport, OptimaxJsonContext.Default.Winapp2UpdateReport);
                Console.WriteLine(json);
                return updateReport.Success ? 0 : 1;
            }

            // 1. Task Scheduler Operations
            if (!string.IsNullOrEmpty(opts.ScheduleDailyTime))
            {
                if (TimeSpan.TryParse(opts.ScheduleDailyTime, out TimeSpan time))
                {
                    Console.WriteLine($"[OPTIMAX NATIVE] Scheduling daily task at {time}...");
                    bool ok = TaskSchedulerEngine.ScheduleDaily(time, opts.IsDryRun);
                    Console.WriteLine(ok ? "[TASK SCHEDULER] Daily task scheduled successfully!" : "[TASK SCHEDULER] Failed to schedule daily task.");
                    return ok ? 0 : 1;
                }
                Console.Error.WriteLine("[TASK SCHEDULER] Invalid time format. Use HH:mm format (e.g. 03:00).");
                return 1;
            }

            if (!string.IsNullOrEmpty(opts.ScheduleWeeklyTime) && !string.IsNullOrEmpty(opts.ScheduleWeeklyDay))
            {
                if (Enum.TryParse<DayOfWeek>(opts.ScheduleWeeklyDay, true, out var day) && TimeSpan.TryParse(opts.ScheduleWeeklyTime, out TimeSpan time))
                {
                    Console.WriteLine($"[OPTIMAX NATIVE] Scheduling weekly task on {day} at {time}...");
                    bool ok = TaskSchedulerEngine.ScheduleWeekly(day, time, opts.IsDryRun);
                    Console.WriteLine(ok ? "[TASK SCHEDULER] Weekly task scheduled successfully!" : "[TASK SCHEDULER] Failed to schedule weekly task.");
                    return ok ? 0 : 1;
                }
                Console.Error.WriteLine("[TASK SCHEDULER] Invalid day or time format (e.g. Sunday 03:00).");
                return 1;
            }

            if (opts.IsUnschedule)
            {
                Console.WriteLine("[OPTIMAX NATIVE] Removing Optimax Scheduled Task...");
                bool ok = TaskSchedulerEngine.Unschedule();
                Console.WriteLine(ok ? "[TASK SCHEDULER] Task unscheduled successfully!" : "[TASK SCHEDULER] Failed to unschedule task.");
                return ok ? 0 : 1;
            }

            if (opts.IsGetStats)
            {
                var stats = SystemStatsHelper.GetSystemStats();
                string json = JsonSerializer.Serialize(stats, OptimaxJsonContext.Default.SystemStatsReport);
                Console.WriteLine(json);
                return 0;
            }

            if (opts.IsGetBackups)
            {
                var rollbackMgr = new TransactionalRollbackManager();
                var backups = rollbackMgr.GetAvailableBackups();
                string json = JsonSerializer.Serialize(backups, OptimaxJsonContext.Default.ListBackupItemDto);
                Console.WriteLine(json);
                return 0;
            }

            if (opts.IsCreateSnapshot)
            {
                var rollbackMgr = new TransactionalRollbackManager();
                string bId = rollbackMgr.CreateSystemSnapshot();
                Console.WriteLine(bId);
                return 0;
            }

            // 2. Kernel RAM Trimmer Mode
            if (opts.IsTrimRam)
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
            if (opts.IsMonitor)
            {
                Console.WriteLine("[OPTIMAX NATIVE] Starting Real-time Event Monitoring Daemon...");
                var cts = new CancellationTokenSource();
                Console.CancelKeyPress += (s, e) => { e.Cancel = true; cts.Cancel(); };
                var daemon = new RealtimeMonitorDaemon();
                await daemon.StartMonitoringAsync(cts.Token);
                return 0;
            }

            // 4. Deep Safe Registry Cleaner Mode
            if (opts.IsCleanRegistry)
            {
                Console.WriteLine($"[OPTIMAX NATIVE] Executing Deep Safe Registry Scan (Dry-Run = {opts.IsDryRun})...");
                var regScanner = new DeepRegistryScanner();
                var regReport = regScanner.ScanAndClean(opts.IsDryRun);
                Console.WriteLine($"[OPTIMAX NATIVE] Đã dọn dẹp {regReport.TotalIssuesFound} mục Registry mồ côi (System Hygiene).");
                string json = JsonSerializer.Serialize(regReport, OptimaxJsonContext.Default.RegistryScanReport);
                Console.WriteLine("---PAYLOAD_START---");
                Console.WriteLine(json);
                Console.WriteLine("---PAYLOAD_END---");
                return 0;
            }

            // 5. Browser SQLite Optimization Mode
            if (opts.IsCleanBrowser)
            {
                Console.WriteLine($"[OPTIMAX NATIVE] Executing Browser SQLite Optimization (Dry-Run = {opts.IsDryRun})...");
                var bEngine = new BrowserOptimizer();
                var bReport = bEngine.OptimizeAllBrowsers(opts.IsDryRun);
                Console.WriteLine($"[OPTIMAX NATIVE] Đã tối ưu {bReport.TotalBytesReclaimed / 1024} KB trên {bReport.TotalDatabasesScanned} cơ sở dữ liệu trình duyệt.");
                string json = JsonSerializer.Serialize(bReport, OptimaxJsonContext.Default.BrowserScanReport);
                Console.WriteLine("---PAYLOAD_START---");
                Console.WriteLine(json);
                Console.WriteLine("---PAYLOAD_END---");
                return 0;
            }

            // 6. Startup & Service Manager List Mode
            if (opts.IsListStartup)
            {
                Console.WriteLine("[OPTIMAX NATIVE] Fetching Startup Items and Services...");
                var sEngine = new StartupOptimizer();
                var sReport = sEngine.GetStartupAndServiceStatus();
                string json = JsonSerializer.Serialize(sReport, OptimaxJsonContext.Default.StartupOptimizerReport);
                Console.WriteLine(json);
                return 0;
            }

            // 7. Secure File Shredder CLI Mode
            if (!string.IsNullOrEmpty(opts.ShredPath))
            {
                ShredAlgorithm algo = opts.ShredModeStr.ToLowerInvariant() switch
                {
                    "zero" => ShredAlgorithm.ZeroFill,
                    "random" => ShredAlgorithm.RandomFill,
                    _ => ShredAlgorithm.DoD5220
                };
                Console.WriteLine($"[OPTIMAX NATIVE] Shredding target '{opts.ShredPath}' using algorithm {algo}...");
                var report = SecureFileShredder.ShredTarget(opts.ShredPath, algo);
                string json = JsonSerializer.Serialize(report, OptimaxJsonContext.Default.ShredReport);
                Console.WriteLine(json);
                return report.Success ? 0 : 1;
            }

            // 8. Windows Debloater CLI Mode
            if (opts.IsDebloatList)
            {
                Console.WriteLine("[OPTIMAX NATIVE] Fetching available Windows Debloat items...");
                var debloater = new WindowsDebloater();
                var items = debloater.GetAvailableDebloatItems();
                string json = JsonSerializer.Serialize(items, OptimaxJsonContext.Default.ListDebloatItemDto);
                Console.WriteLine(json);
                return 0;
            }

            if (opts.IsDebloatApply)
            {
                Console.WriteLine($"[OPTIMAX NATIVE] Applying Windows Debloat tweaks (Dry-Run = {opts.IsDryRun})...");
                var debloater = new WindowsDebloater();
                var allItems = debloater.GetAvailableDebloatItems();
                var ids = new List<string>();
                foreach (var item in allItems) ids.Add(item.Id);
                var report = debloater.ApplyDebloatItems(ids.ToArray(), opts.IsDryRun);
                string json = JsonSerializer.Serialize(report, OptimaxJsonContext.Default.DebloatReport);
                Console.WriteLine(json);
                return 0;
            }

            // 9. Rollback Mode
            if (!string.IsNullOrEmpty(opts.RollbackId))
            {
                var rollbackMgr = new TransactionalRollbackManager();
                Console.WriteLine($"[OPTIMAX NATIVE] Initiating 1-Click Rollback for Backup ID: {opts.RollbackId}...");
                bool ok = rollbackMgr.ExecuteRollback(opts.RollbackId);
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

            if (!string.IsNullOrEmpty(opts.DeleteBackupId))
            {
                var rollbackMgr = new TransactionalRollbackManager();
                bool ok = rollbackMgr.DeleteBackup(opts.DeleteBackupId);
                if (ok)
                {
                    Console.WriteLine($"[OPTIMAX NATIVE] Đã xóa thành công bản sao lưu Snapshot ID [{opts.DeleteBackupId}].");
                }
                else
                {
                    Console.WriteLine($"[OPTIMAX NATIVE] Bản sao lưu Snapshot ID [{opts.DeleteBackupId}] không tồn tại hoặc đã được xóa trước đó.");
                }
                return 0;
            }

            if (opts.IsDeleteAllBackups)
            {
                var rollbackMgr = new TransactionalRollbackManager();
                int count = rollbackMgr.DeleteAllBackups();
                Console.WriteLine($"[OPTIMAX NATIVE] Đã xóa thành công toàn bộ {count} bản sao lưu Snapshot hệ thống.");
                return 0;
            }

            // 10. Named Pipe Service Mode
            if (opts.IsIpcService)
            {
                Console.WriteLine("[OPTIMAX NATIVE] Starting Secure Named Pipe IPC Service (\\\\.\\pipe\\OptimaxIPC)...");
                var cts = new CancellationTokenSource();
                Console.CancelKeyPress += (s, e) => { e.Cancel = true; cts.Cancel(); };

                var dispatcher = new IpcCommandDispatcher();
                var ipcServer = new NamedPipeServer();
                await ipcServer.StartServerStreamAsync((req, sendChunk) =>
                    dispatcher.HandleRequestAsync(req, sendChunk, opts.RulesFile, opts.IsDryRun),
                    cts.Token);

                return 0;
            }

            // 11. Direct CLI File System Scan & System Tweaks Mode (Default)
            Console.WriteLine($"[OPTIMAX NATIVE] Running Native System Optimizer (Dry-Run = {opts.IsDryRun})...");
            if (opts.CliFlags.Count > 0)
            {
                var tweaksEngine = new SystemTweaksEngine();
                var tweakRes = tweaksEngine.ExecuteTweaks(opts.CliFlags.ToArray(), opts.IsDryRun);
                if (tweakRes.TotalApplied > 0)
                {
                    Console.WriteLine($"[OPTIMAX NATIVE] Đã áp dụng {tweakRes.TotalApplied} tinh chỉnh hệ thống:");
                    foreach (var msg in tweakRes.Messages)
                    {
                        Console.WriteLine($" [✓] {msg}");
                    }
                }
            }

            var scanReport = await ScanHelper.PerformScanAsync(opts.IsDryRun, opts.RulesFile);

            string jsonReport = JsonSerializer.Serialize(scanReport, OptimaxJsonContext.Default.ScanReport);
            Console.WriteLine("---PAYLOAD_START---");
            Console.WriteLine(jsonReport);
            Console.WriteLine("---PAYLOAD_END---");

            return 0;
        }
    }
}
