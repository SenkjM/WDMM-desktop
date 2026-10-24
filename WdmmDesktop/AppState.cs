using WdmmDesktop.Models;

namespace WdmmDesktop
{
    /// <summary>
    /// Lightweight cross-page state container.
    /// Holding PendingCastTrack here avoids threading navigation parameters through
    /// the Frame just to pass a single selected track from MusicPage to CastPage.
    /// </summary>
    internal static class AppState
    {
        /// <summary>
        /// Track selected for casting from MusicPage (or null if none queued).
        /// CastPage consumes and clears it on first load.
        /// </summary>
        public static TrackItem? PendingCastTrack { get; set; }
    }
}
