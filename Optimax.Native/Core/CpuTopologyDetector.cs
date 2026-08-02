using System;
using System.Runtime.InteropServices;

namespace Optimax.Core
{
    /// <summary>
    /// Utility to inspect CPU topology and OS Build version for hardware-aware system tweaks.
    /// Uses native kernel32 APIs to avoid external assembly dependencies.
    /// </summary>
    public static class CpuTopologyDetector
    {
        [DllImport("kernel32.dll", SetLastError = true)]
        private static extern bool GetSystemInfo(out SYSTEM_INFO lpSystemInfo);

        [StructLayout(LayoutKind.Sequential)]
        private struct SYSTEM_INFO
        {
            public ushort wProcessorArchitecture;
            public ushort wReserved;
            public uint dwPageSize;
            public IntPtr lpMinimumApplicationAddress;
            public IntPtr lpMaximumApplicationAddress;
            public IntPtr dwActiveProcessorMask;
            public uint dwNumberOfProcessors;
            public uint dwProcessorType;
            public uint dwAllocationGranularity;
            public ushort wProcessorLevel;
            public ushort wProcessorRevision;
        }

        [DllImport("kernel32.dll", SetLastError = true)]
        private static extern bool GetSystemPowerStatus(out SYSTEM_POWER_STATUS lpSystemPowerStatus);

        [StructLayout(LayoutKind.Sequential)]
        private struct SYSTEM_POWER_STATUS
        {
            public byte ACLineStatus;
            public byte BatteryFlag;
            public byte BatteryLifePercent;
            public byte SystemStatusFlag;
            public uint BatteryLifeTime;
            public uint BatteryFullLifeTime;
        }

        private const ushort PROCESSOR_ARCHITECTURE_ARM64 = 12;

        /// <summary>
        /// Detects if running on ARM64 architecture or multi-core topology.
        /// </summary>
        public static bool IsHybridOrArm64Topology()
        {
            try
            {
                GetSystemInfo(out SYSTEM_INFO sysInfo);
                if (sysInfo.wProcessorArchitecture == PROCESSOR_ARCHITECTURE_ARM64 || sysInfo.dwNumberOfProcessors > 8)
                {
                    return true;
                }
            }
            catch (Exception ex)
            {
                OptimaxLogger.Trace("CPU Topology detection check failed", ex);
            }
            return false;
        }

        /// <summary>
        /// Check if system is currently running on battery power (AC offline).
        /// </summary>
        public static bool IsOnBatteryPower()
        {
            try
            {
                if (GetSystemPowerStatus(out SYSTEM_POWER_STATUS status))
                {
                    return status.ACLineStatus == 0; // 0 = Offline / Battery
                }
            }
            catch (Exception ex)
            {
                OptimaxLogger.Trace("System power status check failed", ex);
            }
            return false;
        }

        /// <summary>
        /// Check if running Windows 10 Build 2004 (19041) or newer.
        /// </summary>
        public static bool IsWindows10Build2004OrNewer()
        {
            return Environment.OSVersion.Version.Major >= 10 && Environment.OSVersion.Version.Build >= 19041;
        }

        /// <summary>
        /// Check if running Windows 11 or newer (Build >= 22000).
        /// </summary>
        public static bool IsWindows11OrNewer()
        {
            return Environment.OSVersion.Version.Major >= 10 && Environment.OSVersion.Version.Build >= 22000;
        }

        /// <summary>
        /// Check if running inside a Hypervisor / Virtual Machine.
        /// </summary>
        public static bool IsVirtualMachine()
        {
            try
            {
                using var key = Microsoft.Win32.Registry.LocalMachine.OpenSubKey(@"SYSTEM\CurrentControlSet\Control\SystemInformation");
                if (key != null)
                {
                    string? prodName = key.GetValue("SystemProductName") as string;
                    if (!string.IsNullOrEmpty(prodName) && (prodName.Contains("Virtual", StringComparison.OrdinalIgnoreCase) || prodName.Contains("VMware", StringComparison.OrdinalIgnoreCase) || prodName.Contains("Hyper-V", StringComparison.OrdinalIgnoreCase) || prodName.Contains("QEMU", StringComparison.OrdinalIgnoreCase) || prodName.Contains("VirtualBox", StringComparison.OrdinalIgnoreCase)))
                    {
                        return true;
                    }
                }
            }
            catch (Exception ex)
            {
                OptimaxLogger.Trace("VM check failed", ex);
            }
            return false;
        }
    }
}
