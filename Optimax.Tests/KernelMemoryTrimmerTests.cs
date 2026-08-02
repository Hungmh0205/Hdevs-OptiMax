using System;
using Xunit;
using Optimax.Core;

namespace Optimax.Tests
{
    public class MockWin32SystemApi : IWin32SystemApi
    {
        public bool EmptyWorkingSetResult { get; set; } = true;
        public bool PurgeStandbyListResult { get; set; } = true;
        public ulong TotalPhys { get; set; } = 16L * 1024 * 1024 * 1024; // 16 GB
        public ulong AvailPhys { get; set; } = 2L * 1024 * 1024 * 1024;  // 2 GB (12.5% free - triggers anti-thrashing guard bypass)

        public bool EmptyWorkingSetCalled { get; private set; }
        public bool PurgeStandbyListCalled { get; private set; }

        public bool EmptyWorkingSet(IntPtr hProcess)
        {
            EmptyWorkingSetCalled = true;
            return EmptyWorkingSetResult;
        }

        public bool GetSystemMemoryStatus(out MEMORYSTATUSEX memStatus)
        {
            memStatus = default;
            memStatus.Init();
            memStatus.ullTotalPhys = TotalPhys;
            memStatus.ullAvailPhys = AvailPhys;
            memStatus.dwMemoryLoad = (uint)((TotalPhys - AvailPhys) * 100 / TotalPhys);
            return true;
        }

        public bool PurgeStandbyList()
        {
            PurgeStandbyListCalled = true;
            return PurgeStandbyListResult;
        }
    }

    public class KernelMemoryTrimmerTests
    {
        [Fact]
        public void TrimSystemMemory_SkipsWhenMemoryAvailableGreaterThan15Pct()
        {
            var mockApi = new MockWin32SystemApi
            {
                TotalPhys = 16L * 1024 * 1024 * 1024,
                AvailPhys = 8L * 1024 * 1024 * 1024 // 50% available > 15%
            };

            var trimmer = new KernelMemoryTrimmerEngine(mockApi);
            var report = trimmer.TrimSystemMemory(forceDeepPurge: false);

            Assert.False(report.StandbyListFlushed);
            Assert.Contains("Anti-I/O Thrashing Guard", report.StatusMessage);
            Assert.False(mockApi.PurgeStandbyListCalled);
        }

        [Fact]
        public void TrimSystemMemory_ExecutesWhenLowMemory()
        {
            var mockApi = new MockWin32SystemApi
            {
                TotalPhys = 16L * 1024 * 1024 * 1024,
                AvailPhys = 1L * 1024 * 1024 * 1024 // 6.25% available < 15%
            };

            var trimmer = new KernelMemoryTrimmerEngine(mockApi);
            var report = trimmer.TrimSystemMemory(forceDeepPurge: false);

            Assert.True(report.StandbyListFlushed);
            Assert.True(mockApi.EmptyWorkingSetCalled);
            Assert.True(mockApi.PurgeStandbyListCalled);
        }

        [Fact]
        public void TrimSystemMemory_ForceDeepPurgeBypassesAntiThrashingGuard()
        {
            var mockApi = new MockWin32SystemApi
            {
                TotalPhys = 16L * 1024 * 1024 * 1024,
                AvailPhys = 12L * 1024 * 1024 * 1024 // 75% available
            };

            var trimmer = new KernelMemoryTrimmerEngine(mockApi);
            var report = trimmer.TrimSystemMemory(forceDeepPurge: true);

            Assert.True(report.StandbyListFlushed);
            Assert.True(mockApi.PurgeStandbyListCalled);
        }
    }
}
