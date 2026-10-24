using System;
using System.Collections.Generic;
using System.IO;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace WdmmDesktop
{
    /// <summary>
    /// Local JSON persistence for app data that the FFI does not yet manage
    /// (WebDAV accounts, per-USN DMR preferences). All files live under
    /// %LOCALAPPDATA%\Wdmm. Used only while wdmm_account_* / wdmm_dmr_pref_*
    /// are not exported — see the TODOs on the relevant model classes.
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

        /// <summary>Load (or default) a JSON file, creating the root dir on first use.</summary>
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

        // Convenience wrappers for the two known files — keeps call sites tidy.

        public static List<Models.WebDavAccount> LoadAccounts() =>
            Load("accounts.json", new List<Models.WebDavAccount>());

        public static void SaveAccounts(List<Models.WebDavAccount> accounts) =>
            Save("accounts.json", accounts);

        public static Dictionary<string, Models.DmrPref> LoadDmrPrefs() =>
            Load("dmr_prefs.json", new Dictionary<string, Models.DmrPref>());

        public static void SaveDmrPrefs(Dictionary<string, Models.DmrPref> prefs) =>
            Save("dmr_prefs.json", prefs);
    }
}
