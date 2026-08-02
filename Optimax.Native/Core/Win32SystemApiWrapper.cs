using System;
using System.Runtime.InteropServices;

namespace Optimax.Core
{
    public class Win32SystemApiWrapper : IWin32SystemApi
    {
        [DllImport("psapi.dll", EntryPoint = "EmptyWorkingSet", SetLastError = true)]
        private static extern bool Win32EmptyWorkingSet(IntPtr hProcess);

        private static bool _privilegeEnabled = false;
        private static readonly object _privilegeLock = new object();

        public bool EmptyWorkingSet(IntPtr hProcess)
        {
            try
            {
                return Win32EmptyWorkingSet(hProcess);
            }
            catch (Exception ex)
            {
                OptimaxLogger.Trace("Win32 EmptyWorkingSet call failed", ex);
                return false;
            }
        }

        public bool GetSystemMemoryStatus(out MEMORYSTATUSEX memStatus)
        {
            memStatus = default;
            memStatus.Init();
            try
            {
                return ScmServiceManager.GlobalMemoryStatusEx(ref memStatus);
            }
            catch (Exception ex)
            {
                OptimaxLogger.Trace("Win32 GlobalMemoryStatusEx call failed", ex);
                return false;
            }
        }

        /// <summary>
        /// Purge the System Standby List via NtSetSystemInformation(SystemMemoryListInformation, MemoryPurgeStandbyList).
        /// Automatically enables SeProfileSingleProcessPrivilege on first call (required by NtSetSystemInformation).
        /// </summary>
        public bool PurgeStandbyList()
        {
            try
            {
                // Enable SeProfileSingleProcessPrivilege once — required for NtSetSystemInformation
                if (!_privilegeEnabled)
                {
                    lock (_privilegeLock)
                    {
                        if (!_privilegeEnabled)
                        {
                            bool ok = KernelMemoryInterop.EnablePrivilege("SeProfileSingleProcessPrivilege");
                            if (!ok)
                            {
                                OptimaxLogger.Warn("Failed to enable SeProfileSingleProcessPrivilege. Standby list purge may fail without admin rights.");
                            }
                            _privilegeEnabled = true; // Don't retry on every call even if it fails
                        }
                    }
                }

                int ntstatus = KernelMemoryInterop.PurgeStandbyList();
                if (ntstatus == 0)
                {
                    OptimaxLogger.Trace("System Standby List purged successfully via NtSetSystemInformation.");
                    return true;
                }
                else
                {
                    OptimaxLogger.Warn($"NtSetSystemInformation(MemoryPurgeStandbyList) returned NTSTATUS: 0x{ntstatus:X8}");
                    return false;
                }
            }
            catch (Exception ex)
            {
                OptimaxLogger.Warn("PurgeStandbyList failed", ex);
                return false;
            }
        }
    }
}
