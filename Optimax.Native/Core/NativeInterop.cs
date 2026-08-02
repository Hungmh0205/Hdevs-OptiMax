using System;
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
    public class MEMORYSTATUSEX
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

        public MEMORYSTATUSEX()
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

    /// <summary>
    /// Unified Service Control Manager (SCM) P/Invoke wrapper.
    /// Consolidates Win32 SCM operations previously duplicated in TransactionalRollback and StartupOptimizer.
    /// </summary>
    public static class ScmServiceManager
    {
        [DllImport("advapi32.dll", EntryPoint = "OpenSCManagerW", ExactSpelling = true, CharSet = CharSet.Unicode, SetLastError = true)]
        private static extern IntPtr OpenSCManager(string? machineName, string? databaseName, uint dwDesiredAccess);

        [DllImport("advapi32.dll", EntryPoint = "OpenServiceW", ExactSpelling = true, CharSet = CharSet.Unicode, SetLastError = true)]
        private static extern IntPtr OpenService(IntPtr hSCManager, string serviceName, uint dwDesiredAccess);

        [DllImport("advapi32.dll", EntryPoint = "ChangeServiceConfigW", ExactSpelling = true, CharSet = CharSet.Unicode, SetLastError = true)]
        private static extern bool ChangeServiceConfig(
            IntPtr hService,
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
        private static extern bool ControlService(IntPtr hService, uint dwControl, ref SERVICE_STATUS lpServiceStatus);

        [DllImport("advapi32.dll", EntryPoint = "StartServiceW", ExactSpelling = true, CharSet = CharSet.Unicode, SetLastError = true)]
        private static extern bool StartService(IntPtr hService, uint dwNumServiceArgs, IntPtr lpServiceArgVectors);

        [DllImport("advapi32.dll", EntryPoint = "CloseServiceHandle", ExactSpelling = true, SetLastError = true)]
        private static extern bool CloseServiceHandle(IntPtr hSCObject);

        [DllImport("kernel32.dll", CharSet = CharSet.Auto, SetLastError = true)]
        [return: MarshalAs(UnmanagedType.Bool)]
        public static extern bool GlobalMemoryStatusEx([In, Out] MEMORYSTATUSEX lpBuffer);

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
            IntPtr hSCM = OpenSCManager(null, null, SC_MANAGER_ALL_ACCESS);
            if (hSCM == IntPtr.Zero) return false;

            try
            {
                IntPtr hSvc = OpenService(hSCM, serviceName, SERVICE_ALL_ACCESS);
                if (hSvc == IntPtr.Zero) return false;

                try
                {
                    uint winStartType = startMode switch
                    {
                        ServiceStartMode.Automatic => SERVICE_AUTO_START,
                        ServiceStartMode.Manual => SERVICE_DEMAND_START,
                        ServiceStartMode.Disabled => SERVICE_DISABLED,
                        _ => SERVICE_DEMAND_START
                    };

                    return ChangeServiceConfig(hSvc, SERVICE_NO_CHANGE, winStartType, SERVICE_NO_CHANGE, null, null, IntPtr.Zero, null, null, null, null);
                }
                finally
                {
                    CloseServiceHandle(hSvc);
                }
            }
            finally
            {
                CloseServiceHandle(hSCM);
            }
        }

        /// <summary>
        /// Fully restore a service to its original state (start mode + running status).
        /// Used by TransactionalRollback for rollback operations.
        /// </summary>
        public static bool RestoreServiceState(string serviceName, ServiceStartMode startMode, ServiceControllerStatus status)
        {
            IntPtr hSCM = OpenSCManager(null, null, SC_MANAGER_ALL_ACCESS);
            if (hSCM == IntPtr.Zero) return false;

            try
            {
                IntPtr hSvc = OpenService(hSCM, serviceName, SERVICE_ALL_ACCESS);
                if (hSvc == IntPtr.Zero) return false;

                try
                {
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
                finally
                {
                    CloseServiceHandle(hSvc);
                }
            }
            finally
            {
                CloseServiceHandle(hSCM);
            }
        }
    }
}
