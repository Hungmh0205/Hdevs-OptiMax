using System;
using System.IO;
using System.IO.Pipes;
using System.Security.AccessControl;
using System.Security.Principal;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Optimax.Core;

namespace Optimax.IPC
{
    public class NamedPipeServer
    {
        private const string PIPE_NAME = "OptimaxIPC";
        private const int MAX_CONCURRENT_CLIENTS = 5;
        private static readonly TimeSpan CLIENT_REQUEST_TIMEOUT = TimeSpan.FromSeconds(8);
        private readonly SemaphoreSlim _clientThrottle = new SemaphoreSlim(MAX_CONCURRENT_CLIENTS, MAX_CONCURRENT_CLIENTS);

        public async Task StartServerAsync(Func<IPCRequest, Task<IPCResponse>> handler, CancellationToken ct)
        {
            await StartServerStreamAsync(async (req, sendChunk) =>
            {
                return await handler(req);
            }, ct);
        }

        public async Task StartServerStreamAsync(Func<IPCRequest, Func<IPCStreamChunk, Task>, Task<IPCResponse>> handler, CancellationToken ct)
        {
            var pipeSecurity = CreateSecurePipeSecurity();

            while (!ct.IsCancellationRequested)
            {
                NamedPipeServerStream? pipe = null;
                try
                {
                    await _clientThrottle.WaitAsync(ct);

                    pipe = NamedPipeServerStreamAot.Create(PIPE_NAME, pipeSecurity);
                    await pipe.WaitForConnectionAsync(ct);

                    var activePipe = pipe;
                    pipe = null; // Ownership transferred to async background worker Task

                    _ = Task.Run(async () =>
                    {
                        using (activePipe)
                        {
                            using var clientCts = CancellationTokenSource.CreateLinkedTokenSource(ct);
                            clientCts.CancelAfter(CLIENT_REQUEST_TIMEOUT);

                            try
                            {
                                // Strict Privilege Verification
                                if (activePipe.SafePipeHandle.IsInvalid || !IsClientAuthorized(activePipe))
                                {
                                    OptimaxLogger.Warn("IPC client connection rejected: Unauthorized client caller.");
                                    if (activePipe.IsConnected) activePipe.Disconnect();
                                    return;
                                }

                                using var reader = new StreamReader(activePipe);
                                using var writer = new StreamWriter(activePipe) { AutoFlush = true };

                                string? line = await reader.ReadLineAsync(clientCts.Token);
                                if (line != null)
                                {
                                    var req = JsonSerializer.Deserialize(line, OptimaxJsonContext.Default.IPCRequest);
                                    if (req != null)
                                    {
                                        string cid = !string.IsNullOrWhiteSpace(req.RequestGuid) ? req.RequestGuid : Guid.NewGuid().ToString("N").Substring(0, 8);
                                        OptimaxLogger.SetCorrelationId(cid);

                                        IPCResponse res;
                                        int chunkIndex = 0;

                                        Func<IPCStreamChunk, Task> sendChunk = async (chunk) =>
                                        {
                                            string chunkJson = JsonSerializer.Serialize(chunk, OptimaxJsonContext.Default.IPCStreamChunk);
                                            await writer.WriteLineAsync(chunkJson.AsMemory(), clientCts.Token);
                                        };

                                        res = await handler(req, sendChunk);

                                        var finalChunk = new IPCStreamChunk(
                                            IsFinal: true,
                                            ChunkIndex: ++chunkIndex,
                                            ProgressPct: 100,
                                            Message: res.Message,
                                            PayloadJson: res.PayloadJson
                                        );

                                        string finalJson = JsonSerializer.Serialize(finalChunk, OptimaxJsonContext.Default.IPCStreamChunk);
                                        await writer.WriteLineAsync(finalJson.AsMemory(), clientCts.Token);
                                    }
                                    else
                                    {
                                        var errorResponse = new IPCResponse(false, "Invalid JSON IPC Request", null);
                                        string errorJson = JsonSerializer.Serialize(errorResponse, OptimaxJsonContext.Default.IPCResponse);
                                        await writer.WriteLineAsync(errorJson.AsMemory(), clientCts.Token);
                                    }
                                }
                            }
                            catch (OperationCanceledException)
                            {
                                OptimaxLogger.Warn("IPC client processing timed out (DoS mitigation triggered).");
                            }
                            catch (Exception ex)
                            {
                                OptimaxLogger.Warn("IPC client processing error", ex);
                            }
                            finally
                            {
                                OptimaxLogger.ClearCorrelationId();
                                _clientThrottle.Release();
                            }
                        }
                    }, ct);
                }
                catch (OperationCanceledException)
                {
                    pipe?.Dispose();
                    _clientThrottle.Release();
                    break;
                }
                catch (Exception ex)
                {
                    pipe?.Dispose();
                    _clientThrottle.Release();
                    OptimaxLogger.Error("IPC pipe accept error", ex);
                    await Task.Delay(200, ct);
                }
            }
        }

        private static PipeSecurity CreateSecurePipeSecurity()
        {
            var pipeSecurity = new PipeSecurity();
            var systemSid = new SecurityIdentifier(WellKnownSidType.LocalSystemSid, null);
            var adminSid = new SecurityIdentifier(WellKnownSidType.BuiltinAdministratorsSid, null);

            // Allow ONLY LocalSystem and BuiltinAdministrators. No Everyone access.
            pipeSecurity.AddAccessRule(new PipeAccessRule(systemSid, PipeAccessRights.FullControl, AccessControlType.Allow));
            pipeSecurity.AddAccessRule(new PipeAccessRule(adminSid, PipeAccessRights.FullControl, AccessControlType.Allow));
            pipeSecurity.SetAccessRuleProtection(isProtected: true, preserveInheritance: false);

            return pipeSecurity;
        }

        private static bool IsClientAuthorized(NamedPipeServerStream pipe)
        {
            try
            {
                bool isAuthorized = false;
                pipe.RunAsClient(() =>
                {
                    using var identity = WindowsIdentity.GetCurrent();
                    var principal = new WindowsPrincipal(identity);
                    isAuthorized = (principal.IsInRole(WindowsBuiltInRole.Administrator) || identity.IsSystem) 
                                   && identity.ImpersonationLevel >= TokenImpersonationLevel.Impersonation;
                });
                return isAuthorized;
            }
            catch (Exception ex)
            {
                OptimaxLogger.Warn($"IPC client authorization check failed: {ex.Message}", ex);
                return false;
            }
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
                PipeOptions.Asynchronous,
                0,
                0);

            try
            {
                pipe.SetAccessControl(pipeSecurity);
            }
            catch (Exception ex)
            {
                // CRITICAL SAFETY FIX: If pipe security ACL cannot be applied, MUST DISPOSE AND FAIL.
                // Never proceed with default un-secured pipe!
                pipe.Dispose();
                OptimaxLogger.Error("CRITICAL: Failed to apply security access control to NamedPipeServerStream. Pipe creation aborted.", ex);
                throw new InvalidOperationException($"Failed to secure IPC pipe '{pipeName}': {ex.Message}", ex);
            }

            return pipe;
        }
    }
}
