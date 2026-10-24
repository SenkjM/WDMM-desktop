using System;
using System.Text.Json.Serialization;

namespace WdmmDesktop.Models
{
    /// <summary>
    /// A WebDAV server account used by the Network page.
    ///
    /// Account list is persisted locally to %LOCALAPPDATA%\Wdmm\accounts.json because
    /// wdmm_account_list / wdmm_account_save are not yet in the FFI.
    /// TODO: replace local persistence with wdmm_account_* once exported.
    /// </summary>
    public sealed class WebDavAccount
    {
        [JsonPropertyName("id")]
        public string Id { get; set; } = Guid.NewGuid().ToString("N")[..8];

        [JsonPropertyName("name")]
        public string Name { get; set; } = "";

        [JsonPropertyName("url")]
        public string Url { get; set; } = "";

        [JsonPropertyName("username")]
        public string Username { get; set; } = "";

        /// <summary>
        /// Stored in plaintext for now.
        /// TODO: encrypt with wdmm_crypto_encrypt once a per-machine key strategy is chosen.
        /// </summary>
        [JsonPropertyName("password")]
        public string Password { get; set; } = "";

        [JsonPropertyName("remote_dir")]
        public string RemoteDir { get; set; } = "/wdmm";

        [JsonIgnore]
        public string DisplayName =>
            string.IsNullOrWhiteSpace(Name) ? Url : Name;
    }
}
