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
    public class RealtimeMonitorDaemon : IDisposable
    {
        private readonly List<FileSystemWatcher> _watchers = new();
        private readonly long _thresholdBytes;
        private readonly string[] _monitoredDirs;
        private readonly ConcurrentDictionary<string, long> _accumulatedFileSizes = new();
        private readonly SemaphoreSlim _checkSemaphore = new(1, 1);
        private readonly Timer _debounceTimer;
        private bool _disposed;

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

            // Persistent timer to avoid re-allocations and race conditions
            _debounceTimer = new Timer(async _ =>
            {
                await DebouncedCheckJunkThresholdAsync();
            }, null, Timeout.Infinite, Timeout.Infinite);
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
                    catch (Exception ex) { OptimaxLogger.Warn($"Failed to attach FileSystemWatcher to: {dir}", ex); }
                }
            }

            // Initial async check on startup
            _ = Task.Run(() => DebouncedCheckJunkThresholdAsync(), ct);

            // Register cancellation cleanup
            ct.Register(() =>
            {
                Dispose();
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
            catch (Exception ex) { OptimaxLogger.Trace($"File change event handler error: {e.FullPath}", ex); }

            ScheduleDebouncedCheck();
        }

        private void OnFileDeleted(object sender, FileSystemEventArgs e)
        {
            _accumulatedFileSizes.TryRemove(e.FullPath, out _);
            ScheduleDebouncedCheck();
        }

        private void ScheduleDebouncedCheck()
        {
            if (_disposed) return;
            // Thread-safe timer reset without new object allocation
            _debounceTimer.Change(500, Timeout.Infinite);
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
                var req = new IPCRequest("monitor-event", false, null, null, notification.Message, true, 2, null);
                string reqJson = JsonSerializer.Serialize(req, OptimaxJsonContext.Default.IPCRequest);
                await writer.WriteLineAsync(reqJson.AsMemory());
            }
            catch (Exception ex) { OptimaxLogger.Trace("IPC monitor notification send failed", ex); }
        }

        private static long FastGetDirectorySize(string path)
        {
            long total = 0;
            try
            {
                var dirInfo = new DirectoryInfo(path);
                foreach (var file in dirInfo.EnumerateFiles("*", SearchOption.AllDirectories))
                {
                    try { total += file.Length; } catch { }
                }
            }
            catch (Exception ex) { OptimaxLogger.Trace($"Directory size calculation failed for: {path}", ex); }

            return total;
        }

        public void Dispose()
        {
            if (_disposed) return;
            _disposed = true;

            _debounceTimer.Dispose();
            _checkSemaphore.Dispose();
            foreach (var w in _watchers)
            {
                w.EnableRaisingEvents = false;
                w.Dispose();
            }
            _watchers.Clear();
        }
    }
}
