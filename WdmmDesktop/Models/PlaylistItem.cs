using System;
using System.Text.Json.Serialization;

namespace WdmmDesktop.Models
{
    /// <summary>
    /// A playlist row returned by GET /v1/playlists (wrapped in {"playlists":[…]})
    /// or GET /v1/playlists/:id.
    ///
    /// JSON shape matches wdmm-storage::Playlist (id, name, created_at, updated_at).
    ///
    /// Long-term: replace OpenAPI HTTP call with wdmm_playlist_list FFI once
    /// wdmm-core exports it (not present at submodule d8b2217).
    /// </summary>
    public sealed class PlaylistItem
    {
        [JsonPropertyName("id")]         public string Id         { get; set; } = "";
        [JsonPropertyName("name")]       public string Name       { get; set; } = "(unnamed)";
        [JsonPropertyName("created_at")] public string CreatedAt  { get; set; } = "";
        [JsonPropertyName("updated_at")] public string UpdatedAt  { get; set; } = "";

        [JsonIgnore]
        public string DisplaySub =>
            string.IsNullOrEmpty(CreatedAt)
                ? Id
                : $"created {CreatedAt[..Math.Min(10, CreatedAt.Length)]}";
    }

    /// <summary>
    /// One row inside GET /v1/playlists/:id/entries (wrapped in {"entries":[…]}).
    ///
    /// JSON shape matches wdmm-storage::PlaylistEntry
    /// (playlist_id, position, music_id, source_name, remote_path, title, duration_ms).
    /// </summary>
    public sealed class PlaylistEntryItem
    {
        [JsonPropertyName("playlist_id")]  public string PlaylistId  { get; set; } = "";
        [JsonPropertyName("position")]     public int    Position    { get; set; }
        [JsonPropertyName("music_id")]     public string MusicId     { get; set; } = "";
        [JsonPropertyName("source_name")]  public string SourceName  { get; set; } = "";
        [JsonPropertyName("remote_path")]  public string RemotePath  { get; set; } = "";
        [JsonPropertyName("title")]        public string Title       { get; set; } = "(unknown)";
        [JsonPropertyName("duration_ms")]  public long   DurationMs  { get; set; }

        [JsonIgnore]
        public string DurationDisplay => DurationMs > 0
            ? $"{DurationMs / 60_000}:{(DurationMs / 1000 % 60):D2}"
            : "";

        [JsonIgnore]
        public string SubLine => string.IsNullOrEmpty(SourceName)
            ? RemotePath
            : $"{SourceName} · {RemotePath}";
    }
}
