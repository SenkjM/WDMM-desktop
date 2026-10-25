using WdmmDesktop.Models;

namespace WdmmDesktop
{
    /// <summary>
    /// Lightweight cross-page state container.
    /// PendingCastTrack is retained for callers that still set it before opening
    /// Dialogs/CastDialog; MusicPage now passes the track to CastDialog directly.
    /// </summary>
    internal static class AppState
    {
        /// <summary>
        /// Optional track queued for casting (legacy hand-off). Prefer passing
        /// the track into CastDialog's constructor.
        /// </summary>
        public static TrackItem? PendingCastTrack { get; set; }
    }
}
