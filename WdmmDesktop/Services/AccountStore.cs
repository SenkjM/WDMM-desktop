using System;
using System.Collections.Generic;
using System.Text.Json;
using WdmmDesktop.Generated;
using WdmmDesktop.Models;

namespace WdmmDesktop.Services
{
    /// <summary>Cloud sources via Core <c>wdmm_account_*</c> FFI (no LocalStore fallback).</summary>
    public static class AccountStore
    {
        public static List<WebDavAccount> List()
        {
            var list = new List<WebDavAccount>();
            string raw;
            try { raw = NativeMethods.WithNativeAccountList(); }
            catch { return list; }
            if (string.IsNullOrEmpty(raw) || raw.StartsWith("error:", StringComparison.Ordinal))
                return list;
            try
            {
                using var doc = JsonDocument.Parse(raw);
                foreach (var el in doc.RootElement.EnumerateArray())
                {
                    var a = WebDavAccount.FromAccountJson(el);
                    if (a != null) list.Add(a);
                }
            }
            catch { }
            return list;
        }

        public static string Upsert(WebDavAccount a) =>
            NativeMethods.WithNativeAccountUpsert(a.ToUpsertJson());

        public static string Delete(string id) =>
            NativeMethods.WithNativeAccountDelete(id);

        /// <summary>
        /// One-shot: if Core has no accounts and legacy accounts.json exists, upsert each then rename to .bak.
        /// </summary>
        public static void MigrateFromLocalJsonIfNeeded()
        {
            try
            {
                if (List().Count > 0) return;
                var root = System.IO.Path.Combine(
                    Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Wdmm");
                var jsonPath = System.IO.Path.Combine(root, "accounts.json");
                if (!System.IO.File.Exists(jsonPath)) return;
                var raw = System.IO.File.ReadAllText(jsonPath);
                if (string.IsNullOrWhiteSpace(raw)) return;
                using var doc = JsonDocument.Parse(raw);
                if (doc.RootElement.ValueKind != JsonValueKind.Array) return;
                foreach (var el in doc.RootElement.EnumerateArray())
                {
                    // Legacy shape: id/name/url/username/password/remote_dir
                    var a = new WebDavAccount
                    {
                        Id = el.TryGetProperty("id", out var id) ? id.GetString() ?? "" : "",
                        DisplayNameField = el.TryGetProperty("name", out var n) ? n.GetString() ?? ""
                            : (el.TryGetProperty("display_name", out var dn) ? dn.GetString() ?? "" : ""),
                        Url = el.TryGetProperty("url", out var u) ? u.GetString() ?? ""
                            : (el.TryGetProperty("base_url", out var bu) ? bu.GetString() ?? "" : ""),
                        Username = el.TryGetProperty("username", out var un) ? un.GetString() ?? "" : "",
                        Password = el.TryGetProperty("password", out var pw) ? pw.GetString() ?? "" : "",
                        RemoteDir = el.TryGetProperty("remote_dir", out var rd) ? (rd.GetString() ?? "/") : "/",
                    };
                    if (string.IsNullOrWhiteSpace(a.Url)) continue;
                    try { Upsert(a); } catch { }
                }
                try
                {
                    var bak = jsonPath + ".bak";
                    if (System.IO.File.Exists(bak)) System.IO.File.Delete(bak);
                    System.IO.File.Move(jsonPath, bak);
                }
                catch { }
            }
            catch { }
        }
    }
}
