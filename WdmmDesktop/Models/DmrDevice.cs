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
    ///     unregistered before registering a new one).
    ///   • No automatic HLS ↔ direct fallback. The user's ForceDirect choice is
    ///     honoured verbatim; the app never silently switches mode.
    ///   • HLS mode (ForceDirect = false) requires wdmm_proxy_start_hls from
    ///     wdmm-core feat/proxy-hls-ffmpeg — see NativeMethods for the TODO stub.
    /// </summary>
    public sealed class DmrPref
    {
        /// <summary>
        /// true  = send the proxy URL for direct streaming (no transcode).
        /// false = HLS transcode via wdmm_proxy_start_hls (blocked until
        ///         feat/proxy-hls-ffmpeg merges — Cast UI enforces this guard).
        /// Default = true (direct, universally compatible).
        /// </summary>
        [JsonPropertyName("force_direct")]
        public bool ForceDirect { get; set; } = true;
    }
}
