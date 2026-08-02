using System.Collections.Generic;
using Optimax.IPC;

namespace Optimax.Core
{
    public interface IKernelMemoryTrimmer
    {
        MemoryTrimReport TrimSystemMemory(bool forceDeepPurge = false);
    }

    public interface IDeepRegistryScanner
    {
        RegistryScanReport ScanAndClean(bool isDryRun);
    }

    public interface IWindowsDebloater
    {
        List<DebloatItemDto> GetAvailableDebloatItems();
        DebloatReport ApplyDebloatItems(string[] targetItemIds, bool isDryRun);
    }

    public interface ISystemTweaksEngine
    {
        TweakExecutionResult ExecuteTweaks(string[] flags, bool isDryRun = false);
    }

    public interface IWin32SystemApi
    {
        bool EmptyWorkingSet(System.IntPtr hProcess);
        bool GetSystemMemoryStatus(out MEMORYSTATUSEX memStatus);
    }

}

