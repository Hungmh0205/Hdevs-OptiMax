using System;
using System.IO;
using System.Net.Http;
using System.Security.Cryptography;
using System.Threading;
using System.Threading.Tasks;

namespace Optimax.Core
{
    /// <summary>
    /// Auto-updater for Winapp2.ini community cleaning ruleset.
    /// Downloads the latest version from the community GitHub repository,
    /// verifies integrity, and replaces the local copy safely.
    /// </summary>
    public static class Winapp2Updater
    {
        // Official community Winapp2.ini raw URL (maintained by MoscaDotTo)
        private const string Winapp2RawUrl = "https://raw.githubusercontent.com/MoscaDotTo/Winapp2/master/Winapp2.ini";
        private const int DownloadTimeoutSeconds = 60;

        /// <summary>
        /// Check for updates and download the latest Winapp2.ini if different from local copy.
        /// </summary>
        /// <param name="localWinapp2Path">Path to the local Winapp2.ini file</param>
        /// <param name="isDryRun">If true, only checks for updates without downloading</param>
        /// <returns>Update report with status and details</returns>
        public static async Task<Winapp2UpdateReport> UpdateAsync(string? localWinapp2Path = null, bool isDryRun = false, CancellationToken ct = default)
        {
            if (string.IsNullOrEmpty(localWinapp2Path))
            {
                localWinapp2Path = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "Winapp2.ini");
            }

            try
            {
                // Calculate hash of existing file (if exists)
                string? localHash = null;
                long localSize = 0;
                if (File.Exists(localWinapp2Path))
                {
                    localHash = ComputeFileHash(localWinapp2Path);
                    localSize = new FileInfo(localWinapp2Path).Length;
                }

                if (isDryRun)
                {
                    return new Winapp2UpdateReport(
                        success: true,
                        updated: false,
                        message: $"[DRY-RUN] Sẽ kiểm tra cập nhật Winapp2.ini từ GitHub community. File hiện tại: {(localHash != null ? $"{localSize / 1024} KB" : "không tồn tại")}.",
                        localHash: localHash,
                        remoteHash: null
                    );
                }

                OptimaxLogger.Trace($"Downloading latest Winapp2.ini from {Winapp2RawUrl}...");

                using var httpClient = new HttpClient();
                httpClient.Timeout = TimeSpan.FromSeconds(DownloadTimeoutSeconds);
                httpClient.DefaultRequestHeaders.UserAgent.ParseAdd("Optimax/2.0");

                byte[] remoteBytes = await httpClient.GetByteArrayAsync(Winapp2RawUrl, ct);

                if (remoteBytes.Length < 1024)
                {
                    return new Winapp2UpdateReport(false, false, "Dữ liệu tải về quá nhỏ, có thể bị lỗi. Bỏ qua cập nhật.", localHash, null);
                }

                string remoteHash = ComputeHash(remoteBytes);

                // Compare hashes — skip if identical
                if (string.Equals(localHash, remoteHash, StringComparison.OrdinalIgnoreCase))
                {
                    return new Winapp2UpdateReport(true, false, $"Winapp2.ini đã là phiên bản mới nhất ({localSize / 1024} KB, SHA256: {remoteHash[..16]}...).", localHash, remoteHash);
                }

                // Backup existing file before overwrite
                if (File.Exists(localWinapp2Path))
                {
                    string backupPath = localWinapp2Path + $".backup_{DateTime.UtcNow:yyyyMMdd_HHmmss}";
                    File.Copy(localWinapp2Path, backupPath, overwrite: true);
                    OptimaxLogger.Trace($"Backed up existing Winapp2.ini to: {backupPath}");
                }

                // Write new file atomically (write to temp, then move)
                string tempPath = localWinapp2Path + ".tmp";
                await File.WriteAllBytesAsync(tempPath, remoteBytes, ct);
                File.Move(tempPath, localWinapp2Path, overwrite: true);

                long newSize = remoteBytes.Length;
                OptimaxLogger.Warn($"[AUTO-UPDATE] Winapp2.ini updated successfully. Size: {localSize / 1024} KB → {newSize / 1024} KB, Hash: {remoteHash[..16]}...");

                return new Winapp2UpdateReport(
                    true,
                    true,
                    $"Đã cập nhật Winapp2.ini thành công! Kích thước: {localSize / 1024} KB → {newSize / 1024} KB.",
                    localHash,
                    remoteHash
                );
            }
            catch (HttpRequestException ex)
            {
                OptimaxLogger.Warn("Winapp2.ini download failed (network error)", ex);
                return new Winapp2UpdateReport(false, false, $"Lỗi kết nối mạng: {ex.Message}", null, null);
            }
            catch (TaskCanceledException)
            {
                return new Winapp2UpdateReport(false, false, "Quá thời gian chờ tải Winapp2.ini.", null, null);
            }
            catch (Exception ex)
            {
                OptimaxLogger.Warn("Winapp2.ini update failed", ex);
                return new Winapp2UpdateReport(false, false, $"Lỗi cập nhật: {ex.Message}", null, null);
            }
        }

        private static string ComputeFileHash(string filePath)
        {
            using var sha256 = SHA256.Create();
            using var stream = File.OpenRead(filePath);
            byte[] hash = sha256.ComputeHash(stream);
            return Convert.ToHexString(hash);
        }

        private static string ComputeHash(byte[] data)
        {
            using var sha256 = SHA256.Create();
            byte[] hash = sha256.ComputeHash(data);
            return Convert.ToHexString(hash);
        }
    }

    public class Winapp2UpdateReport
    {
        public bool Success { get; set; }
        public bool Updated { get; set; }
        public string Message { get; set; }
        public string? LocalHash { get; set; }
        public string? RemoteHash { get; set; }

        public Winapp2UpdateReport() { Message = ""; }

        public Winapp2UpdateReport(bool success, bool updated, string message, string? localHash, string? remoteHash)
        {
            Success = success;
            Updated = updated;
            Message = message;
            LocalHash = localHash;
            RemoteHash = remoteHash;
        }
    }
}
