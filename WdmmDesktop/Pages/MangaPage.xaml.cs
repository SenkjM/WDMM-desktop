using System;
using System.Collections.Generic;
using System.Text.Json;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Navigation;
using WdmmDesktop.Generated;
using WdmmDesktop.Models;

namespace WdmmDesktop.Pages
{
    // MangaPage — wired to wdmm_manga_* FFI (Wave D, fdb05df).
    //
    // On navigate: wdmm_manga_list() fetches all manga entries; progress is merged
    // in from wdmm_manga_list_progress().  Clicking a card opens a detail dialog
    // that calls wdmm_manga_get + wdmm_manga_list_pages for richer info.
    //
    // Library must be initialised by wdmm_library_init before any call lands.
    // On a Linux dev box the DLL is absent — FFI calls are silently tolerated and
    // the page shows the empty state with an explanatory hint.
    public sealed partial class MangaPage : Page
    {
        private List<MangaItem> _items = new();
        private bool _libraryInitAttempted;

        public MangaPage() => InitializeComponent();

        protected override void OnNavigatedTo(NavigationEventArgs e)
        {
            base.OnNavigatedTo(e);
            EnsureLibraryInit();
            LoadMangas();
        }

        // ── Library init ──────────────────────────────────────────────────────

        private void EnsureLibraryInit()
        {
            if (_libraryInitAttempted) return;
            _libraryInitAttempted = true;
            try
            {
                var dbDir = System.IO.Path.Combine(
                    Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                    "Wdmm");
                System.IO.Directory.CreateDirectory(dbDir);
                NativeMethods.LibraryInit(System.IO.Path.Combine(dbDir, "library.db"));
            }
            catch { /* FFI not loaded on Linux dev box — tolerate */ }
        }

        // ── Data loading ──────────────────────────────────────────────────────

        private void LoadMangas()
        {
            _items = new List<MangaItem>();
            string? errorHint = null;

            try
            {
                var json = NativeMethods.MangaList();
                if (json.StartsWith("error:", StringComparison.Ordinal))
                {
                    errorHint = json;
                }
                else
                {
                    _items = JsonSerializer.Deserialize<List<MangaItem>>(json)
                             ?? new List<MangaItem>();
                }
            }
            catch (Exception ex)
            {
                // FFI not loaded (Linux dev) or library not yet initialised.
                errorHint = ex.Message;
            }

            // Merge reading progress (best-effort — ignore on any failure).
            try
            {
                var progressJson = NativeMethods.MangaListProgress();
                if (!progressJson.StartsWith("error:", StringComparison.Ordinal))
                {
                    var progList = JsonSerializer.Deserialize<List<MangaProgressRow>>(progressJson);
                    if (progList != null)
                    {
                        var progMap = new Dictionary<string, long>();
                        foreach (var p in progList)
                            progMap[p.MangaId] = p.PageIndex;

                        foreach (var item in _items)
                        {
                            if (progMap.TryGetValue(item.Id, out var page))
                                item.CurrentPage = page;
                        }
                    }
                }
            }
            catch { /* progress merge is non-critical */ }

            ApplyList(errorHint);
        }

        private void ApplyList(string? errorHint)
        {
            SeriesGrid.ItemsSource = null;

            if (_items.Count == 0)
            {
                SeriesGrid.Visibility  = Visibility.Collapsed;
                EmptyState.Visibility  = Visibility.Visible;
                EmptyText.Text = errorHint != null
                    ? $"Could not load manga library: {errorHint}"
                    : "Manga library is empty. Add entries via sync or import.";
                StatusBar.Text = "";
            }
            else
            {
                SeriesGrid.Visibility  = Visibility.Visible;
                EmptyState.Visibility  = Visibility.Collapsed;
                SeriesGrid.ItemsSource = _items;
                StatusBar.Text = $"{_items.Count} manga{(_items.Count == 1 ? "" : "s")}";
            }
        }

        // ── Events ────────────────────────────────────────────────────────────

        private void RefreshBtn_Click(object sender, RoutedEventArgs e)
            => LoadMangas();

        private async void SeriesGrid_ItemClick(object sender, ItemClickEventArgs e)
        {
            if (e.ClickedItem is not MangaItem item) return;
            await ShowDetailDialog(item);
        }

        // ── Detail dialog stub ────────────────────────────────────────────────

        /// <summary>
        /// Opens a ContentDialog showing the manga detail and its page list.
        /// Calls wdmm_manga_get for the full record and wdmm_manga_list_pages
        /// for the page manifest. Page list is shown as a count + first few entries.
        /// </summary>
        private async System.Threading.Tasks.Task ShowDetailDialog(MangaItem item)
        {
            var body = BuildDetailBody(item);

            var dialog = new ContentDialog
            {
                Title           = item.Title,
                Content         = new ScrollViewer
                {
                    MaxHeight = 320,
                    Content   = new TextBlock
                    {
                        Text         = body,
                        FontSize     = 12,
                        TextWrapping = Microsoft.UI.Xaml.TextWrapping.Wrap,
                        FontFamily   = new Microsoft.UI.Xaml.Media.FontFamily(
                                           "Cascadia Mono,Consolas,monospace"),
                        Opacity      = 0.85,
                    }
                },
                CloseButtonText = "Close",
                XamlRoot        = XamlRoot,
            };

            await dialog.ShowAsync();
        }

        private string BuildDetailBody(MangaItem item)
        {
            var sb = new System.Text.StringBuilder();

            // ── Full record from wdmm_manga_get ──────────────────────────────
            try
            {
                var raw = NativeMethods.MangaGet(item.Id);
                if (!raw.StartsWith("error:", StringComparison.Ordinal) && raw != "null")
                {
                    using var doc = JsonDocument.Parse(raw);
                    var root = doc.RootElement;
                    void AppendField(string label, string key)
                    {
                        if (root.TryGetProperty(key, out var v) && v.ValueKind != JsonValueKind.Null)
                            sb.AppendLine($"{label,-16}{v}");
                    }
                    sb.AppendLine("── Metadata ─────────────────────────");
                    AppendField("ID",       "id");
                    AppendField("Title",    "title");
                    AppendField("Author",   "author");
                    AppendField("Series",   "series");
                    AppendField("Volume",   "volume");
                    AppendField("Chapter",  "chapter_number");
                    AppendField("Format",   "format");
                    AppendField("Pages",    "page_count");
                    AppendField("Source",   "source_name");
                    AppendField("Path",     "remote_path");
                    AppendField("Updated",  "updated_at");
                }
                else
                {
                    sb.AppendLine($"(wdmm_manga_get: {raw})");
                }
            }
            catch (Exception ex) { sb.AppendLine($"(manga_get unavailable: {ex.Message})"); }

            // ── Page list from wdmm_manga_list_pages ──────────────────────────
            try
            {
                var pagesJson = NativeMethods.MangaListPages(item.Id);
                if (!pagesJson.StartsWith("error:", StringComparison.Ordinal))
                {
                    using var doc = JsonDocument.Parse(pagesJson);
                    var arr = doc.RootElement;
                    int count = arr.GetArrayLength();
                    sb.AppendLine();
                    sb.AppendLine($"── Pages ({count}) ───────────────────────");
                    int shown = Math.Min(count, 5);
                    for (int i = 0; i < shown; i++)
                    {
                        var el = arr[i];
                        var relPath = el.TryGetProperty("rel_path", out var rp) ? rp.GetString() : "?";
                        sb.AppendLine($"  [{i}] {relPath}");
                    }
                    if (count > shown) sb.AppendLine($"  … and {count - shown} more");
                }
                else
                {
                    sb.AppendLine($"(wdmm_manga_list_pages: {pagesJson})");
                }
            }
            catch (Exception ex) { sb.AppendLine($"(manga_list_pages unavailable: {ex.Message})"); }

            // ── Progress ──────────────────────────────────────────────────────
            if (item.CurrentPage >= 0)
            {
                sb.AppendLine();
                sb.AppendLine($"── Progress ─────────────────────────");
                sb.AppendLine($"  Current page: {item.CurrentPage} " +
                              $"(p{item.CurrentPage + 1}/{(item.PageCount > 0 ? item.PageCount.ToString() : "?")})");
            }

            return sb.ToString().TrimEnd();
        }

        // ── Helper ────────────────────────────────────────────────────────────

        /// <summary>Minimal POCO for deserialising wdmm_manga_list_progress rows.</summary>
        private sealed class MangaProgressRow
        {
            [System.Text.Json.Serialization.JsonPropertyName("manga_id")]
            public string MangaId { get; set; } = "";

            [System.Text.Json.Serialization.JsonPropertyName("page_index")]
            public long PageIndex { get; set; }
        }
    }
}
