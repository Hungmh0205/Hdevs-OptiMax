using System;
using System.Runtime.InteropServices;

namespace Optimax.Core
{
    /// <summary>
    /// Utility to inspect CPU topology and OS Build version for hardware-aware system tweaks.
    /// Uses GetLogicalProcessorInformationEx to accurately detect Intel Hybrid (P-core/E-core) topology.
    /// Uses GetNativeSystemInfo (correct API for WOW64-aware architecture detection).
    /// </summary>
    public static class CpuTopologyDetector
    {
        // ═══════════════════════════════════════════════════════════
        //  GetNativeSystemInfo — correct P/Invoke (void return, not bool)
        // ═══════════════════════════════════════════════════════════

        [DllImport("kernel32.dll", ExactSpelling = true)]
        private static extern void GetNativeSystemInfo(ref SYSTEM_INFO lpSystemInfo);

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

        // ═══════════════════════════════════════════════════════════
        //  GetLogicalProcessorInformationEx — for Hybrid CPU detection
        // ═══════════════════════════════════════════════════════════

        [DllImport("kernel32.dll", SetLastError = true)]
        [return: MarshalAs(UnmanagedType.Bool)]
        private static extern bool GetLogicalProcessorInformationEx(
            int RelationshipType,
            IntPtr Buffer,
            ref uint ReturnedLength);

        private const int RelationProcessorCore = 0;

        // PROCESSOR_RELATIONSHIP structure layout (simplified for EfficiencyClass extraction)
        // Offset 0: Flags (byte)
        // Offset 1: EfficiencyClass (byte) — 0 = P-core, 1+ = E-core (on Intel Hybrid)
        // Offset 2: Reserved[20] (20 bytes)
        // Offset 22: GroupCount (ushort)
        // The full SYSTEM_LOGICAL_PROCESSOR_INFORMATION_EX header:
        // Offset 0: Relationship (int, 4 bytes)
        // Offset 4: Size (uint, 4 bytes)
        // Offset 8: Union start → PROCESSOR_RELATIONSHIP

        // ═══════════════════════════════════════════════════════════
        //  Power Status
        // ═══════════════════════════════════════════════════════════

        [DllImport("kernel32.dll", SetLastError = true)]
        [return: MarshalAs(UnmanagedType.Bool)]
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

        // Cache the hybrid detection result (CPU topology doesn't change at runtime)
        private static int _hybridCacheResult = -1; // -1 = not cached, 0 = false, 1 = true

        /// <summary>
        /// Detects if running on a Hybrid CPU (Intel 12th+ with P-core and E-core) or ARM64 architecture.
        /// Uses GetLogicalProcessorInformationEx with EfficiencyClass to accurately identify hybrid topology.
        /// Does NOT false-positive on high-core-count traditional CPUs (Ryzen 9, Threadripper, Xeon).
        /// </summary>
        public static bool IsHybridOrArm64Topology()
        {
            // Return cached result if available
            int cached = _hybridCacheResult;
            if (cached >= 0) return cached == 1;

            bool result = DetectHybridOrArm64Internal();
            _hybridCacheResult = result ? 1 : 0;
            return result;
        }

        private static bool DetectHybridOrArm64Internal()
        {
            try
            {
                // Step 1: Check ARM64 architecture via GetNativeSystemInfo
                var sysInfo = new SYSTEM_INFO();
                GetNativeSystemInfo(ref sysInfo);
                if (sysInfo.wProcessorArchitecture == PROCESSOR_ARCHITECTURE_ARM64)
                {
                    return true;
                }

                // Step 2: Check Hybrid CPU via GetLogicalProcessorInformationEx (Windows 10 21H2+)
                // Enumerate processor cores and check if EfficiencyClass values differ
                if (DetectHybridViaLogicalProcessorInfo())
                {
                    return true;
                }

                // Step 3: Fallback — check registry for Intel Hybrid indicator
                if (DetectHybridViaRegistry())
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
        /// Use GetLogicalProcessorInformationEx(RelationProcessorCore) to enumerate all cores.
        /// If cores have different EfficiencyClass values, this is a hybrid CPU.
        /// EfficiencyClass was introduced in Windows 10 Build 21370 (21H2).
        /// </summary>
        private static bool DetectHybridViaLogicalProcessorInfo()
        {
            try
            {
                // First call to get required buffer size
                uint bufferSize = 0;
                GetLogicalProcessorInformationEx(RelationProcessorCore, IntPtr.Zero, ref bufferSize);

                if (bufferSize == 0) return false;

                IntPtr buffer = Marshal.AllocHGlobal((int)bufferSize);
                try
                {
                    if (!GetLogicalProcessorInformationEx(RelationProcessorCore, buffer, ref bufferSize))
                        return false;

                    bool hasEfficiencyClass0 = false;
                    bool hasEfficiencyClassNon0 = false;

                    uint offset = 0;
                    while (offset < bufferSize)
                    {
                        IntPtr current = IntPtr.Add(buffer, (int)offset);

                        // Read SYSTEM_LOGICAL_PROCESSOR_INFORMATION_EX header
                        // int Relationship (4 bytes) + uint Size (4 bytes) = 8 bytes header
                        int relationship = Marshal.ReadInt32(current, 0);
                        uint structSize = (uint)Marshal.ReadInt32(current, 4);

                        if (structSize == 0) break; // Safety guard

                        if (relationship == RelationProcessorCore)
                        {
                            // PROCESSOR_RELATIONSHIP starts at offset 8
                            // Offset 8+0: Flags (byte)
                            // Offset 8+1: EfficiencyClass (byte)
                            byte efficiencyClass = Marshal.ReadByte(current, 8 + 1);

                            if (efficiencyClass == 0)
                                hasEfficiencyClass0 = true;
                            else
                                hasEfficiencyClassNon0 = true;

                            // Early exit: if we found both classes, it's hybrid
                            if (hasEfficiencyClass0 && hasEfficiencyClassNon0)
                                return true;
                        }

                        offset += structSize;
                    }

                    return hasEfficiencyClass0 && hasEfficiencyClassNon0;
                }
                finally
                {
                    Marshal.FreeHGlobal(buffer);
                }
            }
            catch (EntryPointNotFoundException)
            {
                // GetLogicalProcessorInformationEx not available on this OS version
                return false;
            }
            catch (Exception ex)
            {
                OptimaxLogger.Trace("GetLogicalProcessorInformationEx hybrid detection failed", ex);
                return false;
            }
        }

        /// <summary>
        /// Fallback: Check registry for Intel Hybrid/big.LITTLE indicators.
        /// Only used when GetLogicalProcessorInformationEx doesn't support EfficiencyClass.
        /// </summary>
        private static bool DetectHybridViaRegistry()
        {
            try
            {
                // Check if Intel Thread Director or HybridCPU flag exists
                using var key = Microsoft.Win32.Registry.LocalMachine.OpenSubKey(
                    @"HARDWARE\DESCRIPTION\System\CentralProcessor\0");
                if (key != null)
                {
                    // Intel 12th gen+ with Thread Director has "HybridCPU" value
                    var hybridVal = key.GetValue("HybridCPU");
                    if (hybridVal is int hv && hv > 0)
                        return true;

                    // Check ProcessorNameString for known hybrid families
                    string? cpuName = key.GetValue("ProcessorNameString") as string;
                    if (!string.IsNullOrEmpty(cpuName))
                    {
                        // Intel 12th gen (Alder Lake), 13th (Raptor Lake), 14th (Meteor Lake)
                        // Pattern: "12th Gen Intel", "13th Gen Intel", "14th Gen Intel", "Core Ultra"
                        if (cpuName.Contains("12th Gen Intel", StringComparison.OrdinalIgnoreCase) ||
                            cpuName.Contains("13th Gen Intel", StringComparison.OrdinalIgnoreCase) ||
                            cpuName.Contains("14th Gen Intel", StringComparison.OrdinalIgnoreCase) ||
                            cpuName.Contains("Core Ultra", StringComparison.OrdinalIgnoreCase))
                        {
                            return true;
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                OptimaxLogger.Trace("Registry-based hybrid CPU detection failed", ex);
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
