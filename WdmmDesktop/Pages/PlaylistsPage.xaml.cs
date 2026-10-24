using System;
using System.Collections.Generic;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Threading.Tasks;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Navigation;
using WdmmDesktop.Generated;
using WdmmDesktop.Models;

namespace WdmmDesktop.Pages
{
    // Preferred long-term path: replace all HTTP calls here with wdmm_playlist_list /
    // wdmm_playlist_save FFI once wdmm-core exports them (not present at d8b2217).
    // Until then we bridge through the local OpenAPI server at 127.0.0.1:{port}.
    public sealed partial class PlaylistsPage : Page
    {
        private static readonly HttpClient _http = new();

        private string? _selectedPlaylistId;

        public PlaylistsPage() => InitializeComponent();

        protected override void OnNavigatedTo(NavigationEventArgs e)
        {
            base.OnNavigatedTo(e);
            _ = RefreshAsync();
        }

        // ── Server guard ──────────────────────────────────────────────────────

        /// <summary>Returns (running, port, token). Never throws.</summary>
        private static (bool running, int port, string token) GetServerState()
        {
            try
            {
                bool running = NativeMethods.IsOpenApiRunning();
                int  port    = NativeMethods.OpenApiPort();
                string token = NativeMethods.TokenGet();
                return (running, port, token);
            }
            catch
            {
                // FFI DLL absent on Linux dev box — tolerate.
                return (false, -1, "");
            }
        }

        // ── Data loading ──────────────────────────────────────────────────────

        private async Task RefreshAsync()
        {
            var (running, port, token) = GetServerState();

            ServerInfoBar.IsOpen = !running;
            MainGrid.IsEnabled   = running;

            if (!running)
            {
                PlaylistList.Items.Clear();
                EntryList.Items.Clear();
                _selectedPlaylistId = null;
                UpdateDetailTitle(null);
                return;
            }

            StatusBar.Text = "Loading…";
            try
            {
                var lists = await GetPlaylistsAsync(port, token);
                PlaylistList.Items.Clear();
                foreach (var pl in lists)
                    PlaylistList.Items.Add(pl);

                StatusBar.Text = lists.Count == 0
                    ? "No playlists yet — click + to create one."
                    : $"{lists.Count} playlist{(lists.Count == 1 ? "" : "s")}";
            }
            catch (Exception ex)
            {
                StatusBar.Text = $"Error loading playlists: {ex.Message}";
            }
        }

        private async Task LoadEntriesAsync(string playlistId, string name, int port, string token)
        {
            EntryList.Items.Clear();
            EntryEmptyState.Visibility = Visibility.Collapsed;
            UpdateDetailTitle(name);

            try
            {
                var entries = await GetEntriesAsync(playlistId, port, token);
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
            catch (Exception ex)
            {
                EntryEmptyState.Visibility = Visibility.Visible;
                EntryEmptyText.Text = $"Error loading entries: {ex.Message}";
            }
        }

        // ── HTTP helpers ──────────────────────────────────────────────────────

        private static HttpRequestMessage BuildRequest(
            HttpMethod method, string url, string token, object? body = null)
        {
            var req = new HttpRequestMessage(method, url);
            req.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
            if (body != null)
            {
                var json = JsonSerializer.Serialize(body);
                req.Content = new StringContent(json, Encoding.UTF8, "application/json");
            }
            return req;
        }

        private static async Task<List<PlaylistItem>> GetPlaylistsAsync(int port, string token)
        {
            var url  = $"http://127.0.0.1:{port}/v1/playlists";
            var resp = await _http.SendAsync(BuildRequest(HttpMethod.Get, url, token));
            resp.EnsureSuccessStatusCode();
            var body = await resp.Content.ReadAsStringAsync();
            using var doc = JsonDocument.Parse(body);
            var arr = doc.RootElement.GetProperty("playlists");
            return JsonSerializer.Deserialize<List<PlaylistItem>>(arr.GetRawText())
                   ?? new List<PlaylistItem>();
        }

        private static async Task<List<PlaylistEntryItem>> GetEntriesAsync(
            string id, int port, string token)
        {
            var url  = $"http://127.0.0.1:{port}/v1/playlists/{Uri.EscapeDataString(id)}/entries";
            var resp = await _http.SendAsync(BuildRequest(HttpMethod.Get, url, token));
            resp.EnsureSuccessStatusCode();
            var body = await resp.Content.ReadAsStringAsync();
            using var doc = JsonDocument.Parse(body);
            var arr = doc.RootElement.GetProperty("entries");
            return JsonSerializer.Deserialize<List<PlaylistEntryItem>>(arr.GetRawText())
                   ?? new List<PlaylistEntryItem>();
        }

        private static async Task<PlaylistItem> CreatePlaylistAsync(
            string name, int port, string token)
        {
            var url  = $"http://127.0.0.1:{port}/v1/playlists";
            var resp = await _http.SendAsync(
                BuildRequest(HttpMethod.Post, url, token, new { name }));
            resp.EnsureSuccessStatusCode(); // 201
            var body = await resp.Content.ReadAsStringAsync();
            return JsonSerializer.Deserialize<PlaylistItem>(body)
                   ?? throw new InvalidOperationException("Empty response from server.");
        }

        private static async Task DeletePlaylistAsync(string id, int port, string token)
        {
            var url  = $"http://127.0.0.1:{port}/v1/playlists/{Uri.EscapeDataString(id)}";
            var resp = await _http.SendAsync(BuildRequest(HttpMethod.Delete, url, token));
            resp.EnsureSuccessStatusCode(); // 204
        }

        // ── Event handlers ────────────────────────────────────────────────────

        private void RefreshBtn_Click(object sender, RoutedEventArgs e)
            => _ = RefreshAsync();

        private void PlaylistList_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (PlaylistList.SelectedItem is not PlaylistItem pl) return;
            _selectedPlaylistId = pl.Id;
            DeleteBtn.IsEnabled = true;

            var (running, port, token) = GetServerState();
            if (running)
                _ = LoadEntriesAsync(pl.Id, pl.Name, port, token);
        }

        private async void AddBtn_Click(object sender, RoutedEventArgs e)
        {
            var (running, port, token) = GetServerState();
            if (!running) { ServerInfoBar.IsOpen = true; return; }

            // Name input dialog
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
                var created = await CreatePlaylistAsync(name, port, token);
                PlaylistList.Items.Add(created);
                PlaylistList.SelectedItem = created;
                StatusBar.Text = $"Created "{created.Name}".";
            }
            catch (Exception ex)
            {
                StatusBar.Text = $"Create failed: {ex.Message}";
            }
        }

        private async void DeleteBtn_Click(object sender, RoutedEventArgs e)
        {
            if (_selectedPlaylistId == null) return;
            var (running, port, token) = GetServerState();
            if (!running) { ServerInfoBar.IsOpen = true; return; }

            var pl = PlaylistList.SelectedItem as PlaylistItem;
            var dlg = new ContentDialog
            {
                Title             = "Delete Playlist",
                Content           = $"Delete "{pl?.Name ?? _selectedPlaylistId}"? This cannot be undone.",
                PrimaryButtonText = "Delete",
                CloseButtonText   = "Cancel",
                XamlRoot          = XamlRoot
            };

            if (await dlg.ShowAsync() != ContentDialogResult.Primary) return;

            try
            {
                await DeletePlaylistAsync(_selectedPlaylistId, port, token);
                PlaylistList.Items.Remove(pl);
                _selectedPlaylistId = null;
                DeleteBtn.IsEnabled = false;
                EntryList.Items.Clear();
                EntryEmptyState.Visibility = Visibility.Collapsed;
                UpdateDetailTitle(null);
                StatusBar.Text = "Playlist deleted.";
            }
            catch (Exception ex)
            {
                StatusBar.Text = $"Delete failed ({ex.Message}).";
            }
        }

        // ── Helpers ───────────────────────────────────────────────────────────

        private void UpdateDetailTitle(string? name)
            => DetailTitle.Text = name ?? "Select a playlist";
    }
}
