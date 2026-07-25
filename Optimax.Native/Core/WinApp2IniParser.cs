using System;
using System.Collections.Generic;
using System.IO;
using System.Text.RegularExpressions;
using Optimax.IPC;

namespace Optimax.Core
{
    public class WinApp2IniParser
    {
        public List<DynamicCleaningRule> ParseIniFile(string iniFilePath)
        {
            var rules = new List<DynamicCleaningRule>();
            if (!File.Exists(iniFilePath)) return rules;

            try
            {
                using var stream = new FileStream(iniFilePath, FileMode.Open, FileAccess.Read, FileShare.Read, bufferSize: 65536);
                using var reader = new StreamReader(stream);

                string? currentSection = null;
                string? detectFile = null;
                string? detectExe = null;
                var fileKeys = new List<string>(8);
                var excludeKeys = new List<string>(4);
                int ruleCounter = 0;

                string? rawLine;
                while ((rawLine = reader.ReadLine()) != null)
                {
                    ReadOnlySpan<char> span = rawLine.AsSpan().Trim();
                    if (span.IsEmpty || span.StartsWith(";".AsSpan())) continue;

                    if (span.StartsWith("[".AsSpan()) && span.EndsWith("]".AsSpan()))
                    {
                        // Save previous section if valid and app installed
                        if (!string.IsNullOrEmpty(currentSection) && fileKeys.Count > 0)
                        {
                            if (IsAppInstalled(detectFile, detectExe))
                            {
                                var rule = ConvertToRule(ruleCounter++, currentSection, detectFile, detectExe, fileKeys, excludeKeys);
                                if (rule != null) rules.Add(rule);
                            }
                        }

                        currentSection = span.Slice(1, span.Length - 2).Trim().ToString();
                        detectFile = null;
                        detectExe = null;
                        fileKeys.Clear();
                        excludeKeys.Clear();
                        continue;
                    }

                    int eqIdx = span.IndexOf('=');
                    if (eqIdx <= 0) continue;

                    ReadOnlySpan<char> keySpan = span.Slice(0, eqIdx).Trim();
                    ReadOnlySpan<char> valSpan = span.Slice(eqIdx + 1).Trim();

                    if (keySpan.Equals("DetectFile".AsSpan(), StringComparison.OrdinalIgnoreCase))
                    {
                        detectFile = valSpan.ToString();
                    }
                    else if (keySpan.Equals("Detect".AsSpan(), StringComparison.OrdinalIgnoreCase))
                    {
                        detectExe = valSpan.ToString();
                    }
                    else if (keySpan.StartsWith("FileKey".AsSpan(), StringComparison.OrdinalIgnoreCase))
                    {
                        fileKeys.Add(valSpan.ToString());
                    }
                    else if (keySpan.StartsWith("ExcludeKey".AsSpan(), StringComparison.OrdinalIgnoreCase))
                    {
                        excludeKeys.Add(valSpan.ToString());
                    }
                }

                // Save last section
                if (!string.IsNullOrEmpty(currentSection) && fileKeys.Count > 0 && IsAppInstalled(detectFile, detectExe))
                {
                    var rule = ConvertToRule(ruleCounter++, currentSection, detectFile, detectExe, fileKeys, excludeKeys);
                    if (rule != null) rules.Add(rule);
                }
            }
            catch
            {
                return rules;
            }

            return rules;
        }

        private static bool IsAppInstalled(string? detectFile, string? detectExe)
        {
            if (string.IsNullOrEmpty(detectFile) && string.IsNullOrEmpty(detectExe)) return true;

            try
            {
                if (!string.IsNullOrEmpty(detectFile))
                {
                    string expanded = Environment.ExpandEnvironmentVariables(detectFile);
                    if (File.Exists(expanded) || Directory.Exists(expanded)) return true;
                }
            }
            catch { }

            return false;
        }

        private static DynamicCleaningRule? ConvertToRule(
            int id,
            string sectionName,
            string? detectFile,
            string? detectExe,
            List<string> fileKeys,
            List<string> excludeKeys)
        {
            var rule = new DynamicCleaningRule
            {
                RuleId = $"WINAPP2_{id:D4}",
                Name = sectionName,
                Condition = new RuleCondition
                {
                    TargetAppExecutable = detectExe ?? detectFile
                }
            };

            foreach (var fk in fileKeys)
            {
                string[] parts = fk.Split('|', StringSplitOptions.RemoveEmptyEntries);
                if (parts.Length == 0) continue;

                string rawPath = parts[0].Trim();
                string pattern = parts.Length > 1 ? parts[1].Trim() : "*.*";

                if (string.IsNullOrEmpty(pattern) || pattern.Contains('|')) pattern = "*.*";

                string normalizedPath = NormalizePath(rawPath);
                rule.FileKeys.Add(new FileKeyEntry(normalizedPath, pattern));
                rule.BasePaths.Add(normalizedPath);
                rule.IncludePatterns.Add(pattern);
            }

            foreach (var ex in excludeKeys)
            {
                int pipeIdx = ex.IndexOf('|');
                string rawPath = pipeIdx >= 0 ? ex.Substring(0, pipeIdx).Trim() : ex.Trim();
                if (!string.IsNullOrEmpty(rawPath))
                {
                    string rx = Regex.Escape(NormalizePath(rawPath)).Replace("\\*", ".*");
                    rule.ExcludeRegex.Add(rx);
                }
            }

            return rule.FileKeys.Count > 0 || rule.BasePaths.Count > 0 ? rule : null;
        }

        private static string NormalizePath(string rawPath)
        {
            string localAppData = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
            string appData = Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData);
            string winDir = Environment.GetFolderPath(Environment.SpecialFolder.Windows);
            if (string.IsNullOrEmpty(winDir)) winDir = "C:\\Windows";
            string systemDrive = Path.GetPathRoot(winDir) ?? "C:\\";
            string commonAppData = Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData);

            return rawPath
                .Replace("%WinDir%", winDir, StringComparison.OrdinalIgnoreCase)
                .Replace("%SystemRoot%", winDir, StringComparison.OrdinalIgnoreCase)
                .Replace("%SystemDrive%", systemDrive.TrimEnd('\\'), StringComparison.OrdinalIgnoreCase)
                .Replace("%ProgramFiles%", Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles), StringComparison.OrdinalIgnoreCase)
                .Replace("%AppData%", appData, StringComparison.OrdinalIgnoreCase)
                .Replace("%LocalAppData%", localAppData, StringComparison.OrdinalIgnoreCase)
                .Replace("%LocalAppdata%", localAppData, StringComparison.OrdinalIgnoreCase)
                .Replace("%CommonAppData%", commonAppData, StringComparison.OrdinalIgnoreCase)
                .Replace("%Chrome%", Path.Combine(localAppData, "Google\\Chrome\\User Data"), StringComparison.OrdinalIgnoreCase)
                .Replace("%Firefox%", Path.Combine(appData, "Mozilla\\Firefox\\Profiles"), StringComparison.OrdinalIgnoreCase)
                .Replace("%Thunderbird%", Path.Combine(appData, "Thunderbird\\Profiles"), StringComparison.OrdinalIgnoreCase)
                .Replace("%Opera%", Path.Combine(appData, "Opera Software"), StringComparison.OrdinalIgnoreCase)
                .Replace("%Profiles%", Path.Combine(appData, "Mozilla\\Firefox\\Profiles"), StringComparison.OrdinalIgnoreCase);
        }
    }
}
