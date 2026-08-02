using System;
using System.IO;
using Xunit;
using Optimax.Core;

namespace Optimax.Tests
{
    public class SecureFileShredderTests : IDisposable
    {
        private readonly string _tempDirectory;

        public SecureFileShredderTests()
        {
            _tempDirectory = Path.Combine(Path.GetTempPath(), "OptimaxShredderTest_" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(_tempDirectory);
        }

        [Fact]
        public void ShredTarget_SingleFile_ZeroFill_DeletesFile()
        {
            string testFile = Path.Combine(_tempDirectory, "test_zero.txt");
            File.WriteAllText(testFile, "Hello World Data Content To Shred 12345");
            Assert.True(File.Exists(testFile));

            var report = SecureFileShredder.ShredTarget(testFile, ShredAlgorithm.ZeroFill);

            Assert.True(report.Success);
            Assert.Equal(1, report.TotalFilesShredded);
            Assert.False(File.Exists(testFile));
        }

        [Fact]
        public void ShredTarget_SingleFile_DoD5220_DeletesFile()
        {
            string testFile = Path.Combine(_tempDirectory, "test_dod.txt");
            File.WriteAllText(testFile, "Sensitive DoD 5220.22-M Shred Test Content");
            Assert.True(File.Exists(testFile));

            var report = SecureFileShredder.ShredTarget(testFile, ShredAlgorithm.DoD5220);

            Assert.True(report.Success);
            Assert.Equal(1, report.TotalFilesShredded);
            Assert.False(File.Exists(testFile));
        }

        [Fact]
        public void ShredTarget_Directory_ShredsAllFilesAndFolder()
        {
            string subDir = Path.Combine(_tempDirectory, "SubFolder");
            Directory.CreateDirectory(subDir);

            string file1 = Path.Combine(_tempDirectory, "file1.dat");
            string file2 = Path.Combine(subDir, "file2.dat");

            File.WriteAllBytes(file1, new byte[1024]);
            File.WriteAllBytes(file2, new byte[2048]);

            var report = SecureFileShredder.ShredTarget(_tempDirectory, ShredAlgorithm.ZeroFill);

            Assert.True(report.Success);
            Assert.Equal(2, report.TotalFilesShredded);
            Assert.False(File.Exists(file1));
            Assert.False(File.Exists(file2));
            Assert.False(Directory.Exists(_tempDirectory));
        }

        [Fact]
        public void ShredTarget_NonExistentFile_ReturnsFailureReport()
        {
            string nonExistent = Path.Combine(_tempDirectory, "ghost.txt");
            var report = SecureFileShredder.ShredTarget(nonExistent);

            Assert.False(report.Success);
            Assert.Equal(0, report.TotalFilesShredded);
        }

        public void Dispose()
        {
            try
            {
                if (Directory.Exists(_tempDirectory))
                    Directory.Delete(_tempDirectory, recursive: true);
            }
            catch { }
        }
    }
}
