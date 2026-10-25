using System;
using System.Collections.Generic;
using System.IO;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace WdmmDesktop
{
    /// <summary>
    /// Local JSON for Desktop-only prefs (desktop_prefs).
    /// Cloud sources: AccountStore / wdmm_account_*. DMR prefs: DmrPrefsStore / wdmm_dmr_prefs_*
    /// (LoadDmrPrefs retained only for one-shot migrate from legacy dmr_prefs.json).
    /// </summary>
    internal static class LocalStore
    {
        private static readonly string Root = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "Wdmm");

        private static readonly JsonSerializerOptions JsonOpts = new()
        {
            WriteIndented = true,
            DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull
        };

        private static string PathFor(string file) =>
            System.IO.Path.Combine(Root, file);

        public static T Load<T>(string file, T defaultValue) where T : class
        {
            try
            {
                Directory.CreateDirectory(Root);
                var p = PathFor(file);
                if (!File.Exists(p)) return defaultValue;
                var json = File.ReadAllText(p);
                return JsonSerializer.Deserialize<T>(json, JsonOpts) ?? defaultValue;
            }
            catch
            {
                return defaultValue;
            }
        }

        public static void Save<T>(string file, T value) where T : class
        {
            try
            {
                Directory.CreateDirectory(Root);
                File.WriteAllText(PathFor(file), JsonSerializer.Serialize(value, JsonOpts));
            }
            catch
            {
                // Persistence is best-effort; never crash the UI on a disk error.
            }
        }

        /// <summary>Legacy read for migrate-only. New writes go through DmrPrefsStore.</summary>
        public static Dictionary<string, Models.DmrPref> LoadDmrPrefs() =>
            Load("dmr_prefs.json", new Dictionary<string, Models.DmrPref>());
    }
}
