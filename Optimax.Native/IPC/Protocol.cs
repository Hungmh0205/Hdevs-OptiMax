using System;
using System.Collections.Generic;
using System.Text.Json.Serialization;
using Microsoft.Win32;

namespace Optimax.IPC
{
    // IPC Request & Response
    public record IPCRequest(
        [property: JsonPropertyName("command")] string Command,
        [property: JsonPropertyName("isDryRun")] bool IsDryRun = false,
        [property: JsonPropertyName("backupId")] string? BackupId = null,
        [property: JsonPropertyName("rulesFile")] string? RulesFile = null,
        [property: JsonPropertyName("targetId")] string? TargetId = null,
        [property: JsonPropertyName("enable")] bool Enable = true,
        [property: JsonPropertyName("serviceStartMode")] int ServiceStartMode = 2,
        [property: JsonPropertyName("flags")] string[]? Flags = null
    );

    public record IPCResponse(
        [property: JsonPropertyName("success")] bool Success,
        [property: JsonPropertyName("message")] string Message,
        [property: JsonPropertyName("payloadJson")] string? PayloadJson
    );

    public record IPCStreamChunk(
        [property: JsonPropertyName("isFinal")] bool IsFinal,
        [property: JsonPropertyName("chunkIndex")] int ChunkIndex,
        [property: JsonPropertyName("progressPct")] int ProgressPct,
        [property: JsonPropertyName("message")] string Message,
        [property: JsonPropertyName("payloadJson")] string? PayloadJson
    );

    // System Scan DTOs
    public record ScanItemResult(
        [property: JsonPropertyName("path")] string Path,
        [property: JsonPropertyName("sizeBytes")] long SizeBytes,
        [property: JsonPropertyName("isLocked")] bool IsLocked,
        [property: JsonPropertyName("lockingProcesses")] string[] LockingProcesses,
        [property: JsonPropertyName("actionRequired")] string ActionRequired
    );

    public record ScanReport(
        [property: JsonPropertyName("isDryRun")] bool IsDryRun,
        [property: JsonPropertyName("totalFilesFound")] int TotalFilesFound,
        [property: JsonPropertyName("totalBytesReclaimable")] long TotalBytesReclaimable,
        [property: JsonPropertyName("riskLevel")] string RiskLevel,
        [property: JsonPropertyName("items")] ScanItemResult[] Items
    );

    // Registry Scan DTOs
    public record RegistryScanItemResult(
        [property: JsonPropertyName("hive")] string Hive,
        [property: JsonPropertyName("subKey")] string SubKey,
        [property: JsonPropertyName("valueName")] string ValueName,
        [property: JsonPropertyName("category")] string Category,
        [property: JsonPropertyName("targetPath")] string TargetPath,
        [property: JsonPropertyName("actionRequired")] string ActionRequired
    );

    public record RegistryScanReport(
        [property: JsonPropertyName("isDryRun")] bool IsDryRun,
        [property: JsonPropertyName("totalIssuesFound")] int TotalIssuesFound,
        [property: JsonPropertyName("backupId")] string? BackupId,
        [property: JsonPropertyName("items")] RegistryScanItemResult[] Items
    );

    // Transactional Rollback DTOs
    public class RegistryStateSnapshot
    {
        [JsonPropertyName("keyPath")] public string KeyPath { get; set; } = string.Empty;
        [JsonPropertyName("valueName")] public string ValueName { get; set; } = string.Empty;
        [JsonPropertyName("originalValue")] public object? OriginalValue { get; set; }
        [JsonPropertyName("valueKind")] public RegistryValueKind ValueKind { get; set; }
        [JsonPropertyName("existed")] public bool Existed { get; set; }
    }

    public class ServiceStateSnapshot
    {
        [JsonPropertyName("serviceName")] public string ServiceName { get; set; } = string.Empty;
        [JsonPropertyName("originalStartMode")] public int OriginalStartMode { get; set; }
        [JsonPropertyName("originalStatus")] public int OriginalStatus { get; set; }
    }

    public class SystemStateBackupPackage
    {
        [JsonPropertyName("backupId")] public string BackupId { get; set; } = Guid.NewGuid().ToString("N");
        [JsonPropertyName("timestamp")] public DateTime Timestamp { get; set; } = DateTime.UtcNow;
        [JsonPropertyName("registryEntries")] public List<RegistryStateSnapshot> RegistryEntries { get; set; } = new();
        [JsonPropertyName("serviceEntries")] public List<ServiceStateSnapshot> ServiceEntries { get; set; } = new();
    }

    public record BackupItemDto(
        [property: JsonPropertyName("backupId")] string BackupId,
        [property: JsonPropertyName("timestamp")] DateTime Timestamp,
        [property: JsonPropertyName("registryCount")] int RegistryCount,
        [property: JsonPropertyName("serviceCount")] int ServiceCount
    );

    // Dynamic Rule DTOs
    public class RuleCondition
    {
        [JsonPropertyName("minOsBuild")] public int MinOsBuild { get; set; } = 0;
        [JsonPropertyName("maxOsBuild")] public int MaxOsBuild { get; set; } = int.MaxValue;
        [JsonPropertyName("targetAppExecutable")] public string? TargetAppExecutable { get; set; }
        [JsonPropertyName("minProductVersion")] public string? MinProductVersion { get; set; }
    }

    public class FileKeyEntry
    {
        [JsonPropertyName("basePath")] public string BasePath { get; set; } = string.Empty;
        [JsonPropertyName("pattern")] public string Pattern { get; set; } = "*.*";

        public FileKeyEntry() { }
        public FileKeyEntry(string basePath, string pattern)
        {
            BasePath = basePath;
            Pattern = pattern;
        }
    }

    public class DynamicCleaningRule
    {
        [JsonPropertyName("ruleId")] public string RuleId { get; set; } = string.Empty;
        [JsonPropertyName("name")] public string Name { get; set; } = string.Empty;
        [JsonPropertyName("condition")] public RuleCondition Condition { get; set; } = new();
        [JsonPropertyName("fileKeys")] public List<FileKeyEntry> FileKeys { get; set; } = new();
        [JsonPropertyName("basePaths")] public List<string> BasePaths { get; set; } = new();
        [JsonPropertyName("includePatterns")] public List<string> IncludePatterns { get; set; } = new();
        [JsonPropertyName("excludeRegex")] public List<string> ExcludeRegex { get; set; } = new();
    }

    // Realtime Monitor DTO
    public record MonitorNotification(
        [property: JsonPropertyName("eventType")] string EventType,
        [property: JsonPropertyName("totalJunkBytes")] long TotalJunkBytes,
        [property: JsonPropertyName("thresholdBytes")] long ThresholdBytes,
        [property: JsonPropertyName("message")] string Message,
        [property: JsonPropertyName("timestamp")] DateTime Timestamp
    );

    // Browser Optimizer DTOs
    public record BrowserScanItemResult(
        [property: JsonPropertyName("browserName")] string BrowserName,
        [property: JsonPropertyName("dbPath")] string DbPath,
        [property: JsonPropertyName("originalSizeBytes")] long OriginalSizeBytes,
        [property: JsonPropertyName("reducedSizeBytes")] long ReducedSizeBytes,
        [property: JsonPropertyName("bytesReclaimed")] long BytesReclaimed,
        [property: JsonPropertyName("isLocked")] bool IsLocked,
        [property: JsonPropertyName("actionTaken")] string ActionTaken
    );

    public record BrowserScanReport(
        [property: JsonPropertyName("isDryRun")] bool IsDryRun,
        [property: JsonPropertyName("totalDatabasesScanned")] int TotalDatabasesScanned,
        [property: JsonPropertyName("totalBytesReclaimed")] long TotalBytesReclaimed,
        [property: JsonPropertyName("items")] BrowserScanItemResult[] Items
    );

    // Startup Optimizer DTOs
    public record StartupItemResult(
        [property: JsonPropertyName("id")] string Id,
        [property: JsonPropertyName("name")] string Name,
        [property: JsonPropertyName("command")] string Command,
        [property: JsonPropertyName("location")] string Location,
        [property: JsonPropertyName("isEnabled")] bool IsEnabled,
        [property: JsonPropertyName("riskLevel")] string RiskLevel
    );

    public record ServiceItemResult(
        [property: JsonPropertyName("serviceName")] string ServiceName,
        [property: JsonPropertyName("displayName")] string DisplayName,
        [property: JsonPropertyName("startMode")] string StartMode,
        [property: JsonPropertyName("status")] string Status,
        [property: JsonPropertyName("isEssential")] bool IsEssential
    );

    public record StartupOptimizerReport(
        [property: JsonPropertyName("startupItems")] StartupItemResult[] StartupItems,
        [property: JsonPropertyName("serviceItems")] ServiceItemResult[] ServiceItems
    );

    // System Telemetry DTO
    public record SystemStatsReport(
        [property: JsonPropertyName("cpuUsagePct")] int CpuUsagePct,
        [property: JsonPropertyName("ramUsagePct")] int RamUsagePct,
        [property: JsonPropertyName("ramFreeGB")] double RamFreeGB,
        [property: JsonPropertyName("ramTotalGB")] double RamTotalGB,
        [property: JsonPropertyName("diskFreeGB")] double DiskFreeGB,
        [property: JsonPropertyName("diskTotalGB")] double DiskTotalGB,
        [property: JsonPropertyName("diskUsedPct")] int DiskUsedPct,
        [property: JsonPropertyName("powerPlan")] string PowerPlan,
        [property: JsonPropertyName("hostname")] string Hostname,
        [property: JsonPropertyName("isAdmin")] bool IsAdmin
    );
}
