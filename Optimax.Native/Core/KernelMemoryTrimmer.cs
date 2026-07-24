using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Runtime.InteropServices;

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

    public static class KernelMemoryTrimmer
    {
        [DllImport("user32.dll")]
        private static extern IntPtr GetForegroundWindow();

        [DllImport("user32.dll")]
        private static extern uint GetWindowThreadProcessId(IntPtr hWnd, out uint lpdwProcessId);

        [DllImport("psapi.dll", SetLastError = true)]
        private static extern bool EmptyWorkingSet(IntPtr hProcess);

        [DllImport("ntdll.dll", SetLastError = true)]
        private static extern int NtSetSystemInformation(int systemInformationClass, ref int systemInformation, int systemInformationLength);

        [DllImport("advapi32.dll", SetLastError = true, CharSet = CharSet.Unicode)]
        private static extern bool OpenProcessToken(IntPtr processHandle, uint desiredAccess, out IntPtr tokenHandle);

        [DllImport("advapi32.dll", SetLastError = true, CharSet = CharSet.Unicode)]
        private static extern bool LookupPrivilegeValue(string? lpSystemName, string lpName, out LUID lpLuid);

        [DllImport("advapi32.dll", SetLastError = true)]
        private static extern bool AdjustTokenPrivileges(
            IntPtr tokenHandle,
            bool disableAllPrivileges,
            ref TOKEN_PRIVILEGES newState,
            uint bufferLength,
            IntPtr previousState,
            IntPtr returnLength);

        [DllImport("kernel32.dll", SetLastError = true)]
        private static extern bool CloseHandle(IntPtr hObject);

        private const uint TOKEN_ADJUST_PRIVILEGES = 0x0020;
        private const uint TOKEN_QUERY = 0x0008;
        private const string SE_INCREASE_QUOTA_NAME = "SeIncreaseQuotaPrivilege";
        private const string SE_PROFILE_SINGLE_PROCESS_NAME = "SeProfileSingleProcessPrivilege";

        private const int SYSTEM_MEMORY_LIST_INFORMATION = 80;
        private const int MEMORY_PURGE_STANDBY_LIST = 2;

        private static DateTime _lastStandbyPurgeTime = DateTime.MinValue;
        private static readonly TimeSpan StandbyPurgeCooldown = TimeSpan.FromMinutes(15);
        private const long CRITICAL_AVAILABLE_RAM_THRESHOLD_BYTES = 2L * 1024 * 1024 * 1024; // 2 GB

        private static readonly HashSet<string> SystemExcludedProcesses = new(StringComparer.OrdinalIgnoreCase)
        {
            "system", "idle", "dwm", "explorer", "csrss", "lsass", "services", "smss", "winlogon", "svchost", "spoolsv"
        };

        private static uint GetForegroundProcessId()
        {
            IntPtr hwnd = GetForegroundWindow();
            if (hwnd != IntPtr.Zero)
            {
                GetWindowThreadProcessId(hwnd, out uint pid);
                return pid;
            }
            return 0;
        }

        [StructLayout(LayoutKind.Sequential)]
        private struct LUID
        {
            public uint LowPart;
            public int HighPart;
        }

        [StructLayout(LayoutKind.Sequential)]
        private struct LUID_AND_ATTRIBUTES
        {
            public LUID Luid;
            public uint Attributes;
        }

        [StructLayout(LayoutKind.Sequential)]
        private struct TOKEN_PRIVILEGES
        {
            public uint PrivilegeCount;
            public LUID_AND_ATTRIBUTES Privilege;
        }

        private static bool EnablePrivilege(string privilegeName)
        {
            if (!OpenProcessToken(Process.GetCurrentProcess().Handle, TOKEN_ADJUST_PRIVILEGES | TOKEN_QUERY, out IntPtr hToken))
                return false;

            try
            {
                if (!LookupPrivilegeValue(null, privilegeName, out LUID luid))
                    return false;

                TOKEN_PRIVILEGES tp = new TOKEN_PRIVILEGES
                {
                    PrivilegeCount = 1,
                    Privilege = new LUID_AND_ATTRIBUTES
                    {
                        Luid = luid,
                        Attributes = 0x00000002 // SE_PRIVILEGE_ENABLED
                    }
                };

                return AdjustTokenPrivileges(hToken, false, ref tp, 0, IntPtr.Zero, IntPtr.Zero);
            }
            finally
            {
                CloseHandle(hToken);
            }
        }

        public static MemoryTrimReport TrimSystemMemory(bool forceDeepPurge = false)
        {
            long initialPhysicalAvailable = GetAvailablePhysicalMemoryBytes();

            EnablePrivilege(SE_INCREASE_QUOTA_NAME);
            EnablePrivilege(SE_PROFILE_SINGLE_PROCESS_NAME);

            // 1. Adaptive Standby List Purging
            // Purge System Standby List ONLY if available physical RAM is below critical threshold (<2GB) OR forceDeepPurge is true AND cooldown has elapsed.
            bool standbyFlushed = false;
            bool isUnderMemoryPressure = initialPhysicalAvailable < CRITICAL_AVAILABLE_RAM_THRESHOLD_BYTES;
            bool shouldPurgeStandby = (isUnderMemoryPressure || forceDeepPurge)
                                      && (DateTime.UtcNow - _lastStandbyPurgeTime > StandbyPurgeCooldown);

            if (shouldPurgeStandby)
            {
                try
                {
                    int command = MEMORY_PURGE_STANDBY_LIST;
                    int result = NtSetSystemInformation(SYSTEM_MEMORY_LIST_INFORMATION, ref command, sizeof(int));
                    if (result == 0)
                    {
                        standbyFlushed = true;
                        _lastStandbyPurgeTime = DateTime.UtcNow;
                    }
                }
                catch { }
            }

            // 2. Selective Working Set Trimming
            // Only trim Working Sets of idle background non-system processes consuming >150MB RAM if system is under memory pressure or deep purge requested.
            uint foregroundPid = GetForegroundProcessId();
            int currentPid = Environment.ProcessId;

            int trimmedCount = 0;
            if (isUnderMemoryPressure || forceDeepPurge)
            {
                Process[] processes = Process.GetProcesses();
                foreach (var proc in processes)
                {
                    try
                    {
                        if (proc.HasExited) continue;
                        int pId = proc.Id;

                        // Skip current process, foreground app, and system core processes
                        if (pId == currentPid || (uint)pId == foregroundPid) continue;
                        if (SystemExcludedProcesses.Contains(proc.ProcessName)) continue;

                        // Skip small processes (<150MB) to prevent unnecessary I/O thrashing
                        if (proc.WorkingSet64 < 150L * 1024 * 1024) continue;

                        if (EmptyWorkingSet(proc.Handle))
                        {
                            trimmedCount++;
                        }
                    }
                    catch
                    {
                        // Ignore processes where access is denied
                    }
                    finally
                    {
                        proc.Dispose();
                    }
                }
            }

            long finalPhysicalAvailable = GetAvailablePhysicalMemoryBytes();
            long bytesFreed = Math.Max(0, finalPhysicalAvailable - initialPhysicalAvailable);

            string msg = (isUnderMemoryPressure || forceDeepPurge)
                ? $"Optimized {trimmedCount} high-RAM background processes." + (standbyFlushed ? " System Standby List purged due to memory pressure." : "")
                : "System memory is optimal (Standby List & Working Sets preserved to prevent I/O thrashing).";

            return new MemoryTrimReport(bytesFreed, trimmedCount, standbyFlushed, msg);
        }

        private static long GetAvailablePhysicalMemoryBytes()
        {
            try
            {
                var gcInfo = GC.GetGCMemoryInfo();
                return gcInfo.TotalAvailableMemoryBytes;
            }
            catch
            {
                return 0;
            }
        }
    }
}
