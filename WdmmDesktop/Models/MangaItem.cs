using System.Text.Json.Serialization;

namespace WdmmDesktop.Models
{
    /// <summary>
    /// View-model for a manga entry returned by wdmm_manga_list.
    /// Field names match the Rust Manga struct (snake_case).
    /// </summary>
    public sealed class MangaItem
    {
        [JsonPropertyName("id")]             public string Id { get; set; } = "";
        [JsonPropertyName("title")]          public string Title { get; set; } = "(unknown)";
        [JsonPropertyName("author")]         public string? Author { get; set; }
        [JsonPropertyName("series")]         public string? Series { get; set; }
        [JsonPropertyName("volume")]         public string? Volume { get; set; }
        [JsonPropertyName("chapter_number")] public string? ChapterNumber { get; set; }
        [JsonPropertyName("page_count")]     public long PageCount { get; set; }
        /// <summary>Core cover_state: 0 unknown / 1 none / 2 present. Not a path.</summary>
        [JsonPropertyName("cover_state")]   public int CoverStateValue { get; set; }
        [JsonIgnore] public CoverState CoverState =>
            CoverStateValue switch { 1 => CoverState.None, 2 => CoverState.Present, _ => CoverState.Unknown };
        [JsonPropertyName("format")]         public string Format { get; set; } = "";
        [JsonPropertyName("source_name")]    public string SourceName { get; set; } = "";
        [JsonPropertyName("remote_path")]    public string RemotePath { get; set; } = "";
        [JsonPropertyName("remote_missing")] public long RemoteMissing { get; set; }
        [JsonPropertyName("updated_at")]     public string UpdatedAt { get; set; } = "";

        /// <summary>Set by MangaPage after merging progress data. -1 = no progress.</summary>
        [JsonIgnore] public long CurrentPage { get; set; } = -1;

        /// <summary>Series tag, volume/chapter, or format hint — second line under the title.</summary>
        [JsonIgnore]
        public string SubLine
        {
            get
            {
                var parts = new System.Collections.Generic.List<string>();
                if (!string.IsNullOrEmpty(Series))         parts.Add(Series);
                if (!string.IsNullOrEmpty(Volume))         parts.Add($"Vol.{Volume}");
                if (!string.IsNullOrEmpty(ChapterNumber))  parts.Add($"Ch.{ChapterNumber}");
                if (parts.Count == 0 && !string.IsNullOrEmpty(Format)) parts.Add(Format.ToUpperInvariant());
                if (PageCount > 0) parts.Add($"{PageCount}p");
                return string.Join(" · ", parts);
            }
        }

        /// <summary>Reading progress hint: "p3/50", or "" if not started.</summary>
        [JsonIgnore]
        public string ProgressHint => CurrentPage >= 0
            ? $"p{CurrentPage + 1}/{(PageCount > 0 ? PageCount.ToString() : "?")}"
            : "";
    }
}
