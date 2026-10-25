using System.Text.Json.Serialization;

namespace WdmmDesktop.Models
{
    /// <summary>A discovered DLNA / UPnP MediaRenderer, as returned by wdmm_dlna_discover_json.</summary>
    public sealed class DmrDevice
    {
        [JsonPropertyName("usn")]              public string Usn { get; set; } = "";
        [JsonPropertyName("friendly_name")]    public string FriendlyName { get; set; } = "(unnamed)";
        [JsonPropertyName("location")]         public string Location { get; set; } = "";
        [JsonPropertyName("av_transport_url")] public string AvTransportUrl { get; set; } = "";

        /// <summary>Display label: override_name from Core prefs if set, else discovery name.</summary>
        [JsonIgnore]
        public string DisplayName { get; set; } = "";
    }

    /// <summary>
    /// Per-DMR rendering preferences, keyed by USN.
    /// Source of truth: Core independent <c>dmr_prefs.db</c> via <c>wdmm_dmr_prefs_*</c>.
    ///
    /// Locked cast rules (mirrors Android):
    ///   • DLNA cast always goes through the local proxy, never a direct file URL.
    ///   • Only one file is proxied at a time (single-file mutex).
    ///   • No automatic HLS ↔ direct fallback. ForceDirect is honoured verbatim.
    ///   • HLS mode (ForceDirect = false) uses wdmm_proxy_register_hls with device_id = Usn.
    /// </summary>
    public sealed class DmrPref
    {
        [JsonPropertyName("usn")]
        public string Usn { get; set; } = "";

        /// <summary>
        /// true  = proxy URL for direct streaming (ProxyRegisterFile).
        /// false = HLS transcode via wdmm_proxy_register_hls.
        /// Default = true (direct, universally compatible).
        /// </summary>
        [JsonPropertyName("force_direct")]
        public bool ForceDirect { get; set; } = true;

        /// <summary>User override for friendly name; null/empty = use discovery name.</summary>
        [JsonPropertyName("override_name")]
        [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
        public string? OverrideName { get; set; }

        [JsonPropertyName("updated_at")]
        public string UpdatedAt { get; set; } = "";
    }
}
