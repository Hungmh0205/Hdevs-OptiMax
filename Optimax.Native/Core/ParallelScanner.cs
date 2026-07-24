using System;
using System.Collections.Concurrent;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Optimax.IPC;

namespace Optimax.Core
{
    public class ParallelScanner
    {
        public async Task<ScanReport> ExecuteScanAsync(string[] targetDirectories, bool isDryRun, CancellationToken ct = default)
        {
            var results = new ConcurrentBag<ScanItemResult>();
            long totalBytes = 0;

            var options = new ParallelOptions
            {
                MaxDegreeOfParallelism = Environment.ProcessorCount,
                CancellationToken = ct
            };

            await Parallel.ForEachAsync(targetDirectories, options, async (dir, token) =>
            {
                await Task.Yield();
                string expanded = Environment.ExpandEnvironmentVariables(dir);
                if (!Directory.Exists(expanded)) return;

                if (!SafetyEngine.IsDriveReadyAndLocal(expanded)) return;

                var dirInfo = new DirectoryInfo(expanded);
                FileInfo[] files;
                try
                {
                    files = dirInfo.GetFiles("*", SearchOption.AllDirectories);
                }
                catch
                {
                    return;
                }

                foreach (var file in files)
                {
                    token.ThrowIfCancellationRequested();
                    long size = 0;
                    try { size = file.Length; } catch { }
                    Interlocked.Add(ref totalBytes, size);

                    var (isLocked, lockingApps) = SafetyEngine.GetFileLockStatus(file.FullName);

                    string action = "DeleteImmediate";
                    if (isLocked)
                    {
                        action = "ScheduleDeleteOnReboot";
                        if (!isDryRun)
                        {
                            SafetyEngine.ScheduleDeleteOnReboot(file.FullName);
                        }
                    }
                    else if (!isDryRun)
                    {
                        try
                        {
                            file.Delete();
                        }
                        catch
                        {
                            SafetyEngine.ScheduleDeleteOnReboot(file.FullName);
                            action = "ScheduleDeleteOnReboot";
                        }
                    }

                    results.Add(new ScanItemResult(file.FullName, size, isLocked, lockingApps, action));
                }
            });

            var itemsArray = results.ToArray();
            string riskLevel = itemsArray.Any(i => i.IsLocked) ? "Medium" : "Low";

            return new ScanReport(isDryRun, itemsArray.Length, totalBytes, riskLevel, itemsArray);
        }
    }
}
