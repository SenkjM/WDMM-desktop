namespace WdmmDesktop.Models
{
    /// <summary>
    /// Mirrors <c>ContentDeliveryKind</c> from
    /// crates/wdmm-drivers/src/driver.rs (wdmm-core e236ece).
    ///
    /// Describes how bytes for a library/cloud item are delivered.
    /// Cast and playback decisions are based on this field — not solely on
    /// whether raw_url is null.
    /// </summary>
    public enum ContentDeliveryKind
    {
        /// <summary>
        /// A direct network URL (+ optional request headers) is available.
        /// Typical: most cloud drives with public/signed download links.
        ///
        /// Desktop cast rule: DLNA must still go through the local proxy
        /// (locked rule 1). A NetworkDirect source with no local file cannot
        /// be proxied via register_file or register_hls — cast refuses with an
        /// explicit error. Future path: materialize to local cache → LocalFile,
        /// or use ProxyRegisterStream with a fetch callback (not supported yet).
        /// </summary>
        NetworkDirect,

        /// <summary>
        /// Bytes come from an in-process Range callback only (no URL, no path).
        /// Typical: crypt-wrapper drivers, streaming-only protocols.
        ///
        /// Cast must use <see cref="NativeMethods.ProxyRegisterStream"/>. Desktop
        /// has no cloud/crypt drivers today — the hook is present in CastPage but
        /// returns NotSupported until a real stream source supplies a
        /// <see cref="NativeMethods.WdmmProxyRangeCallback"/>.
        /// </summary>
        LocalStream,

        /// <summary>
        /// A local filesystem path is available and readable.
        /// ForceDirect = true  → <see cref="NativeMethods.ProxyRegisterFile"/> (direct-stream).
        /// ForceDirect = false → <see cref="NativeMethods.ProxyRegisterHls"/>  (HLS transcode).
        ///
        /// This is today's only real desktop cast path — SourceBox accepts local paths.
        /// </summary>
        LocalFile,
    }

    /// <summary>
    /// Static helpers mirroring Core <c>ContentDeliveryKind::infer</c>.
    /// </summary>
    public static class ContentDeliveryKindHelper
    {
        /// <summary>
        /// Infer the delivery kind from the presence of a local path or raw URL.
        /// Priority: LocalFile > NetworkDirect > LocalStream.
        /// Matches Core infer() logic (crates/wdmm-drivers/src/driver.rs @ e236ece).
        /// </summary>
        public static ContentDeliveryKind Infer(string? localPath, string? rawUrl)
        {
            if (!string.IsNullOrEmpty(localPath)) return ContentDeliveryKind.LocalFile;
            if (!string.IsNullOrEmpty(rawUrl))    return ContentDeliveryKind.NetworkDirect;
            return ContentDeliveryKind.LocalStream;
        }
    }
}
