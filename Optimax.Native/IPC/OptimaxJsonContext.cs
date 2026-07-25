using System.Collections.Generic;
using System.Text.Json.Serialization;
using Optimax.Core;

namespace Optimax.IPC
{
    [JsonSourceGenerationOptions(WriteIndented = true)]
    [JsonSerializable(typeof(ScanReport))]
    [JsonSerializable(typeof(ScanItemResult))]
    [JsonSerializable(typeof(ScanItemResult[]))]
    [JsonSerializable(typeof(SystemStateBackupPackage))]
    [JsonSerializable(typeof(RegistryStateSnapshot))]
    [JsonSerializable(typeof(ServiceStateSnapshot))]
    [JsonSerializable(typeof(List<DynamicCleaningRule>))]
    [JsonSerializable(typeof(FileKeyEntry))]
    [JsonSerializable(typeof(List<FileKeyEntry>))]
    [JsonSerializable(typeof(RegistryScanReport))]
    [JsonSerializable(typeof(RegistryScanItemResult))]
    [JsonSerializable(typeof(RegistryScanItemResult[]))]
    [JsonSerializable(typeof(MonitorNotification))]
    [JsonSerializable(typeof(IPCRequest))]
    [JsonSerializable(typeof(IPCResponse))]
    [JsonSerializable(typeof(IPCStreamChunk))]
    [JsonSerializable(typeof(BrowserScanReport))]
    [JsonSerializable(typeof(BrowserScanItemResult))]
    [JsonSerializable(typeof(BrowserScanItemResult[]))]
    [JsonSerializable(typeof(StartupOptimizerReport))]
    [JsonSerializable(typeof(StartupItemResult))]
    [JsonSerializable(typeof(StartupItemResult[]))]
    [JsonSerializable(typeof(ServiceItemResult))]
    [JsonSerializable(typeof(ServiceItemResult[]))]
    [JsonSerializable(typeof(ShredReport))]
    [JsonSerializable(typeof(ShredItemResult))]
    [JsonSerializable(typeof(ShredItemResult[]))]
    [JsonSerializable(typeof(DebloatItemDto))]
    [JsonSerializable(typeof(DebloatItemDto[]))]
    [JsonSerializable(typeof(List<DebloatItemDto>))]
    [JsonSerializable(typeof(DebloatReport))]
    [JsonSerializable(typeof(MemoryTrimReport))]
    [JsonSerializable(typeof(SystemStatsReport))]
    [JsonSerializable(typeof(BackupItemDto))]
    [JsonSerializable(typeof(BackupItemDto[]))]
    [JsonSerializable(typeof(List<BackupItemDto>))]
    public partial class OptimaxJsonContext : JsonSerializerContext
    {
    }
}
