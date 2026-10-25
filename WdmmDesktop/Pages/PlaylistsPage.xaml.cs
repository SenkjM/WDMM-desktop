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

        // ── Helpers ───────────────────────────────────────────────────────────

        private static string EscapeJson(string s)
            => s.Replace("\\", "\\\\").Replace("\"", "\\\"");

        private void UpdateDetailTitle(string? name)
            => DetailTitle.Text = name ?? "Select a playlist";
    }
}
