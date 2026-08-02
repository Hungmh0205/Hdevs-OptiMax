using System;
using System.IO;
using System.Runtime.InteropServices;

namespace Optimax.Core
{
    public static class SafetyEngine
    {
        [DllImport("rstrtmgr.dll", CharSet = CharSet.Unicode)]
        private static extern int RmStartSession(out uint pSessionHandle, uint dwSessionFlags, string strSessionKey);

        [DllImport("rstrtmgr.dll")]
        private static extern int RmEndSession(uint dwSessionHandle);

        [DllImport("rstrtmgr.dll", CharSet = CharSet.Unicode)]
        private static extern int RmRegisterResources(
            uint dwSessionHandle,
            uint nFiles,
            string[] rgsFilenames,
            uint nApplications,
            IntPtr rgApplications,
            uint nServices,
            IntPtr rgsServiceNames);

        [DllImport("rstrtmgr.dll", CharSet = CharSet.Unicode)]
        private static extern int RmGetList(
            uint dwSessionHandle,
            out uint pnProcInfoNeeded,
            ref uint pnProcInfo,
            [In, Out] RM_PROCESS_INFO[]? rgAffectedApps,
            out uint lpdwRebootReasons);

        [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
        private static extern bool MoveFileEx(string lpExistingFileName, string? lpNewFileName, uint dwFlags);

        private const uint MOVEFILE_DELAY_UNTIL_REBOOT = 0x4;

        [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
        private struct RM_UNIQUE_PROCESS
        {
            public uint dwProcessId;
            public System.Runtime.InteropServices.ComTypes.FILETIME ProcessStartTime;
        }

        [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
        private struct RM_PROCESS_INFO
        {
            public RM_UNIQUE_PROCESS Process;
            [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 256)]
            public string strAppName;
            [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 64)]
            public string strServiceShortName;
            public int ApplicationType;
            public uint AppStatus;
            public uint TSSessionId;
            [MarshalAs(UnmanagedType.Bool)]
            public bool bRestartable;
        }

        public static (bool IsLocked, string[] LockingApps) GetFileLockStatus(string filePath)
        {
            if (!File.Exists(filePath)) return (false, Array.Empty<string>());

            uint handle;
            string sessionKey = Guid.NewGuid().ToString();
            int res = RmStartSession(out handle, 0, sessionKey);
            if (res != 0) return (false, Array.Empty<string>());

            try
            {
                string[] resources = new[] { filePath };
                res = RmRegisterResources(handle, (uint)resources.Length, resources, 0, IntPtr.Zero, 0, IntPtr.Zero);
                if (res != 0) return (false, Array.Empty<string>());

                uint procInfoNeeded = 0;
                uint procInfo = 0;
                uint rebootReasons = 0;

                res = RmGetList(handle, out procInfoNeeded, ref procInfo, null, out rebootReasons);
                if (res == 234) // ERROR_MORE_DATA
                {
                    RM_PROCESS_INFO[] processInfo = new RM_PROCESS_INFO[procInfoNeeded];
                    procInfo = procInfoNeeded;
                    res = RmGetList(handle, out procInfoNeeded, ref procInfo, processInfo, out rebootReasons);
                    if (res == 0)
                    {
                        string[] apps = new string[procInfo];
                        for (int i = 0; i < procInfo; i++)
                        {
                            apps[i] = string.IsNullOrEmpty(processInfo[i].strAppName)
                                ? $"PID:{processInfo[i].Process.dwProcessId}"
                                : processInfo[i].strAppName;
                        }
                        return (true, apps);
                    }
                }
                return (false, Array.Empty<string>());
            }
            catch (Exception ex)
            {
                OptimaxLogger.Warn($"Restart Manager session failed for file: {filePath}", ex);
                return (false, Array.Empty<string>());
            }
            finally
            {
                RmEndSession(handle);
            }
        }

        public static bool ScheduleDeleteOnReboot(string filePath)
        {
            if (string.IsNullOrEmpty(filePath)) return false;
            return MoveFileEx(filePath, null, MOVEFILE_DELAY_UNTIL_REBOOT);
        }

        public static bool IsDriveReadyAndLocal(string path)
        {
            try
            {
                string root = Path.GetPathRoot(path) ?? "C:\\";
                var drive = new DriveInfo(root);
                return drive.IsReady && drive.DriveType == DriveType.Fixed;
            }
            catch (Exception ex)
            {
                OptimaxLogger.Trace($"Drive readiness check failed for: {path}", ex);
                return false;
            }
        }
    }
}
