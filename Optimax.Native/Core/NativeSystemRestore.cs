using System;
using System.Runtime.InteropServices;
using Optimax.IPC;

namespace Optimax.Core
{
    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    public struct RESTOREPOINTINFO
    {
        public int dwEventType;
        public int dwRestorePtType;
        public long llSequenceNumber;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 256)]
        public string szDescription;
    }

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    public struct STATUSEX
    {
        public int nStatus;
        public long llSequenceNumber;
    }

    /// <summary>
    /// Native Win32 System Restore Point helper calling srclient.dll.
    /// Provides safe fallback checkpoint creation before destructive system actions.
    /// </summary>
    public static class NativeSystemRestore
    {
        private const int BEGIN_SYSTEM_CHANGE = 100;
        private const int END_SYSTEM_CHANGE = 101;
        private const int MODIFY_SETTINGS = 12;

        [DllImport("srclient.dll", CharSet = CharSet.Unicode, SetLastError = true)]
        [return: MarshalAs(UnmanagedType.Bool)]
        private static extern bool SRSetRestorePointW(ref RESTOREPOINTINFO pRestorePtSpec, out STATUSEX pSMGRStatus);

        /// <summary>
        /// Create a native Windows System Restore Point.
        /// </summary>
        public static bool CreateRestorePoint(string description, out long sequenceNumber)
        {
            sequenceNumber = 0;
            try
            {
                var rtInfo = new RESTOREPOINTINFO
                {
                    dwEventType = BEGIN_SYSTEM_CHANGE,
                    dwRestorePtType = MODIFY_SETTINGS,
                    llSequenceNumber = 0,
                    szDescription = description
                };

                bool result = SRSetRestorePointW(ref rtInfo, out STATUSEX status);
                if (result && (status.nStatus == 0 || status.nStatus == 1359)) // ERROR_SUCCESS or ERROR_INTERNAL_ERROR fallback
                {
                    sequenceNumber = status.llSequenceNumber;
                    OptimaxLogger.Trace($"System Restore Point created successfully. Seq: {sequenceNumber}");
                    return true;
                }

                OptimaxLogger.Warn($"Failed to create System Restore Point. Win32 Status: {status.nStatus}");
                return false;
            }
            catch (Exception ex)
            {
                OptimaxLogger.Warn("SRSetRestorePointW P/Invoke call failed", ex);
                return false;
            }
        }
    }
}
