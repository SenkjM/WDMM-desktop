using System.Text.Json.Serialization;

namespace WdmmDesktop.Models
{
    public sealed class TrackItem
    {
        [JsonPropertyName("id")]            public string Id { get; set; } = "";
        [JsonPropertyName("title")]         public string Title { get; set; } = "(unknown)";
        [JsonPropertyName("artist")]        public string Artist { get; set; } = "";
        [JsonPropertyName("album")]         public string Album { get; set; } = "";
        [JsonPropertyName("path")]          public string Path { get; set; } = "";
        [JsonPropertyName("duration_secs")] public double DurationSecs { get; set; }
        /// <summary>Core cover_state: 0 unknown / 1 none / 2 present. Not a path.</summary>
        [JsonPropertyName("cover_state")]  public int CoverStateValue { get; set; }
        [JsonIgnore] public CoverState CoverState =>
            CoverStateValue switch { 1 => CoverState.None, 2 => CoverState.Present, _ => CoverState.Unknown };

        /// <summary>Formatted MM:SS duration, or "" if unknown.</summary>
        [JsonIgnore]
        public string DurationDisplay => DurationSecs > 0
            ? $"{(int)(DurationSecs / 60)}:{(int)(DurationSecs % 60):D2}"
            : "";

        /// <summary>"Artist — Album", "Artist", "Album", or "" depending on what is populated.</summary>
        [JsonIgnore]
        public string SubLine =>
            !string.IsNullOrEmpty(Artist) && !string.IsNullOrEmpty(Album) ? $"{Artist} — {Album}" :
            !string.IsNullOrEmpty(Artist) ? Artist :
            !string.IsNullOrEmpty(Album)  ? Album  : "";
    }
}
