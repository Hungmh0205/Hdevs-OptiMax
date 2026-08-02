using System;
using System.IO;
using System.Threading;

namespace Optimax.Core
{
    /// <summary>
    /// Lightweight, AOT-compatible centralized logging engine with Correlation ID support.
    /// Writes structured logs to %ProgramData%\Optimax\Logs\optimax_yyyy-MM-dd.log.
    /// Thread-safe via file-level locking. Logger itself never throws.
    /// </summary>
    public static class OptimaxLogger
    {
        private static readonly string LogDir = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData),
            "Optimax", "Logs");

        private static readonly object _lock = new();
        private static bool _initialized;
        private static readonly AsyncLocal<string?> _correlationId = new();

        public static string? CurrentCorrelationId => _correlationId.Value;

        public static void SetCorrelationId(string? id)
        {
            _correlationId.Value = id;
        }

        public static string EnsureCorrelationId()
        {
            if (string.IsNullOrEmpty(_correlationId.Value))
            {
                _correlationId.Value = Guid.NewGuid().ToString("N").Substring(0, 8);
            }
            return _correlationId.Value;
        }

        public static void ClearCorrelationId()
        {
            _correlationId.Value = null;
        }

        private static void EnsureDirectory()
        {
            if (_initialized) return;
            try { Directory.CreateDirectory(LogDir); } catch { }
            _initialized = true;
        }

        /// <summary>
        /// Log low-priority diagnostic information for expected/frequent failures.
        /// </summary>
        public static void Trace(string context, Exception? ex = null)
        {
            WriteLog("TRACE", context, ex);
        }

        /// <summary>
        /// Log meaningful operational warnings that help with production debugging.
        /// </summary>
        public static void Warn(string context, Exception? ex = null)
        {
            WriteLog("WARN", context, ex);
        }

        /// <summary>
        /// Log critical errors that indicate a broken feature or data corruption risk.
        /// </summary>
        public static void Error(string context, Exception? ex = null)
        {
            WriteLog("ERROR", context, ex);
        }

        private static void WriteLog(string level, string context, Exception? ex)
        {
            try
            {
                EnsureDirectory();
                string logFile = Path.Combine(LogDir, $"optimax_{DateTime.Now:yyyy-MM-dd}.log");
                string timestamp = DateTime.Now.ToString("HH:mm:ss.fff");
                string cid = _correlationId.Value ?? "-";
                string line = $"[{timestamp}] [{level}] [CID:{cid}] {context}";
                if (ex != null)
                {
                    line += $" | {ex.GetType().Name}: {ex.Message}";
                }

                lock (_lock)
                {
                    File.AppendAllText(logFile, line + Environment.NewLine);
                }
            }
            catch
            {
                // Logger itself must NEVER throw — this is the only acceptable empty catch in the codebase
            }
        }
    }
}
