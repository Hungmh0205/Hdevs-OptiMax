using System;
using System.Diagnostics;
using System.Threading;

namespace Optimax.Core
{
    public struct MemoryTrimReport
    {
        public long BytesFreed { get; set; }
        public int ProcessesTrimmed { get; set; }
        public bool StandbyListFlushed { get; set; }
        public string StatusMessage { get; set; }

        public MemoryTrimReport(long bytesFreed, int processesTrimmed, bool standbyListFlushed, string statusMessage)
        {
            BytesFreed = bytesFreed;
            ProcessesTrimmed = processesTrimmed;
            StandbyListFlushed = standbyListFlushed;
            StatusMessage = statusMessage;
        }
    }

    public class KernelMemoryTrimmerEngine : IKernelMemoryTrimmer
    {
        private readonly IWin32SystemApi _win32Api;
        private static long _lastStandbyPurgeTicks = DateTime.MinValue.Ticks;
        private static readonly TimeSpan StandbyPurgeCooldown = TimeSpan.FromMinutes(30);

        public KernelMemoryTrimmerEngine(IWin32SystemApi? win32Api = null)
        {
            _win32Api = win32Api ?? new Win32SystemApiWrapper();
        }

        public MemoryTrimReport TrimSystemMemory(bool forceDeepPurge = false)
        {
            long initialAvailableBytes = GetAvailablePhysicalMemoryBytes();
            long totalBytes = GetTotalPhysicalMemoryBytes();

            double availablePct = totalBytes > 0 ? (double)initialAvailableBytes / totalBytes : 1.0;

            // SAFE GATE 1: Anti-Thrashing Guard - Skip working set purge if physical memory > 15% available
            if (availablePct > 0.15 && !forceDeepPurge)
            {
                return new MemoryTrimReport(0, 0, false,
                    $"Dung lượng RAM khả dụng an toàn ({availablePct * 100:F1}%). Bỏ qua thao tác xả RAM để bảo vệ hiệu năng đĩa (Anti-I/O Thrashing Guard).");
            }

            long nowTicks = DateTime.UtcNow.Ticks;
            long lastPurgeTicks = Volatile.Read(ref _lastStandbyPurgeTicks);
            bool canPurge = (nowTicks - lastPurgeTicks) > StandbyPurgeCooldown.Ticks;

            int trimmedCount = 0;
            bool standbyFlushed = false;

            if ((canPurge || forceDeepPurge) && Interlocked.CompareExchange(ref _lastStandbyPurgeTicks, nowTicks, lastPurgeTicks) == lastPurgeTicks)
            {
                // Step 1: Trim Optimax's own working set to reduce self footprint
                try
                {
                    using var currentProc = Process.GetCurrentProcess();
                    if (_win32Api.EmptyWorkingSet(currentProc.Handle))
                    {
                        trimmedCount++;
                    }
                }
                catch (Exception ex)
                {
                    OptimaxLogger.Trace("Failed to trim Optimax working set", ex);
                }

                // Step 2: Purge System Standby List via NtSetSystemInformation
                // This is the REAL standby list flush — requires SeProfileSingleProcessPrivilege
                try
                {
                    standbyFlushed = _win32Api.PurgeStandbyList();
                    if (standbyFlushed)
                    {
                        OptimaxLogger.Trace("System Standby List purged successfully.");
                    }
                    else
                    {
                        OptimaxLogger.Warn("System Standby List purge returned non-success. Process may lack SeProfileSingleProcessPrivilege or is not running as Administrator.");
                    }
                }
                catch (Exception ex)
                {
                    OptimaxLogger.Warn("System Standby List purge failed", ex);
                    standbyFlushed = false;
                }
            }

            long finalAvailableBytes = GetAvailablePhysicalMemoryBytes();
            long bytesFreed = Math.Max(0, finalAvailableBytes - initialAvailableBytes);

            string statusMsg = standbyFlushed
                ? $"Đã xả System Standby List và thu hồi bộ nhớ thành công. RAM khả dụng: {finalAvailableBytes / (1024 * 1024)} MB (giải phóng {bytesFreed / (1024 * 1024)} MB)."
                : $"Đã hoàn tất kiểm tra và thu hồi Working Set. RAM khả dụng: {finalAvailableBytes / (1024 * 1024)} MB.";

            return new MemoryTrimReport(
                bytesFreed,
                trimmedCount,
                standbyFlushed,
                statusMsg
            );
        }

        private long GetAvailablePhysicalMemoryBytes()
        {
            if (_win32Api.GetSystemMemoryStatus(out var memStatus))
            {
                return (long)memStatus.ullAvailPhys;
            }
            return 0;
        }

        private long GetTotalPhysicalMemoryBytes()
        {
            if (_win32Api.GetSystemMemoryStatus(out var memStatus))
            {
                return (long)memStatus.ullTotalPhys;
            }
            return 1;
        }
    }

    public static class KernelMemoryTrimmer
    {
        private static readonly IKernelMemoryTrimmer _engine = new KernelMemoryTrimmerEngine();

        public static MemoryTrimReport TrimSystemMemory(bool forceDeepPurge = false)
        {
            return _engine.TrimSystemMemory(forceDeepPurge);
        }
    }
}
