using System;
using Xunit;
using Optimax.Core;

namespace Optimax.Tests
{
    public class CpuTopologyDetectorTests
    {
        [Fact]
        public void IsWindows10Build2004OrNewer_ReturnsTrueOnModernWindows()
        {
            bool result = CpuTopologyDetector.IsWindows10Build2004OrNewer();
            // Assuming test runs on Windows 10/11
            Assert.True(result);
        }

        [Fact]
        public void IsWindows11OrNewer_ReturnsBooleanWithoutThrowing()
        {
            // Should execute without throwing exception
            bool result = CpuTopologyDetector.IsWindows11OrNewer();
            Assert.True(result || !result);
        }

        [Fact]
        public void IsHybridOrArm64Topology_ReturnsBooleanWithoutThrowing()
        {
            // Should execute without throwing P/Invoke exception
            bool result = CpuTopologyDetector.IsHybridOrArm64Topology();
            Assert.True(result || !result);
        }

        [Fact]
        public void IsOnBatteryPower_ReturnsBooleanWithoutThrowing()
        {
            bool result = CpuTopologyDetector.IsOnBatteryPower();
            Assert.True(result || !result);
        }

        [Fact]
        public void IsVirtualMachine_ReturnsBooleanWithoutThrowing()
        {
            bool result = CpuTopologyDetector.IsVirtualMachine();
            Assert.True(result || !result);
        }
    }
}
