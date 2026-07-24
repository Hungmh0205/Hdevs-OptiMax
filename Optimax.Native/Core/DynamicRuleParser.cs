using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Text.Json;
using System.Text.RegularExpressions;
using Optimax.IPC;

namespace Optimax.Core
{
    public class DynamicRuleEngine
    {
        public List<DynamicCleaningRule> LoadRules(string jsonContent)
        {
            return JsonSerializer.Deserialize(jsonContent, OptimaxJsonContext.Default.ListDynamicCleaningRule) ?? new();
        }

        public bool EvaluateCondition(RuleCondition cond)
        {
            int currentBuild = Environment.OSVersion.Version.Build;
            if (currentBuild < cond.MinOsBuild || currentBuild > cond.MaxOsBuild) return false;

            if (!string.IsNullOrEmpty(cond.TargetAppExecutable))
            {
                string expandedExe = Environment.ExpandEnvironmentVariables(cond.TargetAppExecutable);
                if (!File.Exists(expandedExe)) return false;

                if (!string.IsNullOrEmpty(cond.MinProductVersion))
                {
                    var fileVer = FileVersionInfo.GetVersionInfo(expandedExe);
                    if (fileVer.ProductVersion != null &&
                        Version.TryParse(fileVer.ProductVersion, out var currentVer) &&
                        Version.TryParse(cond.MinProductVersion, out var minVer))
                    {
                        if (currentVer < minVer) return false;
                    }
                }
            }
            return true;
        }

        public List<string> ResolveMatchedFiles(DynamicCleaningRule rule)
        {
            var matchedFiles = new List<string>();
            if (!EvaluateCondition(rule.Condition)) return matchedFiles;

            var excludeRegexes = new List<Regex>();
            foreach (var pattern in rule.ExcludeRegex)
            {
                excludeRegexes.Add(new Regex(pattern, RegexOptions.IgnoreCase | RegexOptions.Compiled));
            }

            foreach (var basePath in rule.BasePaths)
            {
                string expandedBase = Environment.ExpandEnvironmentVariables(basePath);
                if (!Directory.Exists(expandedBase)) continue;

                foreach (var pattern in rule.IncludePatterns)
                {
                    try
                    {
                        var files = Directory.GetFiles(expandedBase, pattern, SearchOption.AllDirectories);
                        foreach (var file in files)
                        {
                            bool isExcluded = false;
                            foreach (var rx in excludeRegexes)
                            {
                                if (rx.IsMatch(file)) { isExcluded = true; break; }
                            }
                            if (!isExcluded) matchedFiles.Add(file);
                        }
                    }
                    catch { }
                }
            }
            return matchedFiles;
        }
    }
}
