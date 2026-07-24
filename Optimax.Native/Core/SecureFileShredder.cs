using System;
using System.IO;
using System.Security.Cryptography;
using System.Threading.Tasks;

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
        private const int BUFFER_SIZE = 64 * 1024; // 64 KB buffer

        public static ShredReport ShredTarget(string targetPath, ShredAlgorithm algorithm = ShredAlgorithm.DoD5220)
        {
            if (string.IsNullOrWhiteSpace(targetPath))
            {
                return new ShredReport(false, 0, 0, Array.Empty<ShredItemResult>(), "Đường dẫn không hợp lệ.");
            }

            var results = new System.Collections.Generic.List<ShredItemResult>();
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
                    string[] files = Directory.GetFiles(targetPath, "*", SearchOption.AllDirectories);
                    foreach (var file in files)
                    {
                        var res = ShredSingleFile(file, algorithm);
                        results.Add(res);
                        if (res.Success)
                        {
                            successCount++;
                            totalBytes += res.FileSizeBytes;
                        }
                    }

                    // Try delete empty directories
                    try
                    {
                        Directory.Delete(targetPath, true);
                    }
                    catch { }
                }
                catch (Exception ex)
                {
                    return new ShredReport(false, successCount, totalBytes, results.ToArray(), $"Lỗi duyệt thư mục: {ex.Message}");
                }
            }
            else
            {
                return new ShredReport(false, 0, 0, Array.Empty<ShredItemResult>(), "Tệp hoặc thư mục không tồn tại.");
            }

            return new ShredReport(true, successCount, totalBytes, results.ToArray(), $"Đã hủy an toàn {successCount} tệp ({totalBytes / (1024 * 1024):F2} MB).");
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

                using (var fs = new FileStream(filePath, FileMode.Open, FileAccess.Write, FileShare.None))
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
            fs.Flush(true);
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
            fs.Flush(true);
        }
    }
}
