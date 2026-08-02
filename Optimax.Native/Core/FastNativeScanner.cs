using System;
using System.IO;
using System.Runtime.InteropServices;
using System.Threading;
using System.Threading.Channels;
using System.Threading.Tasks;

namespace Optimax.Core
{
    public readonly struct NativeFileInfo
    {
        public readonly string FullPath;
        public readonly long SizeBytes;

        public NativeFileInfo(string fullPath, long sizeBytes)
        {
            FullPath = fullPath;
            SizeBytes = sizeBytes;
        }
    }

    public static class FastNativeScanner
    {
        [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
        private static extern IntPtr FindFirstFileExW(
            string lpFileName,
            int fInfoLevelId,
            out WIN32_FIND_DATA lpFindFileData,
            int fSearchOp,
            IntPtr lpSearchFilter,
            int dwAdditionalFlags);

        [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
        private static extern bool FindNextFileW(IntPtr hFindFile, out WIN32_FIND_DATA lpFindFileData);

        [DllImport("kernel32.dll", SetLastError = true)]
        private static extern bool FindClose(IntPtr hFindFile);

        private const int FindExInfoBasic = 1;
        private const int FindExSearchNameMatch = 0;
        private const int FIND_FIRST_EX_LARGE_FETCH = 2;
        private static readonly IntPtr INVALID_HANDLE_VALUE = new IntPtr(-1);

        [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
        private struct WIN32_FIND_DATA
        {
            public uint dwFileAttributes;
            public System.Runtime.InteropServices.ComTypes.FILETIME ftCreationTime;
            public System.Runtime.InteropServices.ComTypes.FILETIME ftLastAccessTime;
            public System.Runtime.InteropServices.ComTypes.FILETIME ftLastWriteTime;
            public uint nFileSizeHigh;
            public uint nFileSizeLow;
            public uint dwReserved0;
            public uint dwReserved1;
            [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 260)]
            public string cFileName;
            [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 14)]
            public string cAlternateFileName;
        }

        private const uint FILE_ATTRIBUTE_DIRECTORY = 0x00000010;
        private const uint FILE_ATTRIBUTE_REPARSE_POINT = 0x00000400; // Symlinks / Junction points

        /// <summary>
        /// True multi-core work-stealing parallel scanner powered by System.Threading.Channels.Channel
        /// Uses Win32 FindFirstFileExW basic info & large fetch flags for maximum NTFS throughput.
        /// </summary>
        public static async Task<(int TotalFiles, long TotalBytes)> ScanDirectoriesParallelAsync(
            string[] rootDirectories,
            Action<NativeFileInfo> onFileFound,
            CancellationToken ct = default)
        {
            var dirChannel = Channel.CreateUnbounded<string>(new UnboundedChannelOptions
            {
                SingleWriter = false,
                SingleReader = false
            });

            long totalBytes = 0;
            int totalFiles = 0;
            long pendingWorkItems = 0;

            foreach (var dir in rootDirectories)
            {
                if (string.IsNullOrWhiteSpace(dir)) continue;
                string expanded = Environment.ExpandEnvironmentVariables(dir);
                if (Directory.Exists(expanded))
                {
                    Interlocked.Increment(ref pendingWorkItems);
                    dirChannel.Writer.TryWrite(expanded);
                }
            }

            if (Interlocked.Read(ref pendingWorkItems) == 0)
            {
                dirChannel.Writer.Complete();
                return (0, 0);
            }

            int workerCount = Math.Max(1, Environment.ProcessorCount);
            Task[] workers = new Task[workerCount];

            for (int i = 0; i < workerCount; i++)
            {
                workers[i] = Task.Run(async () =>
                {
                    try
                    {
                        while (await dirChannel.Reader.WaitToReadAsync(ct))
                        {
                            while (dirChannel.Reader.TryRead(out string? currentDir))
                            {
                                if (currentDir == null) continue;
                                try
                                {
                                    ScanDirectoryNative(currentDir, dirChannel.Writer, ref pendingWorkItems, ref totalFiles, ref totalBytes, onFileFound, ct);
                                }
                                finally
                                {
                                    if (Interlocked.Decrement(ref pendingWorkItems) == 0)
                                    {
                                        dirChannel.Writer.TryComplete();
                                    }
                                }
                            }
                        }
                    }
                    catch (OperationCanceledException) { }
                    catch (Exception ex)
                    {
                        OptimaxLogger.Trace("Worker task error in FastNativeScanner", ex);
                    }
                }, ct);
            }

            await Task.WhenAll(workers);
            return (totalFiles, totalBytes);
        }

        private static void ScanDirectoryNative(
            string dirPath,
            ChannelWriter<string> dirWriter,
            ref long pendingWorkItems,
            ref int fileCount,
            ref long byteCount,
            Action<NativeFileInfo> onFileFound,
            CancellationToken ct)
        {
            if (ct.IsCancellationRequested) return;

            string searchPattern = Path.Combine(dirPath, "*");
            IntPtr hFind = FindFirstFileExW(searchPattern, FindExInfoBasic, out WIN32_FIND_DATA findData, FindExSearchNameMatch, IntPtr.Zero, FIND_FIRST_EX_LARGE_FETCH);

            if (hFind == INVALID_HANDLE_VALUE) return;

            try
            {
                do
                {
                    if (ct.IsCancellationRequested) break;

                    string fileName = findData.cFileName;
                    if (fileName == "." || fileName == "..") continue;

                    bool isDir = (findData.dwFileAttributes & FILE_ATTRIBUTE_DIRECTORY) != 0;
                    bool isSymlink = (findData.dwFileAttributes & FILE_ATTRIBUTE_REPARSE_POINT) != 0;

                    string fullPath = Path.Combine(dirPath, fileName);

                    if (isDir)
                    {
                        // Skip symlinks and junction points to prevent infinite loops
                        if (!isSymlink)
                        {
                            Interlocked.Increment(ref pendingWorkItems);
                            dirWriter.TryWrite(fullPath);
                        }
                    }
                    else
                    {
                        long size = ((long)findData.nFileSizeHigh << 32) | findData.nFileSizeLow;
                        Interlocked.Increment(ref fileCount);
                        Interlocked.Add(ref byteCount, size);

                        onFileFound(new NativeFileInfo(fullPath, size));
                    }
                }
                while (FindNextFileW(hFind, out findData));
            }
            catch (Exception ex)
            {
                OptimaxLogger.Trace($"Directory scan error for path: {dirPath}", ex);
            }
            finally
            {
                FindClose(hFind);
            }
        }
    }
}
