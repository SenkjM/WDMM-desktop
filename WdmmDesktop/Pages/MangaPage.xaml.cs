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
    // On navigate: wdmm_manga_list() + wdmm_manga_list_progress(). Detail dialog
    // supports wdmm_manga_get/list_pages read plus upsert / set_progress / delete.
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

        // ── Detail dialog (read + write via manga FFI) ───────────────────────

        /// <summary>
        /// Opens a ContentDialog showing the manga detail and its page list.
        /// Calls wdmm_manga_get for the full record and wdmm_manga_list_pages
        /// for the page manifest. Page list is shown as a count + first few entries.
        /// </summary>
        private async System.Threading.Tasks.Task ShowDetailDialog(MangaItem item)
        {
            var body = BuildDetailBody(item);

            var titleBox = new TextBox { Header = "Title", Text = item.Title, Margin = new Thickness(0, 0, 0, 8) };
            var authorBox = new TextBox { Header = "Author", Text = item.Author ?? "", Margin = new Thickness(0, 0, 0, 8) };
            var seriesBox = new TextBox { Header = "Series", Text = item.Series ?? "", Margin = new Thickness(0, 0, 0, 8) };
            var progressBox = new NumberBox
            {
                Header = "Progress page index (0-based)",
                Minimum = 0,
                Maximum = item.PageCount > 0 ? item.PageCount - 1 : 99999,
                Value = item.CurrentPage >= 0 ? item.CurrentPage : 0,
                SpinButtonPlacementMode = NumberBoxSpinButtonPlacementMode.Compact,
                Margin = new Thickness(0, 0, 0, 8),
            };
            var meta = new TextBlock
            {
                Text = body,
                FontSize = 11,
                TextWrapping = Microsoft.UI.Xaml.TextWrapping.Wrap,
                FontFamily = new Microsoft.UI.Xaml.Media.FontFamily("Cascadia Mono,Consolas,monospace"),
                Opacity = 0.8,
                Margin = new Thickness(0, 8, 0, 0),
            };
            var stack = new StackPanel { Spacing = 4 };
            stack.Children.Add(titleBox);
            stack.Children.Add(authorBox);
            stack.Children.Add(seriesBox);
            stack.Children.Add(progressBox);
            stack.Children.Add(new TextBlock { Text = "Raw metadata", FontWeight = Microsoft.UI.Text.FontWeights.SemiBold, Margin = new Thickness(0, 8, 0, 0) });
            stack.Children.Add(meta);

            var dialog = new ContentDialog
            {
                Title = item.Title,
                Content = new ScrollViewer { MaxHeight = 420, Content = stack },
                PrimaryButtonText = "Save",
                SecondaryButtonText = "Set progress",
                CloseButtonText = "Close",
                DefaultButton = ContentDialogButton.Primary,
                XamlRoot = XamlRoot,
            };

            // Delete via a separate confirm — ContentDialog has only 3 buttons.
            var deleteBtn = new Button { Content = "Delete…", Margin = new Thickness(0, 8, 0, 0) };
            stack.Children.Insert(0, deleteBtn);
            string? pendingDelete = null;
            deleteBtn.Click += async (_, __) =>
            {
                var confirm = new ContentDialog
                {
                    Title = "Delete manga?",
                    Content = $"Remove «{item.Title}» from the library? This calls wdmm_manga_delete.",
                    PrimaryButtonText = "Delete",
                    CloseButtonText = "Cancel",
                    DefaultButton = ContentDialogButton.Close,
                    XamlRoot = XamlRoot,
                };
                if (await confirm.ShowAsync() == ContentDialogResult.Primary)
                {
                    pendingDelete = item.Id;
                    dialog.Hide();
                }
            };

            var result = await dialog.ShowAsync();
            if (pendingDelete != null)
            {
                try
                {
                    var r = NativeMethods.MangaDelete(pendingDelete);
                    if (r.StartsWith("error:", StringComparison.Ordinal))
                        await ShowMsg("Delete failed", r);
                }
                catch (Exception ex) { await ShowMsg("Delete failed", ex.Message); }
                LoadMangas();
                return;
            }

            if (result == ContentDialogResult.Primary)
            {
                // Upsert metadata
                try
                {
                    var payload = new Dictionary<string, object?>
                    {
                        ["id"] = item.Id,
                        ["title"] = titleBox.Text?.Trim() ?? item.Title,
                        ["author"] = string.IsNullOrWhiteSpace(authorBox.Text) ? null : authorBox.Text.Trim(),
                        ["series"] = string.IsNullOrWhiteSpace(seriesBox.Text) ? null : seriesBox.Text.Trim(),
                        ["volume"] = item.Volume,
                        ["chapter_number"] = item.ChapterNumber,
                        ["page_count"] = item.PageCount,
                        ["cover_state"] = item.CoverStateValue,
                        ["format"] = item.Format,
                        ["source_name"] = item.SourceName,
                        ["remote_path"] = item.RemotePath,
                    };
                    var json = JsonSerializer.Serialize(payload);
                    var r = NativeMethods.MangaUpsert(json);
                    if (r.StartsWith("error:", StringComparison.Ordinal))
                        await ShowMsg("Upsert failed", r);
                }
                catch (Exception ex) { await ShowMsg("Upsert failed", ex.Message); }
                LoadMangas();
            }
            else if (result == ContentDialogResult.Secondary)
            {
                try
                {
                    var page = (long)Math.Max(0, progressBox.Value);
                    var r = NativeMethods.MangaSetProgress(item.Id, page);
                    if (r.StartsWith("error:", StringComparison.Ordinal))
                        await ShowMsg("Set progress failed", r);
                }
                catch (Exception ex) { await ShowMsg("Set progress failed", ex.Message); }
                LoadMangas();
            }
        }

        private async System.Threading.Tasks.Task ShowMsg(string title, string body)
        {
            var d = new ContentDialog
            {
                Title = title,
                Content = body,
                CloseButtonText = "OK",
                XamlRoot = XamlRoot,
            };
            await d.ShowAsync();
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
