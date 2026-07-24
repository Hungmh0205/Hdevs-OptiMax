using System;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Text.Json;
using Optimax.IPC;

namespace Optimax.Core
{
    public class BrowserOptimizer
    {
        private const int SQLITE_OPEN_READWRITE = 0x00000002;

        static BrowserOptimizer()
        {
            try
            {
                NativeLibrary.SetDllImportResolver(typeof(BrowserOptimizer).Assembly, ResolveDllImport);
            }
            catch { }
        }

        private static IntPtr ResolveDllImport(string libraryName, Assembly assembly, DllImportSearchPath? searchPath)
        {
            if (libraryName.Equals("sqlite3.dll", StringComparison.OrdinalIgnoreCase) || libraryName.Equals("sqlite3", StringComparison.OrdinalIgnoreCase))
            {
                string baseDir = AppDomain.CurrentDomain.BaseDirectory;
                string localDll = Path.Combine(baseDir, "sqlite3.dll");
                if (File.Exists(localDll) && NativeLibrary.TryLoad(localDll, out IntPtr handle))
                {
                    return handle;
                }
            }
            return IntPtr.Zero;
        }

        [DllImport("sqlite3.dll", EntryPoint = "sqlite3_open_v2", CallingConvention = CallingConvention.Cdecl, CharSet = CharSet.Ansi)]
        private static extern int sqlite3_open_v2(string filename, out IntPtr db, int flags, string? zVfs);

        [DllImport("sqlite3.dll", EntryPoint = "sqlite3_exec", CallingConvention = CallingConvention.Cdecl, CharSet = CharSet.Ansi)]
        private static extern int sqlite3_exec(IntPtr db, string sql, IntPtr callback, IntPtr arg, out IntPtr errmsg);

        [DllImport("sqlite3.dll", EntryPoint = "sqlite3_close", CallingConvention = CallingConvention.Cdecl)]
        private static extern int sqlite3_close(IntPtr db);

        [DllImport("sqlite3.dll", EntryPoint = "sqlite3_free", CallingConvention = CallingConvention.Cdecl)]
        private static extern void sqlite3_free(IntPtr ptr);

        public BrowserScanReport OptimizeAllBrowsers(bool isDryRun)
        {
            var results = new List<BrowserScanItemResult>();
            long totalReclaimed = 0;

            var targets = DiscoverBrowserDatabases();

            foreach (var (browserName, dbPath) in targets)
            {
                if (!File.Exists(dbPath)) continue;

                var (isLocked, lockingApps) = SafetyEngine.GetFileLockStatus(dbPath);
                long originalSize = 0;
                try { originalSize = new FileInfo(dbPath).Length; } catch { }

                if (isLocked)
                {
                    results.Add(new BrowserScanItemResult(
                        browserName,
                        dbPath,
                        originalSize,
                        originalSize,
                        0,
                        true,
                        "Skipped (File Locked by Browser)"
                    ));
                    continue;
                }

                if (isDryRun)
                {
                    results.Add(new BrowserScanItemResult(
                        browserName,
                        dbPath,
                        originalSize,
                        originalSize,
                        0,
                        false,
                        "Dry-Run (Scanned)"
                    ));
                    continue;
                }

                long newSize = VacuumDatabase(dbPath, originalSize);
                long reclaimed = Math.Max(0, originalSize - newSize);
                totalReclaimed += reclaimed;

                // Clean orphaned WAL and journal files if safe
                CleanJournalFiles(dbPath);

                results.Add(new BrowserScanItemResult(
                    browserName,
                    dbPath,
                    originalSize,
                    newSize,
                    reclaimed,
                    false,
                    reclaimed > 0 ? $"Vacuumed ({reclaimed / 1024} KB saved)" : "Vacuumed (Already Optimized)"
                ));
            }

            return new BrowserScanReport(isDryRun, results.Count, totalReclaimed, results.ToArray());
        }

        private static List<(string BrowserName, string DbPath)> DiscoverBrowserDatabases()
        {
            var list = new List<(string, string)>();
            var visitedPaths = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

            string localAppData = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
            string appData = Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData);

            // Comprehensive Chromium targets (including Cốc Cốc, Vivaldi, Yandex, Opera GX...)
            var chromiumBases = new (string Name, string BaseDir)[]
            {
                ("Google Chrome", Path.Combine(localAppData, "Google\\Chrome\\User Data")),
                ("Microsoft Edge", Path.Combine(localAppData, "Microsoft\\Edge\\User Data")),
                ("Brave Browser", Path.Combine(localAppData, "BraveSoftware\\Brave-Browser\\User Data")),
                ("Cốc Cốc Browser", Path.Combine(localAppData, "CocCoc\\Browser\\User Data")),
                ("Vivaldi Browser", Path.Combine(localAppData, "Vivaldi\\User Data")),
                ("Yandex Browser", Path.Combine(localAppData, "Yandex\\YandexBrowser\\User Data")),
                ("Opera", Path.Combine(appData, "Opera Software\\Opera Stable")),
                ("Opera GX", Path.Combine(appData, "Opera Software\\Opera GX Stable"))
            };

            string[] dbNames = { "History", "Web Data", "Favicons", "Cookies", "QuotaManager", "Network\\Cookies" };

            foreach (var (name, baseDir) in chromiumBases)
            {
                if (!Directory.Exists(baseDir)) continue;

                try
                {
                    var profiles = new List<string> { Path.Combine(baseDir, "Default"), baseDir };
                    if (Directory.Exists(baseDir))
                    {
                        foreach (var p in Directory.GetDirectories(baseDir, "Profile *"))
                        {
                            profiles.Add(p);
                        }
                    }

                    foreach (var prof in profiles)
                    {
                        if (!Directory.Exists(prof)) continue;
                        foreach (var dbName in dbNames)
                        {
                            string path = Path.Combine(prof, dbName);
                            if (File.Exists(path) && visitedPaths.Add(path))
                            {
                                list.Add((name, path));
                            }
                        }
                    }
                }
                catch { }
            }

            // Comprehensive Gecko targets (Firefox, Waterfox, LibreWolf, Thunderbird)
            var geckoBases = new (string Name, string BaseDir)[]
            {
                ("Mozilla Firefox", Path.Combine(appData, "Mozilla\\Firefox\\Profiles")),
                ("Waterfox", Path.Combine(appData, "Waterfox\\Profiles")),
                ("LibreWolf", Path.Combine(appData, "LibreWolf\\Profiles")),
                ("Thunderbird", Path.Combine(appData, "Thunderbird\\Profiles"))
            };

            string[] ffDbs = { "places.sqlite", "cookies.sqlite", "favicons.sqlite", "webappsstore.sqlite", "formhistory.sqlite" };

            foreach (var (name, ffBase) in geckoBases)
            {
                if (!Directory.Exists(ffBase)) continue;

                try
                {
                    foreach (var prof in Directory.GetDirectories(ffBase))
                    {
                        foreach (var db in ffDbs)
                        {
                            string path = Path.Combine(prof, db);
                            if (File.Exists(path) && visitedPaths.Add(path))
                            {
                                list.Add((name, path));
                            }
                        }
                    }
                }
                catch { }
            }

            return list;
        }

        private static long VacuumDatabase(string dbPath, long originalSize)
        {
            try
            {
                int res = sqlite3_open_v2(dbPath, out IntPtr db, SQLITE_OPEN_READWRITE, null);
                if (res != 0 || db == IntPtr.Zero) return originalSize;

                try
                {
                    sqlite3_exec(db, "VACUUM;", IntPtr.Zero, IntPtr.Zero, out _);
                    sqlite3_exec(db, "REINDEX;", IntPtr.Zero, IntPtr.Zero, out _);
                }
                finally
                {
                    sqlite3_close(db);
                }

                return new FileInfo(dbPath).Length;
            }
            catch
            {
                return originalSize;
            }
        }

        private static void CleanJournalFiles(string dbPath)
        {
            string wal = dbPath + "-wal";
            string journal = dbPath + "-journal";
            string shm = dbPath + "-shm";

            foreach (var f in new[] { wal, journal, shm })
            {
                if (File.Exists(f))
                {
                    try
                    {
                        if (new FileInfo(f).Length == 0) File.Delete(f);
                    }
                    catch { }
                }
            }
        }
    }
}
