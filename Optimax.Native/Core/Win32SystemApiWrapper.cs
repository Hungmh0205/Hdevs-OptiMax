using System;
using System.Runtime.InteropServices;

namespace Optimax.Core
{
    public class Win32SystemApiWrapper : IWin32SystemApi
    {
        [DllImport("psapi.dll", EntryPoint = "EmptyWorkingSet", SetLastError = true)]
        private static extern bool Win32EmptyWorkingSet(IntPtr hProcess);

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
    }
}
