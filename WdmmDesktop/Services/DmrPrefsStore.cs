using System;
using System.Collections.Generic;
using System.IO;
using System.Text.Json;
using WdmmDesktop.Generated;
using WdmmDesktop.Models;

namespace WdmmDesktop.Services
{
    /// <summary>
    /// USN-keyed DMR prefs via Core <c>wdmm_dmr_prefs_*</c> (independent dmr_prefs.db).
    /// One-shot migrates legacy %LOCALAPPDATA%\Wdmm\dmr_prefs.json → Core then renames to .bak.
    /// </summary>
    internal static class DmrPrefsStore
    {
        private static readonly object Gate = new();
        private static bool _inited;
        private static bool _migrated;

        private static string DbDir => Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "Wdmm");

        private static string DbPath => Path.Combine(DbDir, "dmr_prefs.db");
        private static string LegacyJson => Path.Combine(DbDir, "dmr_prefs.json");
        private static string LegacyBak => Path.Combine(DbDir, "dmr_prefs.json.bak");

        public static void EnsureInit()
        {
            lock (Gate)
            {
                if (_inited) return;
                Directory.CreateDirectory(DbDir);
                try
                {
                    var r = NativeMethods.DmrPrefsInit(DbPath);
                    if (r.StartsWith("error:", StringComparison.Ordinal))
                        System.Diagnostics.Debug.WriteLine("dmr_prefs_init: " + r);
                }
                catch (Exception ex)
                {
                    System.Diagnostics.Debug.WriteLine("dmr_prefs_init: " + ex.Message);
                }
                _inited = true;
                MigrateLegacyJsonOnce();
            }
        }

        private static void MigrateLegacyJsonOnce()
        {
            if (_migrated) return;
            _migrated = true;
            try
            {
                if (!File.Exists(LegacyJson)) return;
                if (File.Exists(LegacyBak)) return; // already migrated previously
                var map = LocalStore.LoadDmrPrefs();
                if (map.Count == 0)
                {
                    TryRenameLegacy();
                    return;
                }
                foreach (var kv in map)
                {
                    var row = new DmrPref
                    {
                        Usn = kv.Key,
                        ForceDirect = kv.Value.ForceDirect,
                        OverrideName = kv.Value.OverrideName,
                        UpdatedAt = DateTime.UtcNow.ToString("o"),
                    };
                    Upsert(row);
                }
                TryRenameLegacy();
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine("dmr migrate: " + ex.Message);
            }
        }

        private static void TryRenameLegacy()
        {
            try
            {
                if (File.Exists(LegacyJson) && !File.Exists(LegacyBak))
                    File.Move(LegacyJson, LegacyBak);
            }
            catch { /* best-effort */ }
        }

        public static Dictionary<string, DmrPref> LoadAll()
        {
            EnsureInit();
            var dict = new Dictionary<string, DmrPref>(StringComparer.Ordinal);
            try
            {
                var json = NativeMethods.DmrPrefsList();
                if (json.StartsWith("error:", StringComparison.Ordinal) || json == "null")
                    return dict;
                var rows = JsonSerializer.Deserialize<List<DmrPref>>(json) ?? new List<DmrPref>();
                foreach (var r in rows)
                {
                    if (!string.IsNullOrEmpty(r.Usn))
                        dict[r.Usn] = r;
                }
            }
            catch { }
            return dict;
        }

        public static DmrPref? Get(string usn)
        {
            EnsureInit();
            if (string.IsNullOrEmpty(usn)) return null;
            try
            {
                var json = NativeMethods.DmrPrefsGet(usn);
                if (json == "null" || json.StartsWith("error:", StringComparison.Ordinal))
                    return null;
                return JsonSerializer.Deserialize<DmrPref>(json);
            }
            catch { return null; }
        }

        public static string Upsert(DmrPref pref)
        {
            EnsureInit();
            if (string.IsNullOrWhiteSpace(pref.Usn))
                return "error:missing usn";
            if (string.IsNullOrEmpty(pref.UpdatedAt))
                pref.UpdatedAt = DateTime.UtcNow.ToString("o");
            try
            {
                var json = JsonSerializer.Serialize(pref);
                return NativeMethods.DmrPrefsUpsert(json);
            }
            catch (Exception ex) { return "error:" + ex.Message; }
        }

        public static string Delete(string usn)
        {
            EnsureInit();
            try { return NativeMethods.DmrPrefsDelete(usn); }
            catch (Exception ex) { return "error:" + ex.Message; }
        }
    }
}
