using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Optimax.IPC;

namespace Optimax.Core
{
    public class ParallelScanner
    {
        public async Task<ScanReport> ExecuteScanAsync(string[] targetDirectories, bool isDryRun, IEnumerable<string>? customMatchedFiles = null, CancellationToken ct = default)
        {
            var results = new ConcurrentBag<ScanItemResult>();
            var processedFiles = new ConcurrentDictionary<string, byte>(StringComparer.OrdinalIgnoreCase);
            long totalBytes = 0;

            // 1. Process explicit custom matched files (e.g. from WinApp2.ini / custom rules)
            if (customMatchedFiles != null)
            {
                foreach (var filePath in customMatchedFiles)
                {
                    if (ct.IsCancellationRequested) break;
                    if (File.Exists(filePath) && processedFiles.TryAdd(filePath, 0))
                    {
                        long size = 0;
                        try { size = new FileInfo(filePath).Length; } catch { }
                        ProcessSingleFileNative(filePath, size, isDryRun, ref totalBytes, results);
                    }
                }
            }

            // 2. High-performance Win32 Native Work-Stealing Multi-Core Directory Scan
            await FastNativeScanner.ScanDirectoriesParallelAsync(targetDirectories, fileInfo =>
            {
                if (processedFiles.TryAdd(fileInfo.FullPath, 0))
                {
                    ProcessSingleFileNative(fileInfo.FullPath, fileInfo.SizeBytes, isDryRun, ref totalBytes, results);
                }
            }, ct);

            var itemsArray = results.ToArray();
            string riskLevel = itemsArray.Any(i => i.IsLocked) ? "Medium" : "Low";

            return new ScanReport(isDryRun, itemsArray.Length, totalBytes, riskLevel, itemsArray);
        }

        private static void ProcessSingleFileNative(string filePath, long size, bool isDryRun, ref long totalBytes, ConcurrentBag<ScanItemResult> results)
        {
            bool isLocked = false;
            string[] lockingApps = Array.Empty<string>();
            string action = isDryRun ? "Would Delete" : "DeleteImmediate";

            try
            {
                var attr = File.GetAttributes(filePath);
                if ((attr & FileAttributes.ReparsePoint) != 0)
                {
                    results.Add(new ScanItemResult(filePath, size, false, Array.Empty<string>(), "Skipped (ReparsePoint/Symlink)"));
                    return;
                }
            }
            catch
            {
                // Attribute read error
            }

            if (isDryRun)
            {
                // Lightweight check during dry-run scan without opening unnecessary exclusive write handles
                try
                {
                    var attr = File.GetAttributes(filePath);
                    if ((attr & FileAttributes.ReadOnly) != 0)
                    {
                        // File is read-only
                    }
                }
                catch (IOException)
                {
                    isLocked = true;
                }
                catch (UnauthorizedAccessException)
                {
                    isLocked = true;
                }

                if (isLocked)
                {
                    var lockRes = SafetyEngine.GetFileLockStatus(filePath);
                    lockingApps = lockRes.LockingApps;
                    action = "Skipped (File Locked)";
                }
                else
                {
                    Interlocked.Add(ref totalBytes, size);
                }
            }
            else
            {
                // Execution mode: Direct removal attempt to eliminate redundant pre-open handles
                try
                {
                    var fileAttr = File.GetAttributes(filePath);
                    if ((fileAttr & FileAttributes.ReadOnly) != 0)
                    {
                        File.SetAttributes(filePath, fileAttr & ~FileAttributes.ReadOnly);
                    }
                    File.Delete(filePath);
                    Interlocked.Add(ref totalBytes, size);
                }
                catch (IOException)
                {
                    isLocked = true;
                    var lockRes = SafetyEngine.GetFileLockStatus(filePath);
                    lockingApps = lockRes.LockingApps;
                    action = "Skipped (File Locked)";
                }
                catch (UnauthorizedAccessException)
                {
                    isLocked = true;
                    action = "Skipped (Access Denied)";
                }
                catch (Exception ex)
                {
                    OptimaxLogger.Trace($"File delete failed: {filePath}", ex);
                    action = "Skipped (Error)";
                }
            }

            results.Add(new ScanItemResult(filePath, size, isLocked, lockingApps, action));
        }
    }
}
