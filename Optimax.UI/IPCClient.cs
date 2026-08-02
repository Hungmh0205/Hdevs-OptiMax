using System;
using System.Diagnostics;
using System.IO;
using System.IO.Pipes;
using System.Text.Json;
using System.Threading.Tasks;
using Optimax.IPC;

namespace Optimax.UI
{
    public static class IpcClient
    {
        private const string PIPE_NAME = "OptimaxIPC";

        public static async Task<IPCResponse> SendCommandAsync(
            string command, 
            bool isDryRun = false, 
            string? targetId = null, 
            string? backupId = null, 
            bool enable = true, 
            int serviceStartMode = 2, 
            string[]? flags = null,
            Action<IPCStreamChunk>? onProgressChunk = null,
            System.Threading.CancellationToken cancellationToken = default)
        {
            for (int attempt = 0; attempt < 2; attempt++)
            {
                cancellationToken.ThrowIfCancellationRequested();

                try
                {
                    using var pipe = new NamedPipeClientStream(".", PIPE_NAME, PipeDirection.InOut);
                    await pipe.ConnectAsync(attempt == 0 ? 800 : 2000, cancellationToken);

                    using var reader = new StreamReader(pipe);
                    using var writer = new StreamWriter(pipe) { AutoFlush = true };

                    var req = new IPCRequest(command, isDryRun, backupId, null, targetId, enable, serviceStartMode, flags);
                    string reqJson = JsonSerializer.Serialize(req, OptimaxJsonContext.Default.IPCRequest);
                    await writer.WriteLineAsync(reqJson.AsMemory(), cancellationToken);

                    IPCStreamChunk? lastChunk = null;
                    string? resJson;
                    while ((resJson = await reader.ReadLineAsync(cancellationToken)) != null)
                    {
                        cancellationToken.ThrowIfCancellationRequested();
                        if (string.IsNullOrWhiteSpace(resJson)) continue;

                        try
                        {
                            var chunk = JsonSerializer.Deserialize(resJson, OptimaxJsonContext.Default.IPCStreamChunk);
                            if (chunk != null)
                            {
                                lastChunk = chunk;
                                onProgressChunk?.Invoke(chunk);
                                if (chunk.IsFinal)
                                {
                                    return new IPCResponse(true, chunk.Message, chunk.PayloadJson);
                                }
                                continue;
                            }
                        }
                        catch { }

                        try
                        {
                            var res = JsonSerializer.Deserialize(resJson, OptimaxJsonContext.Default.IPCResponse);
                            if (res != null) return res;
                        }
                        catch { }
                    }

                    if (lastChunk != null)
                    {
                        return new IPCResponse(true, lastChunk.Message, lastChunk.PayloadJson);
                    }
                }
                catch (OperationCanceledException)
                {
                    return new IPCResponse(false, "Operation cancelled by user.", null);
                }
                catch
                {
                    if (attempt == 0)
                    {
                        // Try auto-starting Optimax.exe --ipc-service in the background
                        TryStartIpcServiceDaemon();
                        await Task.Delay(500, cancellationToken);
                        continue;
                    }

                    // Fallback to launching Optimax.exe process directly if IPC Service fails to start
                    return await ExecuteCliFallbackAsync(command, isDryRun, targetId, backupId, flags, cancellationToken);
                }
            }

            return new IPCResponse(false, "Failed to connect to Optimax IPC Engine.", null);
        }

        private static DateTime _lastDaemonSpawnTime = DateTime.MinValue;

        private static void TryStartIpcServiceDaemon()
        {
            if ((DateTime.Now - _lastDaemonSpawnTime).TotalSeconds < 10) return;
            _lastDaemonSpawnTime = DateTime.Now;

            try
            {
                string exePath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "Optimax.exe");
                if (!File.Exists(exePath))
                {
                    string altPath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "..", "Optimax.Native", "Optimax.exe");
                    if (File.Exists(altPath)) exePath = altPath;
                }

                if (File.Exists(exePath))
                {
                    var psi = new ProcessStartInfo
                    {
                        FileName = exePath,
                        Arguments = "--ipc-service",
                        UseShellExecute = true,
                        Verb = "runas",
                        CreateNoWindow = true,
                        WindowStyle = ProcessWindowStyle.Hidden
                    };
                    Process.Start(psi);
                }
            }
            catch { }
        }

        private static async Task<IPCResponse> ExecuteCliFallbackAsync(string command, bool isDryRun, string? targetId, string? backupId, string[]? flags, System.Threading.CancellationToken cancellationToken = default)
        {
            try
            {
                cancellationToken.ThrowIfCancellationRequested();
                string exePath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "Optimax.exe");
                if (!File.Exists(exePath))
                {
                    string altPath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "..", "Optimax.Native", "Optimax.exe");
                    if (File.Exists(altPath)) exePath = altPath;
                }

                string flagsArg = (flags != null && flags.Length > 0) ? "--flags " + string.Join(" ", flags) : "";

                string flag = command switch
                {
                    "scan" => "--dry-run",
                    "clean" => (isDryRun ? "--dry-run " : "") + flagsArg,
                    "clean-registry" => isDryRun ? "--clean-registry --dry-run" : "--clean-registry",
                    "clean-browser" => isDryRun ? "--clean-browser --dry-run" : "--clean-browser",
                    "trim-ram" => "--trim-ram",
                    "get-startup" => "--list-startup",
                    "get-debloat-items" => "--debloat-list",
                    "apply-debloat" => isDryRun ? "--debloat --dry-run" : "--debloat",
                    "shred" => $"--shred \"{targetId}\" --shred-mode {(flags != null && flags.Length > 0 ? flags[0] : "dod")}",
                    "start-monitor" => "--monitor",
                    "schedule-weekly" => "--schedule-weekly Sunday 03:00",
                    "get-stats" => "--get-stats",
                    "get-backups" => "--get-backups",
                    "create-snapshot" => "--create-snapshot",
                    "rollback" => $"--rollback {backupId}",
                    "delete-backup" => $"--delete-backup {backupId}",
                    _ => flagsArg
                };

                var psi = new ProcessStartInfo
                {
                    FileName = File.Exists(exePath) ? exePath : "dotnet",
                    Arguments = File.Exists(exePath) ? flag : $"run --project \"{Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "..", "..", "..", "..", "Optimax.Native")}\" -- {flag}",
                    RedirectStandardOutput = true,
                    StandardOutputEncoding = System.Text.Encoding.UTF8,
                    UseShellExecute = false,
                    CreateNoWindow = true,
                    WindowStyle = ProcessWindowStyle.Hidden
                };

                using var proc = Process.Start(psi);
                if (proc == null) return new IPCResponse(false, "Failed to start Optimax process.", null);

                string output = await proc.StandardOutput.ReadToEndAsync(cancellationToken);
                await proc.WaitForExitAsync(cancellationToken);

                string payloadJson = "";
                string logText = "";

                int startIdx = output.IndexOf("---PAYLOAD_START---");
                int endIdx = output.IndexOf("---PAYLOAD_END---");

                if (startIdx >= 0 && endIdx > startIdx)
                {
                    logText = output.Substring(0, startIdx).Trim();
                    payloadJson = output.Substring(startIdx + "---PAYLOAD_START---".Length, endIdx - startIdx - "---PAYLOAD_START---".Length).Trim();
                }
                else
                {
                    payloadJson = ExtractJsonFromOutput(output);
                    if (!string.IsNullOrEmpty(payloadJson))
                    {
                        int jsonPos = output.IndexOf(payloadJson);
                        if (jsonPos > 0)
                        {
                            logText = output.Substring(0, jsonPos).Trim();
                        }
                    }
                }

                string finalMessage = !string.IsNullOrWhiteSpace(logText) ? logText : "CLI Fallback executed";
                return new IPCResponse(proc.ExitCode == 0, finalMessage, string.IsNullOrEmpty(payloadJson) ? null : payloadJson);
            }
            catch (Exception ex)
            {
                return new IPCResponse(false, $"CLI Fallback error: {ex.Message}", null);
            }
        }

        private static string ExtractJsonFromOutput(string rawOutput)
        {
            if (string.IsNullOrWhiteSpace(rawOutput)) return "";

            // 1. Check explicit PAYLOAD delimiters
            int startIdx = rawOutput.IndexOf("---PAYLOAD_START---");
            int endIdx = rawOutput.IndexOf("---PAYLOAD_END---");
            if (startIdx >= 0 && endIdx > startIdx)
            {
                return rawOutput.Substring(startIdx + "---PAYLOAD_START---".Length, endIdx - startIdx - "---PAYLOAD_START---".Length).Trim();
            }

            // 2. Scan line by line for valid JSON payload (ignoring log headers like [OPTIMAX NATIVE])
            using var reader = new StringReader(rawOutput);
            string? line;
            while ((line = reader.ReadLine()) != null)
            {
                string trimmed = line.Trim();
                if (string.IsNullOrEmpty(trimmed)) continue;

                // Skip console log lines that start with brackets like [OPTIMAX NATIVE]
                if (trimmed.StartsWith("[") && (trimmed.Contains("OPTIMAX") || trimmed.Contains("WINAPP2") || trimmed.Contains("TASK") || trimmed.Contains("INFO") || trimmed.Contains("WARN") || trimmed.Contains("ERROR") || trimmed.Contains("SUCCESS") || trimmed.Contains("✓") || trimmed.Contains("x") || trimmed.Contains("!")))
                {
                    continue;
                }

                if (trimmed.StartsWith("{") || trimmed.StartsWith("["))
                {
                    try
                    {
                        using var doc = JsonDocument.Parse(trimmed);
                        return trimmed;
                    }
                    catch
                    {
                        // Continue checking next lines
                    }
                }
            }

            return "";
        }
    }
}
