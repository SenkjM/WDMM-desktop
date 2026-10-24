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
    }

    /// <summary>
    /// Per-DMR rendering preferences, keyed by USN.
    /// Persisted to %LOCALAPPDATA%\Wdmm\dmr_prefs.json.
    ///
    /// Locked cast rules (mirrors Android):
    ///   • DLNA cast always goes through the local proxy, never a direct file URL.
    ///   • Only one file is proxied at a time (single-file mutex; previous token is
    ///     unregistered before registering a new one — HLS tokens use
    ///     ProxyUnregisterHls, file tokens use ProxyUnregister).
    ///   • No automatic HLS ↔ direct fallback. The user's ForceDirect choice is
    ///     honoured verbatim; the app never silently switches mode.
    ///   • HLS mode (ForceDirect = false) uses wdmm_proxy_register_hls with
    ///     device_id set to the renderer's Usn (available since wdmm-core e236ece).
    /// </summary>
    public sealed class DmrPref
    {
        /// <summary>
        /// true  = send the proxy URL for direct streaming (ProxyRegisterFile, no transcode).
        /// false = HLS transcode via wdmm_proxy_register_hls (wdmm-core e236ece).
        /// Default = true (direct, universally compatible).
        /// </summary>
        [JsonPropertyName("force_direct")]
        public bool ForceDirect { get; set; } = true;
    }
}
