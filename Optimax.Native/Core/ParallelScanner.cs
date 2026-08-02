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

            var options = new ParallelOptions
            {
                MaxDegreeOfParallelism = Environment.ProcessorCount,
                CancellationToken = ct
            };

            if (customMatchedFiles != null)
            {
                foreach (var filePath in customMatchedFiles)
                {
                    if (File.Exists(filePath) && processedFiles.TryAdd(filePath, 0))
                    {
                        ProcessSingleFile(new FileInfo(filePath), isDryRun, ref totalBytes, results);
                    }
                }
            }

            await Parallel.ForEachAsync(targetDirectories, options, async (dir, token) =>
            {
                await Task.Yield();
                string expanded = Environment.ExpandEnvironmentVariables(dir);
                if (!Directory.Exists(expanded)) return;

                if (!SafetyEngine.IsDriveReadyAndLocal(expanded)) return;

                var files = EnumerateFilesSafe(expanded);

                foreach (var file in files)
                {
                    token.ThrowIfCancellationRequested();
                    if (processedFiles.TryAdd(file.FullName, 0))
                    {
                        ProcessSingleFile(file, isDryRun, ref totalBytes, results);
                    }
                }
            });

            var itemsArray = results.ToArray();
            string riskLevel = itemsArray.Any(i => i.IsLocked) ? "Medium" : "Low";

            return new ScanReport(isDryRun, itemsArray.Length, totalBytes, riskLevel, itemsArray);
        }

        private static void ProcessSingleFile(FileInfo file, bool isDryRun, ref long totalBytes, ConcurrentBag<ScanItemResult> results)
        {
            long size = 0;
            try { size = file.Length; } catch (Exception ex) { OptimaxLogger.Trace($"Cannot read file size: {file.FullName}", ex); }

            bool isLocked = false;
            string[] lockingApps = Array.Empty<string>();

            // Test if file can be opened exclusively
            try
            {
                using var fs = file.Open(FileMode.Open, FileAccess.ReadWrite, FileShare.None);
            }
            catch (IOException)
            {
                isLocked = true;
            }
            catch (UnauthorizedAccessException)
            {
                isLocked = true;
            }
            catch (Exception ex) { OptimaxLogger.Trace($"File access check inconclusive: {file.FullName}", ex); }

            if (isLocked)
            {
                var lockRes = SafetyEngine.GetFileLockStatus(file.FullName);
                lockingApps = lockRes.LockingApps;
            }

            string action = isDryRun ? "Would Delete" : "DeleteImmediate";
            if (isLocked)
            {
                action = "Skipped (File Locked)";
            }
            else if (!isDryRun)
            {
                try
                {
                    if (file.IsReadOnly) file.IsReadOnly = false;
                    file.Delete();
                    Interlocked.Add(ref totalBytes, size);
                }
                catch
                {
                    action = "Skipped (Access Denied)";
                }
            }
            else
            {
                Interlocked.Add(ref totalBytes, size);
            }

            results.Add(new ScanItemResult(file.FullName, size, isLocked, lockingApps, action));
        }


        private static IEnumerable<FileInfo> EnumerateFilesSafe(string rootPath)
        {
            var dirsToProcess = new Queue<string>();
            dirsToProcess.Enqueue(rootPath);

            while (dirsToProcess.Count > 0)
            {
                string currentDir = dirsToProcess.Dequeue();

                IEnumerable<string> files;
                try
                {
                    files = Directory.EnumerateFiles(currentDir);
                }
                catch (Exception ex)
                {
                    OptimaxLogger.Trace($"Cannot enumerate files in: {currentDir}", ex);
                    continue;
                }

                foreach (var filePath in files)
                {
                    FileInfo fi;
                    try { fi = new FileInfo(filePath); }
                    catch (Exception ex)
                    {
                        OptimaxLogger.Trace($"Cannot access file: {filePath}", ex);
                        continue;
                    }
                    yield return fi;
                }

                try
                {
                    foreach (var subDir in Directory.EnumerateDirectories(currentDir))
                    {
                        dirsToProcess.Enqueue(subDir);
                    }
                }
                catch (Exception ex)
                {
                    // Expected for Access Denied on system directories
                    OptimaxLogger.Trace($"Cannot enumerate subdirectories: {currentDir}", ex);
                }
            }
        }
    }
}

