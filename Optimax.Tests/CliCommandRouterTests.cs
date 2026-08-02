using Xunit;
using Optimax.Core;

namespace Optimax.Tests
{
    public class CliCommandRouterTests
    {
        [Fact]
        public void ParseArguments_DryRunFlag_SetsDryRunTrue()
        {
            string[] args = new[] { "--dry-run" };
            var opts = CliCommandRouter.ParseArguments(args);

            Assert.True(opts.IsDryRun);
        }

        [Fact]
        public void ParseArguments_TrimRamFlag_SetsIsTrimRamTrue()
        {
            string[] args = new[] { "--trim-ram" };
            var opts = CliCommandRouter.ParseArguments(args);

            Assert.True(opts.IsTrimRam);
        }

        [Fact]
        public void ParseArguments_FlagsList_ParsesSystemTweaksFlags()
        {
            string[] args = new[] { "--flags", "-systemp", "-standbyram", "-msimode" };
            var opts = CliCommandRouter.ParseArguments(args);

            Assert.Equal(3, opts.CliFlags.Count);
            Assert.Contains("-systemp", opts.CliFlags);
            Assert.Contains("-standbyram", opts.CliFlags);
            Assert.Contains("-msimode", opts.CliFlags);
        }

        [Fact]
        public void ParseArguments_ShredMode_ParsesPathAndMode()
        {
            string[] args = new[] { "--shred", "C:\\temp\\file.txt", "--shred-mode", "zero" };
            var opts = CliCommandRouter.ParseArguments(args);

            Assert.Equal("C:\\temp\\file.txt", opts.ShredPath);
            Assert.Equal("zero", opts.ShredModeStr);
        }

        [Fact]
        public void ParseArguments_UpdateWinapp2_SetsFlagTrue()
        {
            string[] args = new[] { "--update-winapp2", "--dry-run" };
            var opts = CliCommandRouter.ParseArguments(args);

            Assert.True(opts.IsUpdateWinapp2);
            Assert.True(opts.IsDryRun);
        }
    }
}
