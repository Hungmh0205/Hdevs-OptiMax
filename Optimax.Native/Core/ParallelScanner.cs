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

                List<FileInfo> files = EnumerateFilesSafe(expanded);

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
            try { size = file.Length; } catch { }

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
            catch { }

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


        private static List<FileInfo> EnumerateFilesSafe(string rootPath)
        {
            var list = new List<FileInfo>();
            var dirsToProcess = new Queue<string>();
            dirsToProcess.Enqueue(rootPath);

            while (dirsToProcess.Count > 0)
            {
                string currentDir = dirsToProcess.Dequeue();
                try
                {
                    var dirInfo = new DirectoryInfo(currentDir);
                    FileInfo[] files = dirInfo.GetFiles();
                    list.AddRange(files);

                    DirectoryInfo[] subDirs = dirInfo.GetDirectories();
                    foreach (var sd in subDirs)
                    {
                        dirsToProcess.Enqueue(sd.FullName);
                    }
                }
                catch
                {
                    // Ignore subdirectories with Access Denied or not found
                }
            }

            return list;
        }
    }
}

