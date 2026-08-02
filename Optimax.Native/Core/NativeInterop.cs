using System;
using System.ComponentModel;
using System.Runtime.InteropServices;
using System.ServiceProcess;

namespace Optimax.Core
{
    /// <summary>
    /// Centralized Win32 Native Interop definitions — eliminates P/Invoke duplication across modules.
    /// Contains shared MEMORYSTATUSEX, GlobalMemoryStatusEx, and Service Control Manager (SCM) APIs.
    /// </summary>

    // ═══════════════════════════════════════════════════════════
    //  Shared Memory Status (used by KernelMemoryTrimmer + Program.cs)
    // ═══════════════════════════════════════════════════════════

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Auto)]
    public struct MEMORYSTATUSEX
    {
        public uint dwLength;
        public uint dwMemoryLoad;
        public ulong ullTotalPhys;
        public ulong ullAvailPhys;
        public ulong ullTotalPageFile;
        public ulong ullAvailPageFile;
        public ulong ullTotalVirtual;
        public ulong ullAvailVirtual;
        public ulong ullAvailExtendedVirtual;

        public void Init()
        {
            dwLength = (uint)Marshal.SizeOf<MEMORYSTATUSEX>();
        }
    }

    // ═══════════════════════════════════════════════════════════
    //  Shared Service Control Manager (used by TransactionalRollback + StartupOptimizer)
    // ═══════════════════════════════════════════════════════════

    [StructLayout(LayoutKind.Sequential)]
    public struct SERVICE_STATUS
    {
        public uint dwServiceType;
        public uint dwCurrentState;
        public uint dwControlsAccepted;
        public uint dwWin32ExitCode;
        public uint dwServiceSpecificExitCode;
        public uint dwCheckPoint;
        public uint dwWaitHint;
    }

    public sealed class SafeServiceHandle : Microsoft.Win32.SafeHandles.SafeHandleZeroOrMinusOneIsInvalid
    {
        private SafeServiceHandle() : base(true) { }

        [DllImport("advapi32.dll", ExactSpelling = true, SetLastError = true)]
        [return: MarshalAs(UnmanagedType.Bool)]
        private static extern bool CloseServiceHandle(IntPtr hSCObject);

        protected override bool ReleaseHandle()
        {
            return CloseServiceHandle(handle);
        }
    }

    /// <summary>
    /// Unified Service Control Manager (SCM) P/Invoke wrapper.
    /// Consolidates Win32 SCM operations with SafeServiceHandle resource safety.
    /// </summary>
    public static class ScmServiceManager
    {
        [DllImport("advapi32.dll", EntryPoint = "OpenSCManagerW", ExactSpelling = true, CharSet = CharSet.Unicode, SetLastError = true)]
        private static extern SafeServiceHandle OpenSCManager(string? machineName, string? databaseName, uint dwDesiredAccess);

        [DllImport("advapi32.dll", EntryPoint = "OpenServiceW", ExactSpelling = true, CharSet = CharSet.Unicode, SetLastError = true)]
        private static extern SafeServiceHandle OpenService(SafeServiceHandle hSCManager, string serviceName, uint dwDesiredAccess);

        [DllImport("advapi32.dll", EntryPoint = "ChangeServiceConfigW", ExactSpelling = true, CharSet = CharSet.Unicode, SetLastError = true)]
        [return: MarshalAs(UnmanagedType.Bool)]
        private static extern bool ChangeServiceConfig(
            SafeServiceHandle hService,
            uint dwServiceType,
            uint dwStartType,
            uint dwErrorControl,
            string? binaryPathName,
            string? loadOrderGroup,
            IntPtr tagId,
            string? dependencies,
            string? serviceStartName,
            string? password,
            string? displayName);

        [DllImport("advapi32.dll", EntryPoint = "ControlService", ExactSpelling = true, SetLastError = true)]
        [return: MarshalAs(UnmanagedType.Bool)]
        private static extern bool ControlService(SafeServiceHandle hService, uint dwControl, ref SERVICE_STATUS lpServiceStatus);

        [DllImport("advapi32.dll", EntryPoint = "StartServiceW", ExactSpelling = true, CharSet = CharSet.Unicode, SetLastError = true)]
        [return: MarshalAs(UnmanagedType.Bool)]
        private static extern bool StartService(SafeServiceHandle hService, uint dwNumServiceArgs, IntPtr lpServiceArgVectors);

        [DllImport("kernel32.dll", CharSet = CharSet.Auto, SetLastError = true)]
        [return: MarshalAs(UnmanagedType.Bool)]
        public static extern bool GlobalMemoryStatusEx(ref MEMORYSTATUSEX lpBuffer);

        // ── Constants ──
        public const uint SC_MANAGER_ALL_ACCESS = 0xF003F;
        public const uint SERVICE_ALL_ACCESS = 0xF01FF;
        public const uint SERVICE_NO_CHANGE = 0xFFFFFFFF;
        public const uint SERVICE_CONTROL_STOP = 0x00000001;
        public const uint SERVICE_AUTO_START = 0x00000002;
        public const uint SERVICE_DEMAND_START = 0x00000003;
        public const uint SERVICE_DISABLED = 0x00000004;

        /// <summary>
        /// Set the start type (Automatic/Manual/Disabled) for a Windows service via Win32 SCM API.
        /// Used by both StartupOptimizer and TransactionalRollback.
        /// </summary>
        public static bool SetServiceConfig(string serviceName, ServiceStartMode startMode)
        {
            using SafeServiceHandle hSCM = OpenSCManager(null, null, SC_MANAGER_ALL_ACCESS);
            if (hSCM.IsInvalid) return false;

            using SafeServiceHandle hSvc = OpenService(hSCM, serviceName, SERVICE_ALL_ACCESS);
            if (hSvc.IsInvalid) return false;

            uint winStartType = startMode switch
            {
                ServiceStartMode.Automatic => SERVICE_AUTO_START,
                ServiceStartMode.Manual => SERVICE_DEMAND_START,
                ServiceStartMode.Disabled => SERVICE_DISABLED,
                _ => SERVICE_DEMAND_START
            };

            return ChangeServiceConfig(hSvc, SERVICE_NO_CHANGE, winStartType, SERVICE_NO_CHANGE, null, null, IntPtr.Zero, null, null, null, null);
        }

        /// <summary>
        /// Fully restore a service to its original state (start mode + running status).
        /// Used by TransactionalRollback for rollback operations.
        /// </summary>
        public static bool RestoreServiceState(string serviceName, ServiceStartMode startMode, ServiceControllerStatus status)
        {
            using SafeServiceHandle hSCM = OpenSCManager(null, null, SC_MANAGER_ALL_ACCESS);
            if (hSCM.IsInvalid) return false;

            using SafeServiceHandle hSvc = OpenService(hSCM, serviceName, SERVICE_ALL_ACCESS);
            if (hSvc.IsInvalid) return false;

            uint winStartType = startMode switch
            {
                ServiceStartMode.Automatic => SERVICE_AUTO_START,
                ServiceStartMode.Manual => SERVICE_DEMAND_START,
                ServiceStartMode.Disabled => SERVICE_DISABLED,
                _ => SERVICE_DEMAND_START
            };

            ChangeServiceConfig(hSvc, SERVICE_NO_CHANGE, winStartType, SERVICE_NO_CHANGE, null, null, IntPtr.Zero, null, null, null, null);

            try
            {
                using var sc = new ServiceController(serviceName);
                if (status == ServiceControllerStatus.Running && sc.Status != ServiceControllerStatus.Running)
                {
                    StartService(hSvc, 0, IntPtr.Zero);
                }
                else if (status == ServiceControllerStatus.Stopped && sc.Status != ServiceControllerStatus.Stopped)
                {
                    SERVICE_STATUS statusStruct = new SERVICE_STATUS();
                    ControlService(hSvc, SERVICE_CONTROL_STOP, ref statusStruct);
                }
            }
            catch (Exception ex) { OptimaxLogger.Trace($"Failed to restore service run state: {serviceName}", ex); }

            return true;
        }
    }

    // ═══════════════════════════════════════════════════════════
    //  Kernel Memory Management — NtSetSystemInformation for Standby List Purge
    // ═══════════════════════════════════════════════════════════

    public enum SYSTEM_MEMORY_LIST_COMMAND : int
    {
        MemoryCaptureAccessedBits = 0,
        MemoryCaptureAndResetAccessedBits = 1,
        MemoryEmptyWorkingSets = 2,
        MemoryFlushModifiedList = 3,
        MemoryPurgeStandbyList = 4,
        MemoryPurgeLowPriorityStandbyList = 5,
        MemoryCommandMax = 6
    }

    public static class KernelMemoryInterop
    {
        private const int SystemMemoryListInformation = 80;
        private const uint SE_PRIVILEGE_ENABLED = 0x00000002;
        private const uint TOKEN_ADJUST_PRIVILEGES = 0x0020;
        private const uint TOKEN_QUERY = 0x0008;

        [DllImport("ntdll.dll", ExactSpelling = true, SetLastError = false)]
        private static extern int NtSetSystemInformation(int SystemInformationClass, ref int SystemInformation, int SystemInformationLength);

        [DllImport("advapi32.dll", ExactSpelling = true, SetLastError = true)]
        [return: MarshalAs(UnmanagedType.Bool)]
        private static extern bool OpenProcessToken(IntPtr ProcessHandle, uint DesiredAccess, out IntPtr TokenHandle);

        [DllImport("advapi32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
        [return: MarshalAs(UnmanagedType.Bool)]
        private static extern bool LookupPrivilegeValueW(string? lpSystemName, string lpName, out long lpLuid);

        [DllImport("advapi32.dll", ExactSpelling = true, SetLastError = true)]
        [return: MarshalAs(UnmanagedType.Bool)]
        private static extern bool AdjustTokenPrivileges(IntPtr TokenHandle, [MarshalAs(UnmanagedType.Bool)] bool DisableAllPrivileges, ref TOKEN_PRIVILEGES NewState, uint BufferLength, IntPtr PreviousState, IntPtr ReturnLength);

        [DllImport("kernel32.dll", ExactSpelling = true, SetLastError = true)]
        [return: MarshalAs(UnmanagedType.Bool)]
        private static extern bool CloseHandle(IntPtr hObject);

        [DllImport("kernel32.dll", ExactSpelling = true)]
        private static extern IntPtr GetCurrentProcess();

        [StructLayout(LayoutKind.Sequential)]
        private struct TOKEN_PRIVILEGES
        {
            public uint PrivilegeCount;
            public long Luid;
            public uint Attributes;
        }

        public static bool EnablePrivilege(string privilegeName)
        {
            IntPtr tokenHandle = IntPtr.Zero;
            try
            {
                if (!OpenProcessToken(GetCurrentProcess(), TOKEN_ADJUST_PRIVILEGES | TOKEN_QUERY, out tokenHandle))
                    return false;

                if (!LookupPrivilegeValueW(null, privilegeName, out long luid))
                    return false;

                var tp = new TOKEN_PRIVILEGES
                {
                    PrivilegeCount = 1,
                    Luid = luid,
                    Attributes = SE_PRIVILEGE_ENABLED
                };

                if (!AdjustTokenPrivileges(tokenHandle, false, ref tp, 0, IntPtr.Zero, IntPtr.Zero))
                    return false;

                return Marshal.GetLastWin32Error() == 0;
            }
            catch
            {
                return false;
            }
            finally
            {
                if (tokenHandle != IntPtr.Zero)
                    CloseHandle(tokenHandle);
            }
        }

        public static int PurgeStandbyList()
        {
            int command = (int)SYSTEM_MEMORY_LIST_COMMAND.MemoryPurgeStandbyList;
            return NtSetSystemInformation(SystemMemoryListInformation, ref command, sizeof(int));
        }

        public static int FlushModifiedList()
        {
            int command = (int)SYSTEM_MEMORY_LIST_COMMAND.MemoryFlushModifiedList;
            return NtSetSystemInformation(SystemMemoryListInformation, ref command, sizeof(int));
        }
    }
}
