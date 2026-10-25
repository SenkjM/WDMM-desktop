using System;
using System.Collections.Generic;
using System.Linq;
using Newtonsoft.Json;
using WdmmDesktop.Generated;
using WdmmDesktop.Models;

namespace WdmmDesktop.Services
{
    /// <summary>
    /// Public FFI facade for driver and account operations.
    /// Wraps <see cref="NativeMethods"/> driver/account bindings, returns parsed lists.
    /// </summary>
    public static class CloudFfi
    {
        // ── Driver-level operations ───────────────────────────────────────────

        /// <summary>
        /// List directory entries using driver-level config (no saved account).
        /// </summary>
        /// <param name="providerType">e.g. "webdav", "aliyundrive", "115"</param>
        /// <param name="configJson">JSON object with provider-specific config</param>
        /// <param name="path">Remote path to list</param>
        /// <returns>Parsed list or throws on error</returns>
        public static List<CloudFileItem> DriverList(string providerType, string configJson, string path)
        {
            var json = NativeMethods.DriverList(providerType, configJson, path);
            if (json.StartsWith("error:"))
                throw new Exception(json.Substring(6));
            return JsonConvert.DeserializeObject<List<CloudFileItem>>(json) ?? new List<CloudFileItem>();
        }

        public static void DriverMkdir(string providerType, string configJson, string path)
        {
            var result = NativeMethods.DriverMkdir(providerType, configJson, path);
            if (result.StartsWith("error:"))
                throw new Exception(result.Substring(6));
        }

        public static void DriverRename(string providerType, string configJson, string oldPath, string newPath)
        {
            var result = NativeMethods.DriverRename(providerType, configJson, oldPath, newPath);
            if (result.StartsWith("error:"))
                throw new Exception(result.Substring(6));
        }

        public static void DriverRemove(string providerType, string configJson, string path)
        {
            var result = NativeMethods.DriverRemove(providerType, configJson, path);
            if (result.StartsWith("error:"))
                throw new Exception(result.Substring(6));
        }

        /// <summary>
        /// Get metadata for a single entry.
        /// </summary>
        /// <returns>CloudFileItem or null if not found</returns>
        public static CloudFileItem DriverGet(string providerType, string configJson, string path)
        {
            var json = NativeMethods.DriverGet(providerType, configJson, path);
            if (json.StartsWith("error:"))
                throw new Exception(json.Substring(6));
            if (json == "null")
                return null;
            return JsonConvert.DeserializeObject<CloudFileItem>(json);
        }

        // ── Account-based operations ──────────────────────────────────────────

        /// <summary>
        /// List directory entries for a saved account.
        /// </summary>
        /// <param name="accountId">DB-stored account UUID</param>
        /// <param name="path">Remote path to list</param>
        /// <returns>Parsed list or throws on error</returns>
        public static List<CloudFileItem> AccountListDir(string accountId, string path)
        {
            var json = NativeMethods.AccountListDir(accountId, path);
            if (json.StartsWith("error:"))
                throw new Exception(json.Substring(6));
            return JsonConvert.DeserializeObject<List<CloudFileItem>>(json) ?? new List<CloudFileItem>();
        }

        public static void AccountMkdir(string accountId, string path)
        {
            var result = NativeMethods.AccountMkdir(accountId, path);
            if (result.StartsWith("error:"))
                throw new Exception(result.Substring(6));
        }

        public static void AccountRename(string accountId, string oldPath, string newPath)
        {
            var result = NativeMethods.AccountRename(accountId, oldPath, newPath);
            if (result.StartsWith("error:"))
                throw new Exception(result.Substring(6));
        }

        public static void AccountRemove(string accountId, string path)
        {
            var result = NativeMethods.AccountRemove(accountId, path);
            if (result.StartsWith("error:"))
                throw new Exception(result.Substring(6));
        }

        /// <summary>
        /// Get metadata for a single entry in the account's directory.
        /// </summary>
        /// <returns>CloudFileItem or null if not found</returns>
        public static CloudFileItem AccountGetEntry(string accountId, string path)
        {
            var json = NativeMethods.AccountGetEntry(accountId, path);
            if (json.StartsWith("error:"))
                throw new Exception(json.Substring(6));
            if (json == "null")
                return null;
            return JsonConvert.DeserializeObject<CloudFileItem>(json);
        }
    }
}
