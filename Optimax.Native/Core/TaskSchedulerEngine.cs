using System;
using System.Diagnostics;
using System.IO;

namespace Optimax.Core
{
    public static class TaskSchedulerEngine
    {
        private const string TASK_NAME = "OptimaxAutoClean";

        public static bool ScheduleDaily(TimeSpan time, bool isDryRun = true)
        {
            try
            {
                string exePath = Process.GetCurrentProcess().MainModule?.FileName ?? Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "Optimax.exe");
                string args = isDryRun ? "--dry-run" : "--clean-registry";
                string targetCmd = $"\"{exePath}\" {args}";
                string timeStr = $"{time.Hours:D2}:{time.Minutes:D2}";

                // schtasks /create /tn "OptimaxAutoClean" /tr "\"C:\path\Optimax.exe\" --scan" /sc daily /st 03:00 /ru SYSTEM /rl HIGHEST /f
                string schCmd = $"/create /tn \"{TASK_NAME}\" /tr \"{targetCmd}\" /sc daily /st {timeStr} /ru SYSTEM /rl HIGHEST /f";

                var psi = new ProcessStartInfo
                {
                    FileName = "schtasks.exe",
                    Arguments = schCmd,
                    CreateNoWindow = true,
                    UseShellExecute = false,
                    RedirectStandardOutput = true,
                    RedirectStandardError = true
                };

                using var p = Process.Start(psi);
                if (p == null) return false;
                p.WaitForExit(4000);
                return p.ExitCode == 0;
            }
            catch (Exception ex)
            {
                Console.Error.WriteLine($"[TASK SCHEDULER ERROR] Failed to schedule daily task: {ex.Message}");
                return false;
            }
        }

        public static bool ScheduleWeekly(DayOfWeek dayOfWeek, TimeSpan time, bool isDryRun = true)
        {
            try
            {
                string exePath = Process.GetCurrentProcess().MainModule?.FileName ?? Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "Optimax.exe");
                string args = isDryRun ? "--dry-run" : "--clean-registry";
                string targetCmd = $"\"{exePath}\" {args}";
                string timeStr = $"{time.Hours:D2}:{time.Minutes:D2}";

                string dayStr = dayOfWeek switch
                {
                    DayOfWeek.Sunday => "SUN",
                    DayOfWeek.Monday => "MON",
                    DayOfWeek.Tuesday => "TUE",
                    DayOfWeek.Wednesday => "WED",
                    DayOfWeek.Thursday => "THU",
                    DayOfWeek.Friday => "FRI",
                    DayOfWeek.Saturday => "SAT",
                    _ => "SUN"
                };

                string schCmd = $"/create /tn \"{TASK_NAME}\" /tr \"{targetCmd}\" /sc weekly /d {dayStr} /st {timeStr} /ru SYSTEM /rl HIGHEST /f";

                var psi = new ProcessStartInfo
                {
                    FileName = "schtasks.exe",
                    Arguments = schCmd,
                    CreateNoWindow = true,
                    UseShellExecute = false,
                    RedirectStandardOutput = true,
                    RedirectStandardError = true
                };

                using var p = Process.Start(psi);
                if (p == null) return false;
                p.WaitForExit(4000);
                return p.ExitCode == 0;
            }
            catch (Exception ex)
            {
                Console.Error.WriteLine($"[TASK SCHEDULER ERROR] Failed to schedule weekly task: {ex.Message}");
                return false;
            }
        }

        public static bool Unschedule()
        {
            try
            {
                string schCmd = $"/delete /tn \"{TASK_NAME}\" /f";

                var psi = new ProcessStartInfo
                {
                    FileName = "schtasks.exe",
                    Arguments = schCmd,
                    CreateNoWindow = true,
                    UseShellExecute = false,
                    RedirectStandardOutput = true,
                    RedirectStandardError = true
                };

                using var p = Process.Start(psi);
                if (p == null) return false;
                p.WaitForExit(4000);
                return p.ExitCode == 0;
            }
            catch (Exception ex)
            {
                Console.Error.WriteLine($"[TASK SCHEDULER ERROR] Failed to unschedule task: {ex.Message}");
                return false;
            }
        }
    }
}
