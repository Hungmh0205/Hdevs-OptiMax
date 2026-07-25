using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.IO;
using System.IO.Pipes;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Optimax.IPC;

namespace Optimax.Core
{
    public class RealtimeMonitorDaemon
    {
        private readonly List<FileSystemWatcher> _watchers = new();
        private readonly long _thresholdBytes;
        private readonly string[] _monitoredDirs;
        private readonly ConcurrentDictionary<string, long> _accumulatedFileSizes = new();
        private readonly SemaphoreSlim _checkSemaphore = new(1, 1);
        private Timer? _debounceTimer;

        public RealtimeMonitorDaemon(long thresholdBytes = 2L * 1024 * 1024 * 1024) // Default 2 GB
        {
            _thresholdBytes = thresholdBytes;

            string winDir = Environment.GetFolderPath(Environment.SpecialFolder.Windows);
            if (string.IsNullOrEmpty(winDir)) winDir = "C:\\Windows";

            _monitoredDirs = new[]
            {
                Environment.ExpandEnvironmentVariables("%TEMP%"),
                Path.Combine(winDir, "Temp"),
                Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Microsoft\\Windows\\INetCache"),
                Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "D3DSCache")
            };
        }

        public Task StartMonitoringAsync(CancellationToken ct)
        {
            Console.WriteLine($"[REALTIME EVENT MONITOR] Initializing FileSystemWatcher Event Engine (Threshold: {_thresholdBytes / (1024 * 1024)} MB)...");

            foreach (var dir in _monitoredDirs)
            {
                if (Directory.Exists(dir))
                {
                    try
                    {
                        var watcher = new FileSystemWatcher(dir)
                        {
                            IncludeSubdirectories = true,
                            NotifyFilter = NotifyFilters.FileName | NotifyFilters.Size | NotifyFilters.LastWrite
                        };

                        watcher.Created += OnFileChanged;
                        watcher.Changed += OnFileChanged;
                        watcher.Deleted += OnFileDeleted;
                        watcher.EnableRaisingEvents = true;

                        _watchers.Add(watcher);
                        Console.WriteLine($"[REALTIME EVENT MONITOR] Attached Event Listener to: {dir}");
                    }
                    catch { }
                }
            }

            // Initial async check on startup
            _ = Task.Run(() => DebouncedCheckJunkThresholdAsync(), ct);

            // Register cancellation cleanup
            ct.Register(() =>
            {
                _debounceTimer?.Dispose();
                foreach (var w in _watchers)
                {
                    w.EnableRaisingEvents = false;
                    w.Dispose();
                }
                _watchers.Clear();
            });

            return Task.CompletedTask;
        }

        private void OnFileChanged(object sender, FileSystemEventArgs e)
        {
            try
            {
                if (File.Exists(e.FullPath))
                {
                    var info = new FileInfo(e.FullPath);
                    _accumulatedFileSizes[e.FullPath] = info.Length;
                }
            }
            catch { }

            ScheduleDebouncedCheck();
        }

        private void OnFileDeleted(object sender, FileSystemEventArgs e)
        {
            _accumulatedFileSizes.TryRemove(e.FullPath, out _);
            ScheduleDebouncedCheck();
        }

        private void ScheduleDebouncedCheck()
        {
            // Debounce event processing: Wait 500ms after last event before checking threshold
            _debounceTimer?.Dispose();
            _debounceTimer = new Timer(async _ =>
            {
                await DebouncedCheckJunkThresholdAsync();
            }, null, 500, Timeout.Infinite);
        }

        private async Task DebouncedCheckJunkThresholdAsync()
        {
            if (!await _checkSemaphore.WaitAsync(0)) return;

            try
            {
                long totalBytes = 0;
                foreach (var dir in _monitoredDirs)
                {
                    if (Directory.Exists(dir))
                    {
                        totalBytes += FastGetDirectorySize(dir);
                    }
                }

                if (totalBytes >= _thresholdBytes)
                {
                    Console.WriteLine($"[REALTIME MONITOR EVENT ALERT] Threshold exceeded! Current Junk: {totalBytes / (1024 * 1024)} MB");

                    var notification = new MonitorNotification(
                        "JUNK_THRESHOLD_EXCEEDED",
                        totalBytes,
                        _thresholdBytes,
                        $"Junk accumulation reached {totalBytes / (1024 * 1024)} MB. Auto-clean recommended.",
                        DateTime.UtcNow
                    );

                    await SendIPCNotificationAsync(notification);
                }
            }
            finally
            {
                _checkSemaphore.Release();
            }
        }

        private static async Task SendIPCNotificationAsync(MonitorNotification notification)
        {
            try
            {
                using var pipeClient = new NamedPipeClientStream(".", "OptimaxIPC", PipeDirection.InOut);
                await pipeClient.ConnectAsync(1000);

                using var writer = new StreamWriter(pipeClient) { AutoFlush = true };
                var req = new IPCRequest("monitor-event", false, null, null, null, true, 2, null);
                string reqJson = JsonSerializer.Serialize(req, OptimaxJsonContext.Default.IPCRequest);
                await writer.WriteLineAsync(reqJson.AsMemory());
            }
            catch { }
        }


        private static long FastGetDirectorySize(string path)
        {
            long total = 0;
            var queue = new Queue<string>();
            queue.Enqueue(path);

            while (queue.Count > 0)
            {
                string current = queue.Dequeue();
                try
                {
                    var dirInfo = new DirectoryInfo(current);
                    FileInfo[] files = dirInfo.GetFiles();
                    foreach (var file in files)
                    {
                        try { total += file.Length; } catch { }
                    }

                    DirectoryInfo[] subDirs = dirInfo.GetDirectories();
                    foreach (var sd in subDirs)
                    {
                        queue.Enqueue(sd.FullName);
                    }
                }
                catch { }
            }

            return total;
        }
    }
}
