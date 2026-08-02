using System;
using System.Collections.Generic;

namespace Optimax.Core
{
    /// <summary>
    /// Parsed CLI options from command line arguments.
    /// Extracted from Program.cs to decouple argument parsing from execution logic.
    /// </summary>
    public class CliOptions
    {
        public bool IsDryRun { get; set; }
        public string? RollbackId { get; set; }
        public string RulesFile { get; set; } = "";
        public bool IsIpcService { get; set; }
        public bool IsCleanRegistry { get; set; }
        public bool IsCleanBrowser { get; set; }
        public bool IsListStartup { get; set; }
        public bool IsMonitor { get; set; }
        public bool IsTrimRam { get; set; }
        public string? ScheduleDailyTime { get; set; }
        public string? ScheduleWeeklyDay { get; set; }
        public string? ScheduleWeeklyTime { get; set; }
        public bool IsUnschedule { get; set; }
        public string? ImportWinApp2Path { get; set; }
        public bool IsGetStats { get; set; }
        public string? ShredPath { get; set; }
        public string ShredModeStr { get; set; } = "dod";
        public bool IsDebloatList { get; set; }
        public bool IsDebloatApply { get; set; }
        public List<string> CliFlags { get; set; } = new();
        public bool IsGetBackups { get; set; }
        public bool IsCreateSnapshot { get; set; }
        public string? DeleteBackupId { get; set; }
        public bool IsDeleteAllBackups { get; set; }
        public bool IsUpdateWinapp2 { get; set; }
    }

    /// <summary>
    /// CLI argument parser — converts raw string[] args into a structured CliOptions object.
    /// Separated from Program.cs to reduce main entry point size and improve testability.
    /// </summary>
    public static class CliCommandRouter
    {
        public static CliOptions ParseArguments(string[] args)
        {
            var opts = new CliOptions();
            opts.RulesFile = System.IO.Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "rules", "custom_rules.json");

            for (int i = 0; i < args.Length; i++)
            {
                string arg = args[i].ToLowerInvariant();

                if (arg == "--flags" && i + 1 < args.Length)
                {
                    while (i + 1 < args.Length && !args[i + 1].StartsWith("--"))
                    {
                        opts.CliFlags.Add(args[++i]);
                    }
                }
                else if (arg.StartsWith("-") && !arg.StartsWith("--"))
                {
                    opts.CliFlags.Add(args[i]);
                }
                else if (arg == "--get-stats") opts.IsGetStats = true;
                else if (arg == "--get-backups") opts.IsGetBackups = true;
                else if (arg == "--create-snapshot") opts.IsCreateSnapshot = true;
                else if (arg == "--delete-backup" && i + 1 < args.Length) opts.DeleteBackupId = args[++i];
                else if (arg == "--delete-all-backups") opts.IsDeleteAllBackups = true;
                else if (arg == "--import-winapp2" && i + 1 < args.Length) opts.ImportWinApp2Path = args[++i];
                else if (arg == "--update-winapp2") opts.IsUpdateWinapp2 = true;
                else if (arg == "--dry-run") opts.IsDryRun = true;
                else if (arg == "--rollback" && i + 1 < args.Length) opts.RollbackId = args[++i];
                else if (arg == "--rules" && i + 1 < args.Length) opts.RulesFile = args[++i];
                else if (arg == "--ipc-service") opts.IsIpcService = true;
                else if (arg == "--clean-registry") opts.IsCleanRegistry = true;
                else if (arg == "--clean-browser") opts.IsCleanBrowser = true;
                else if (arg == "--list-startup") opts.IsListStartup = true;
                else if (arg == "--monitor") opts.IsMonitor = true;
                else if (arg == "--trim-ram") opts.IsTrimRam = true;
                else if (arg == "--shred" && i + 1 < args.Length) opts.ShredPath = args[++i];
                else if (arg == "--shred-mode" && i + 1 < args.Length) opts.ShredModeStr = args[++i];
                else if (arg == "--debloat-list") opts.IsDebloatList = true;
                else if (arg == "--debloat") opts.IsDebloatApply = true;
                else if (arg == "--schedule-daily" && i + 1 < args.Length) opts.ScheduleDailyTime = args[++i];
                else if (arg == "--schedule-weekly" && i + 2 < args.Length)
                {
                    opts.ScheduleWeeklyDay = args[++i];
                    opts.ScheduleWeeklyTime = args[++i];
                }
                else if (arg == "--unschedule") opts.IsUnschedule = true;
                else if (arg == "--scan") { /* default mode, no special flag needed */ }
            }

            return opts;
        }
    }
}
