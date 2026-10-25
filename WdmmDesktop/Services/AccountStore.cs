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
    }
}
