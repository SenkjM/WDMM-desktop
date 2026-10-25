using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Navigation;
using WdmmDesktop.Generated;
using WdmmDesktop.Models;

namespace WdmmDesktop.Pages
{
    /// <summary>
    /// Playlists page — FFI only.
    /// All read/write goes through NativeMethods.PlaylistList / PlaylistCreate /
    /// PlaylistDelete / PlaylistListEntries. No OpenAPI HTTP, no HttpClient.
    /// </summary>
    public sealed partial class PlaylistsPage : Page
    {
        private string? _selectedPlaylistId;

        public PlaylistsPage() => InitializeComponent();

        protected override void OnNavigatedTo(NavigationEventArgs e)
        {
            base.OnNavigatedTo(e);
            Refresh();
        }

        // ── Data loading ──────────────────────────────────────────────────────

        private void Refresh()
        {
            List<PlaylistItem> lists;
            try
            {
                var json = NativeMethods.PlaylistList();
                if (json.StartsWith("error:"))
                {
                    StatusBar.Text = json;
                    PlaylistList.Items.Clear();
                    _selectedPlaylistId = null;
                    UpdateDetailTitle(null);
                    return;
                }
                lists = JsonSerializer.Deserialize<List<PlaylistItem>>(json)
                        ?? new List<PlaylistItem>();
            }
            catch (Exception ex)
            {
                // FFI not loaded (Linux dev box) or library not initialised.
                StatusBar.Text = $"Load failed: {ex.Message}";
                PlaylistList.Items.Clear();
                _selectedPlaylistId = null;
                UpdateDetailTitle(null);
                return;
            }

            PlaylistList.Items.Clear();
            foreach (var pl in lists)
                PlaylistList.Items.Add(pl);

            StatusBar.Text = lists.Count == 0
                ? "No playlists yet — click + to create one."
                : $"{lists.Count} playlist{(lists.Count == 1 ? "" : "s")}";

            // Clear detail pane.
            _selectedPlaylistId = null;
            DeleteBtn.IsEnabled = false;
            EntryToolbar.Visibility = Visibility.Collapsed;
            EntryList.Items.Clear();
            EntryEmptyState.Visibility = Visibility.Collapsed;
            UpdateDetailTitle(null);
        }

        private void LoadEntries(string playlistId, string name)
        {
            EntryList.Items.Clear();
            EntryEmptyState.Visibility = Visibility.Collapsed;
            UpdateDetailTitle(name);

            List<PlaylistEntryItem> entries;
            try
            {
                var json = NativeMethods.PlaylistListEntries(playlistId);
                if (json.StartsWith("error:"))
                {
                    EntryEmptyState.Visibility = Visibility.Visible;
                    EntryEmptyText.Text = json;
                    return;
                }
                entries = JsonSerializer.Deserialize<List<PlaylistEntryItem>>(json)
                          ?? new List<PlaylistEntryItem>();
            }
            catch (Exception ex)
            {
                EntryEmptyState.Visibility = Visibility.Visible;
                EntryEmptyText.Text = $"Error loading entries: {ex.Message}";
                return;
            }

            if (entries.Count == 0)
            {
                EntryEmptyState.Visibility = Visibility.Visible;
                EntryEmptyText.Text = "No entries in this playlist.";
            }
            else
            {
                foreach (var e in entries)
                    EntryList.Items.Add(e);
            }
        }

        // ── Event handlers ────────────────────────────────────────────────────

        private void RefreshBtn_Click(object sender, RoutedEventArgs e) => Refresh();

        private void PlaylistList_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (PlaylistList.SelectedItem is not PlaylistItem pl) return;
            _selectedPlaylistId = pl.Id;
            DeleteBtn.IsEnabled = true;
            EntryToolbar.Visibility = Visibility.Visible;
            LoadEntries(pl.Id, pl.Name);
        }

        private async void AddBtn_Click(object sender, RoutedEventArgs e)
        {
            var nameBox = new TextBox { PlaceholderText = "Playlist name", MinWidth = 260 };
            var dlg = new ContentDialog
            {
                Title             = "New Playlist",
                Content           = nameBox,
                PrimaryButtonText = "Create",
                CloseButtonText   = "Cancel",
                XamlRoot          = XamlRoot
            };

            if (await dlg.ShowAsync() != ContentDialogResult.Primary) return;

            var name = nameBox.Text.Trim();
            if (string.IsNullOrEmpty(name)) return;

            try
            {
                var createJson = $"{{\"name\":\"{EscapeJson(name)}\"}}";
                var result = NativeMethods.PlaylistCreate(createJson);
                if (result.StartsWith("error:"))
                {
                    StatusBar.Text = result;
                    return;
                }

                var created = JsonSerializer.Deserialize<PlaylistItem>(result);
                StatusBar.Text = created != null
                    ? $"Created \"{created.Name}\""
                    : "Playlist created.";
                Refresh();
            }
            catch (Exception ex)
            {
                StatusBar.Text = $"Create failed: {ex.Message}";
            }
        }

        private async void DeleteBtn_Click(object sender, RoutedEventArgs e)
        {
            if (_selectedPlaylistId == null) return;
            var pl = PlaylistList.SelectedItem as PlaylistItem;

            var dlg = new ContentDialog
            {
                Title             = "Delete Playlist",
                Content           = $"Delete \"{pl?.Name ?? _selectedPlaylistId}\"? This cannot be undone.",
                PrimaryButtonText = "Delete",
                CloseButtonText   = "Cancel",
                XamlRoot          = XamlRoot,
                DefaultButton     = ContentDialogButton.Close
            };

            if (await dlg.ShowAsync() != ContentDialogResult.Primary) return;

            try
            {
                var result = NativeMethods.PlaylistDelete(_selectedPlaylistId);
                if (result.StartsWith("error:"))
                {
                    StatusBar.Text = $"Delete failed ({result}).";
                    return;
                }

                StatusBar.Text = "Playlist deleted.";
                Refresh();
            }
            catch (Exception ex)
            {
                StatusBar.Text = $"Delete failed ({ex.Message}).";
            }
        }

        // ── Entry editing: add / remove / reorder (persisted via set_entries) ───

        private async void AddEntryBtn_Click(object sender, RoutedEventArgs e)
        {
            if (string.IsNullOrEmpty(_selectedPlaylistId)) return;

            // Load library tracks for the picker.
            var tracks = new List<TrackItem>();
            try
            {
                var json = NativeMethods.TrackList();
                if (!json.StartsWith("error:"))
                    tracks = JsonSerializer.Deserialize<List<TrackItem>>(json) ?? new();
            }
            catch { /* FFI not loaded */ }

            var searchBox = new TextBox
            {
                PlaceholderText = "Search tracks…",
                MinWidth        = 340
            };
            var resultList = new ListView
            {
                MinHeight     = 300,
                SelectionMode = ListViewSelectionMode.Single
            };
            foreach (var t in tracks.OrderBy(x => x.Title))
                resultList.Items.Add(t);

            searchBox.TextChanged += (_, _) =>
            {
                var q = searchBox.Text.Trim();
                resultList.Items.Clear();
                foreach (var t in tracks)
                    if (q.Length == 0 || t.Title.Contains(q, StringComparison.OrdinalIgnoreCase)
                                       || t.Artist.Contains(q, StringComparison.OrdinalIgnoreCase))
                        resultList.Items.Add(t);
            };

            var panel = new StackPanel { Spacing = 8 };
            panel.Children.Add(searchBox);
            panel.Children.Add(resultList);

            var dlg = new ContentDialog
            {
                Title             = "Add track to playlist",
                Content           = panel,
                PrimaryButtonText = "Add",
                CloseButtonText   = "Cancel",
                XamlRoot          = XamlRoot
            };
            if (await dlg.ShowAsync() != ContentDialogResult.Primary) return;
            if (resultList.SelectedItem is not TrackItem picked) return;

            // Append a new entry derived from the picked track.
            var entries = EntryList.Items.OfType<PlaylistEntryItem>().ToList();
            var newEntry = new PlaylistEntryItem
            {
                PlaylistId  = _selectedPlaylistId,
                Position    = entries.Count,
                MusicId     = picked.Id,
                SourceName  = "library",
                RemotePath  = picked.Path,
                Title       = picked.Title,
                DurationMs  = 0
            };
            entries.Add(newEntry);

            if (PersistEntries(entries))
            {
                EntryList.Items.Clear();
                foreach (var en in entries) EntryList.Items.Add(en);
                EntryEmptyState.Visibility = entries.Count == 0 ? Visibility.Visible : Visibility.Collapsed;
            }
        }

        private void RemoveEntryBtn_Click(object sender, RoutedEventArgs e)
        {
            if (sender is not Button btn || btn.Tag is not PlaylistEntryItem row) return;
            if (string.IsNullOrEmpty(_selectedPlaylistId)) return;

            var entries = EntryList.Items.OfType<PlaylistEntryItem>()
                .Where(en => en.MusicId != row.MusicId || en.Position != row.Position)
                .ToList();

            if (PersistEntries(entries))
            {
                EntryList.Items.Clear();
                foreach (var en in entries) EntryList.Items.Add(en);
                if (entries.Count == 0)
                {
                    EntryEmptyState.Visibility = Visibility.Visible;
                    EntryEmptyText.Text = "No entries in this playlist.";
                }
            }
        }

        private void MoveEntryUpBtn_Click(object sender, RoutedEventArgs e)
        {
            if (sender is not Button btn || btn.Tag is not PlaylistEntryItem row) return;
            ReorderEntry(row, -1);
        }

        private void MoveEntryDownBtn_Click(object sender, RoutedEventArgs e)
        {
            if (sender is not Button btn || btn.Tag is not PlaylistEntryItem row) return;
            ReorderEntry(row, +1);
        }

        private void ReorderEntry(PlaylistEntryItem row, int delta)
        {
            var entries = EntryList.Items.OfType<PlaylistEntryItem>().ToList();
            var idx = entries.FindIndex(en => en.MusicId == row.MusicId && en.Position == row.Position);
            if (idx < 0) return;
            var newIdx = idx + delta;
            if (newIdx < 0 || newIdx >= entries.Count) return;

            (entries[idx], entries[newIdx]) = (entries[newIdx], entries[idx]);
            if (PersistEntries(entries))
            {
                EntryList.Items.Clear();
                foreach (var en in entries) EntryList.Items.Add(en);
            }
        }

        /// <summary>
        /// Serialize the current entry list and persist it via wdmm_playlist_set_entries.
        /// Reindexes Position to a contiguous 0-based sequence. Returns true on success.
        /// </summary>
        private bool PersistEntries(List<PlaylistEntryItem> entries)
        {
            for (int i = 0; i < entries.Count; i++)
                entries[i].Position = i;

            var parts = entries.Select(en =>
                $"{{\"music_id\":\"{EscapeJson(en.MusicId)}\","          +
                $"\"source_name\":\"{EscapeJson(en.SourceName)}\","      +
                $"\"remote_path\":\"{EscapeJson(en.RemotePath)}\","      +
                $"\"title\":\"{EscapeJson(en.Title)}\","                  +
                $"\"duration_ms\":{en.DurationMs}}}");
            var jsonArray = $"[{string.Join(",", parts)}]";

            try
            {
                var result = NativeMethods.PlaylistSetEntries(_selectedPlaylistId!, jsonArray);
                if (result.StartsWith("error:"))
                {
                    StatusBar.Text = result;
                    return false;
                }
                StatusBar.Text = $"Saved ({entries.Count} entries).";
                return true;
            }
            catch (Exception ex)
            {
                StatusBar.Text = $"Save failed: {ex.Message}";
                return false;
            }
        }

        // ── Helpers ───────────────────────────────────────────────────────────

        private static string EscapeJson(string s)
            => s.Replace("\\", "\\\\").Replace("\"", "\\\"");

        private void UpdateDetailTitle(string? name)
            => DetailTitle.Text = name ?? "Select a playlist";
    }
}
