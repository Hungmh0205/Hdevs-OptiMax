using System;
using System.Collections.Generic;
using System.IO;
using System.Security.Cryptography;

namespace Optimax.Core
{
    public enum ShredAlgorithm
    {
        ZeroFill = 0,
        RandomFill = 1,
        DoD5220 = 2
    }

    public class ShredItemResult
    {
        public string FilePath { get; set; } = "";
        public long FileSizeBytes { get; set; }
        public bool Success { get; set; }
        public string Message { get; set; } = "";

        public ShredItemResult() { }

        public ShredItemResult(string filePath, long size, bool success, string message)
        {
            FilePath = filePath;
            FileSizeBytes = size;
            Success = success;
            Message = message;
        }
    }

    public class ShredReport
    {
        public bool Success { get; set; }
        public int TotalFilesShredded { get; set; }
        public long TotalBytesShredded { get; set; }
        public ShredItemResult[] Items { get; set; } = Array.Empty<ShredItemResult>();
        public string StatusMessage { get; set; } = "";

        public ShredReport() { }

        public ShredReport(bool success, int count, long bytes, ShredItemResult[] items, string status)
        {
            Success = success;
            TotalFilesShredded = count;
            TotalBytesShredded = bytes;
            Items = items;
            StatusMessage = status;
        }
    }

    public static class SecureFileShredder
    {
        private const int BUFFER_SIZE = 128 * 1024; // 128 KB buffer for optimal I/O throughput without memory thrashing

        public static ShredReport ShredTarget(string targetPath, ShredAlgorithm algorithm = ShredAlgorithm.DoD5220)
        {
            if (string.IsNullOrWhiteSpace(targetPath) || !Path.IsPathRooted(targetPath))
            {
                return new ShredReport(false, 0, 0, Array.Empty<ShredItemResult>(), "Đường dẫn không hợp lệ.");
            }

            var results = new List<ShredItemResult>();
            long totalBytes = 0;
            int successCount = 0;

            if (File.Exists(targetPath))
            {
                var res = ShredSingleFile(targetPath, algorithm);
                results.Add(res);
                if (res.Success)
                {
                    successCount++;
                    totalBytes += res.FileSizeBytes;
                }
            }
            else if (Directory.Exists(targetPath))
            {
                try
                {
                    // Check if target root directory is a ReparsePoint / Symlink
                    if ((File.GetAttributes(targetPath) & FileAttributes.ReparsePoint) != 0)
                    {
                        return new ShredReport(false, 0, 0, Array.Empty<ShredItemResult>(), "Bỏ qua thư mục là Junction/Symlink để bảo vệ tệp hệ thống.");
                    }

                    // SAFE TRAVERSAL: Enumerate files manually without following subfolder ReparsePoints/Symlinks
                    List<string> safeFiles = EnumerateFilesSafeNoSymlinks(targetPath);
                    foreach (var file in safeFiles)
                    {
                        var res = ShredSingleFile(file, algorithm);
                        results.Add(res);
                        if (res.Success)
                        {
                            successCount++;
                            totalBytes += res.FileSizeBytes;
                        }
                    }

                    // Delete empty subdirectories safely
                    try
                    {
                        Directory.Delete(targetPath, true);
                    }
                    catch (Exception ex)
                    {
                        OptimaxLogger.Trace($"Failed to clean root directory '{targetPath}' after shredding", ex);
                    }
                }
                catch (Exception ex)
                {
                    OptimaxLogger.Warn($"Directory enumeration error during shredding: {targetPath}", ex);
                    return new ShredReport(false, successCount, totalBytes, results.ToArray(), $"Lỗi duyệt thư mục: {ex.Message}");
                }
            }
            else
            {
                return new ShredReport(false, 0, 0, Array.Empty<ShredItemResult>(), "Tệp hoặc thư mục không tồn tại.");
            }

            return new ShredReport(true, successCount, totalBytes, results.ToArray(), $"Đã hủy an toàn {successCount} tệp ({(double)totalBytes / (1024 * 1024):F2} MB).");
        }

        private static List<string> EnumerateFilesSafeNoSymlinks(string rootDirPath)
        {
            var filesList = new List<string>();
            var dirQueue = new Queue<string>();
            dirQueue.Enqueue(rootDirPath);

            while (dirQueue.Count > 0)
            {
                string currentDir = dirQueue.Dequeue();

                try
                {
                    var dirInfo = new DirectoryInfo(currentDir);

                    // Skip subdirectories that are ReparsePoints (Symlinks / NTFS Junctions)
                    if ((dirInfo.Attributes & FileAttributes.ReparsePoint) != 0 && !currentDir.Equals(rootDirPath, StringComparison.OrdinalIgnoreCase))
                    {
                        OptimaxLogger.Warn($"Skipping symlink/junction subdirectory traversal: '{currentDir}'");
                        continue;
                    }

                    foreach (var file in dirInfo.GetFiles())
                    {
                        // Skip individual file symlinks
                        if ((file.Attributes & FileAttributes.ReparsePoint) == 0)
                        {
                            filesList.Add(file.FullName);
                        }
                        else
                        {
                            OptimaxLogger.Warn($"Skipping symlink file: '{file.FullName}'");
                        }
                    }

                    foreach (var subDir in dirInfo.GetDirectories())
                    {
                        if ((subDir.Attributes & FileAttributes.ReparsePoint) == 0)
                        {
                            dirQueue.Enqueue(subDir.FullName);
                        }
                    }
                }
                catch (UnauthorizedAccessException ex)
                {
                    OptimaxLogger.Trace($"Access denied during directory traversal: {currentDir}", ex);
                }
                catch (DirectoryNotFoundException ex)
                {
                    OptimaxLogger.Trace($"Directory not found during traversal: {currentDir}", ex);
                }
            }

            return filesList;
        }

        private static ShredItemResult ShredSingleFile(string filePath, ShredAlgorithm algorithm)
        {
            long fileSize = 0;
            try
            {
                var fileInfo = new FileInfo(filePath);
                if (!fileInfo.Exists)
                {
                    return new ShredItemResult(filePath, 0, false, "Tệp không tồn tại.");
                }

                // Check ReparsePoint / Symlink attribute
                if ((fileInfo.Attributes & FileAttributes.ReparsePoint) != 0)
                {
                    return new ShredItemResult(filePath, 0, false, "Bỏ qua tệp là Symlink/ReparsePoint để bảo vệ tệp hệ thống.");
                }

                // Check read-only attribute
                if (fileInfo.IsReadOnly)
                {
                    fileInfo.IsReadOnly = false;
                }

                fileSize = fileInfo.Length;

                // Check lock status
                var (isLocked, lockingApps) = SafetyEngine.GetFileLockStatus(filePath);
                if (isLocked)
                {
                    return new ShredItemResult(filePath, fileSize, false, $"Tệp đang bị khóa bởi: {string.Join(", ", lockingApps)}");
                }

                using (var fs = new FileStream(filePath, FileMode.Open, FileAccess.Write, FileShare.None, BUFFER_SIZE, FileOptions.SequentialScan | FileOptions.WriteThrough))
                {
                    switch (algorithm)
                    {
                        case ShredAlgorithm.ZeroFill:
                            OverwriteFileWithPattern(fs, fileSize, (byte)0x00);
                            break;

                        case ShredAlgorithm.RandomFill:
                            OverwriteFileWithRandom(fs, fileSize);
                            break;

                        case ShredAlgorithm.DoD5220:
                        default:
                            // Pass 1: Zeros (0x00)
                            OverwriteFileWithPattern(fs, fileSize, (byte)0x00);
                            fs.Position = 0;
                            // Pass 2: Ones (0xFF)
                            OverwriteFileWithPattern(fs, fileSize, (byte)0xFF);
                            fs.Position = 0;
                            // Pass 3: Pseudo-Random
                            OverwriteFileWithRandom(fs, fileSize);
                            break;
                    }

                    // Truncate file size to 0
                    fs.SetLength(0);
                    fs.Flush(true);
                }

                // Rename file randomly before deletion to obscure original filename metadata
                string tempPath = Path.Combine(Path.GetDirectoryName(filePath)!, Guid.NewGuid().ToString("N") + ".tmp");
                try
                {
                    File.Move(filePath, tempPath);
                    File.Delete(tempPath);
                }
                catch
                {
                    File.Delete(filePath);
                }

                return new ShredItemResult(filePath, fileSize, true, "Đã xóa đè và tiêu hủy thành công.");
            }
            catch (Exception ex)
            {
                OptimaxLogger.Warn($"Failed to shred file: {filePath}", ex);
                return new ShredItemResult(filePath, fileSize, false, $"Lỗi xóa đè: {ex.Message}");
            }
        }

        private static void OverwriteFileWithPattern(FileStream fs, long totalBytes, byte pattern)
        {
            byte[] buffer = new byte[BUFFER_SIZE];
            Array.Fill(buffer, pattern);

            long bytesRemaining = totalBytes;
            while (bytesRemaining > 0)
            {
                int bytesToWrite = (int)Math.Min(BUFFER_SIZE, bytesRemaining);
                fs.Write(buffer, 0, bytesToWrite);
                bytesRemaining -= bytesToWrite;
            }
        }

        private static void OverwriteFileWithRandom(FileStream fs, long totalBytes)
        {
            byte[] buffer = new byte[BUFFER_SIZE];
            long bytesRemaining = totalBytes;

            while (bytesRemaining > 0)
            {
                int bytesToWrite = (int)Math.Min(BUFFER_SIZE, bytesRemaining);
                RandomNumberGenerator.Fill(buffer.AsSpan(0, bytesToWrite));
                fs.Write(buffer, 0, bytesToWrite);
                bytesRemaining -= bytesToWrite;
            }
        }
    }
}
