using System;
using System.Diagnostics;
using System.IO;
using System.IO.Pipes;
using System.Runtime.InteropServices;
using System.Security.AccessControl;
using System.Security.Principal;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;

namespace Optimax.IPC
{
    public class NamedPipeServer
    {
        private const string PIPE_NAME = "OptimaxIPC";

        [DllImport("kernel32.dll", SetLastError = true)]
        private static extern bool GetNamedPipeClientProcessId(IntPtr Pipe, out uint ClientProcessId);

        [DllImport("kernel32.dll", SetLastError = true)]
        private static extern IntPtr OpenProcess(uint processAccess, bool bInheritHandle, uint processId);

        [DllImport("advapi32.dll", SetLastError = true)]
        private static extern bool OpenProcessToken(IntPtr ProcessHandle, uint DesiredAccess, out IntPtr TokenHandle);

        [DllImport("advapi32.dll", SetLastError = true)]
        private static extern bool GetTokenInformation(IntPtr TokenHandle, int TokenInformationClass, out int TokenInformation, int TokenInformationLength, out int ReturnLength);

        [DllImport("kernel32.dll", SetLastError = true)]
        private static extern bool CloseHandle(IntPtr hObject);

        private const uint PROCESS_QUERY_LIMITED_INFORMATION = 0x1000;
        private const uint TOKEN_QUERY = 0x0008;
        private const int TokenElevation = 20;

        public async Task StartServerAsync(Func<IPCRequest, Task<IPCResponse>> handler, CancellationToken ct)
        {
            await StartServerStreamAsync(async (req, sendChunk) =>
            {
                return await handler(req);
            }, ct);
        }

        public async Task StartServerStreamAsync(Func<IPCRequest, Func<IPCStreamChunk, Task>, Task<IPCResponse>> handler, CancellationToken ct)
        {
            while (!ct.IsCancellationRequested)
            {
                try
                {
                    var pipeSecurity = new PipeSecurity();
                    var systemSid = new SecurityIdentifier(WellKnownSidType.LocalSystemSid, null);
                    var adminSid = new SecurityIdentifier(WellKnownSidType.BuiltinAdministratorsSid, null);

                    pipeSecurity.AddAccessRule(new PipeAccessRule(systemSid, PipeAccessRights.FullControl, AccessControlType.Allow));
                    pipeSecurity.AddAccessRule(new PipeAccessRule(adminSid, PipeAccessRights.FullControl, AccessControlType.Allow));

                    using var pipe = NamedPipeServerStreamAot.Create(PIPE_NAME, pipeSecurity);
                    await pipe.WaitForConnectionAsync(ct);

                    // Client validation: Verify client PID and Token elevation
                    if (pipe.SafePipeHandle.IsInvalid || !IsClientAuthorized(pipe.SafePipeHandle.DangerousGetHandle()))
                    {
                        pipe.Disconnect();
                        continue;
                    }

                    using var reader = new StreamReader(pipe);
                    using var writer = new StreamWriter(pipe) { AutoFlush = true };

                    string? line = await reader.ReadLineAsync(ct);
                    if (line != null)
                    {
                        var req = JsonSerializer.Deserialize(line, OptimaxJsonContext.Default.IPCRequest);
                        IPCResponse res;

                        int chunkIndex = 0;
                        Func<IPCStreamChunk, Task> sendChunk = async (chunk) =>
                        {
                            string chunkJson = JsonSerializer.Serialize(chunk, OptimaxJsonContext.Default.IPCStreamChunk);
                            await writer.WriteLineAsync(chunkJson.AsMemory(), ct);
                        };

                        if (req != null)
                        {
                            res = await handler(req, sendChunk);
                        }
                        else
                        {
                            res = new IPCResponse(false, "Invalid JSON IPC Request", null);
                        }

                        // Send final chunk / completion response
                        var finalChunk = new IPCStreamChunk(
                            IsFinal: true,
                            ChunkIndex: ++chunkIndex,
                            ProgressPct: 100,
                            Message: res.Message,
                            PayloadJson: res.PayloadJson
                        );

                        string finalJson = JsonSerializer.Serialize(finalChunk, OptimaxJsonContext.Default.IPCStreamChunk);
                        await writer.WriteLineAsync(finalJson.AsMemory(), ct);
                    }
                }
                catch (OperationCanceledException)
                {
                    break;
                }
                catch
                {
                    await Task.Delay(500, ct);
                }
            }
        }

        private static bool IsClientAuthorized(IntPtr pipeHandle)
        {
            try
            {
                if (GetNamedPipeClientProcessId(pipeHandle, out uint clientPid))
                {
                    // Allow same process or elevated admin process
                    if (clientPid == Environment.ProcessId) return true;
                    return IsProcessElevated(clientPid);
                }
            }
            catch { }
            return false;
        }

        private static bool IsProcessElevated(uint pid)
        {
            IntPtr hProcess = OpenProcess(PROCESS_QUERY_LIMITED_INFORMATION, false, pid);
            if (hProcess == IntPtr.Zero) return false;

            try
            {
                if (OpenProcessToken(hProcess, TOKEN_QUERY, out IntPtr hToken))
                {
                    try
                    {
                        if (GetTokenInformation(hToken, TokenElevation, out int isElevated, sizeof(int), out _))
                        {
                            return isElevated != 0;
                        }
                    }
                    finally
                    {
                        CloseHandle(hToken);
                    }
                }
            }
            finally
            {
                CloseHandle(hProcess);
            }
            return false;
        }
    }

    internal static class NamedPipeServerStreamAot
    {
        public static NamedPipeServerStream Create(string pipeName, PipeSecurity pipeSecurity)
        {
            var pipe = new NamedPipeServerStream(
                pipeName,
                PipeDirection.InOut,
                NamedPipeServerStream.MaxAllowedServerInstances,
                PipeTransmissionMode.Byte,
                PipeOptions.Asynchronous | PipeOptions.CurrentUserOnly,
                0,
                0);

            try
            {
                pipe.SetAccessControl(pipeSecurity);
            }
            catch { }

            return pipe;
        }
    }
}
