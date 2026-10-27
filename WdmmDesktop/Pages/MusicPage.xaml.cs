using System;
using System.Collections.Generic;
using System.Text.Json;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Navigation;
using WdmmDesktop.Dialogs;
using WdmmDesktop.Generated;
using WdmmDesktop.Models;
using WdmmDesktop.Services;

namespace WdmmDesktop.Pages
{
    /// <summary>
    /// Music library page with three tabs: Tracks / Albums / Artists.
    /// Wave 5: Albums and Artists tabs backed by AlbumList/ArtistList/TracksByAlbum/TracksByArtist FFI.
    /// No playback — Cast is the only audio action.
    /// Empty-state text uses Loc keys (Wave i18n fix).
    /// </summary>
    public sealed partial class MusicPage : Page
    {
        private List<TrackItem> _allTracks = new();
        private bool _libraryInitAttempted;

        public MusicPage() => InitializeComponent();

        protected override void OnNavigatedTo(NavigationEventArgs e)
        {
            base.OnNavigatedTo(e);
            ApplyTabLabels();
            EnsureLibraryInit();
            LoadTracks(query: null);
        }

        private void ApplyTabLabels()
        {
            TabTracks.Content  = Loc.Get("Music.Tracks");
            TabAlbums.Content  = Loc.Get("Music.Albums");
            TabArtists.Content = Loc.Get("Music.Artists");
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

        // ── Tab switching ─────────────────────────────────────────────────────

        private void MusicTab_Checked(object sender, RoutedEventArgs e)
        {
            if (sender is not RadioButton rb) return;
            var tag = rb.Tag?.ToString() ?? "Tracks";

            PanelTracks.Visibility  = tag == "Tracks"  ? Visibility.Visible : Visibility.Collapsed;
            PanelAlbums.Visibility  = tag == "Albums"  ? Visibility.Visible : Visibility.Collapsed;
            PanelArtists.Visibility = tag == "Artists" ? Visibility.Visible : Visibility.Collapsed;
            SearchRow.Visibility    = tag == "Tracks"  ? Visibility.Visible : Visibility.Collapsed;

            if (tag == "Albums")  LoadAlbums();
            if (tag == "Artists") LoadArtists();
        }

        // ── Tracks tab ────────────────────────────────────────────────────────

        private void LoadTracks(string? query)
        {
            try
            {
                var json = string.IsNullOrWhiteSpace(query)
                    ? NativeMethods.TrackList()
                    : NativeMethods.TrackSearch(query);
                _allTracks = JsonSerializer.Deserialize<List<TrackItem>>(json)
                             ?? new List<TrackItem>();
            }
            catch
            {
                _allTracks = new List<TrackItem>();
            }
            ApplyTrackList(_allTracks, query);
        }

        private void ApplyTrackList(List<TrackItem> tracks, string? query)
        {
            TrackList.Items.Clear();
            if (tracks.Count == 0)
            {
                EmptyState.Visibility = Visibility.Visible;
                EmptyText.Text = string.IsNullOrWhiteSpace(query)
                    ? Loc.Get("Music.EmptyLibrary")
                    : $"{Loc.Get("Music.EmptyLibrary")} (no results for \"{query}\")";
                StatusBar.Text = "";
            }
            else
            {
                EmptyState.Visibility = Visibility.Collapsed;
                foreach (var t in tracks)
                    TrackList.Items.Add(t);
                StatusBar.Text = $"{tracks.Count} track{(tracks.Count == 1 ? "" : "s")}";
                if (!string.IsNullOrWhiteSpace(query))
                    StatusBar.Text += $" matching \"{query}\"";
            }
        }

        // ── Albums tab (Wave 5) ───────────────────────────────────────────────

        private void LoadAlbums()
        {
            AlbumList.Items.Clear();
            AlbumTrackList.Items.Clear();
            AlbumEmptyState.Visibility = Visibility.Collapsed;

            try
            {
                var json = NativeMethods.AlbumList();
                if (json.StartsWith("error:", StringComparison.Ordinal)) return;

                // AlbumList returns JSON array of strings (album titles).
                var albums = JsonSerializer.Deserialize<List<string>>(json) ?? new List<string>();
                foreach (var a in albums)
                    if (!string.IsNullOrEmpty(a))
                        AlbumList.Items.Add(a);

                if (AlbumList.Items.Count == 0)
                {
                    AlbumEmptyState.Visibility = Visibility.Visible;
                    AlbumEmptyText.Text = Loc.Get("Music.EmptyLibrary");
                }
            }
            catch { }
        }

        private void AlbumList_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (AlbumList.SelectedItem is not string album) return;

            AlbumTrackList.Items.Clear();
            AlbumEmptyState.Visibility = Visibility.Collapsed;
            try
            {
                var json = NativeMethods.TracksByAlbum(album);
                if (json.StartsWith("error:", StringComparison.Ordinal)) return;

                var tracks = JsonSerializer.Deserialize<List<TrackItem>>(json) ?? new List<TrackItem>();
                foreach (var t in tracks)
                    AlbumTrackList.Items.Add(t);

                if (tracks.Count == 0)
                {
                    AlbumEmptyState.Visibility = Visibility.Visible;
                    AlbumEmptyText.Text = "No tracks for this album.";
                }
                else
                {
                    StatusBar.Text = $"{tracks.Count} track{(tracks.Count == 1 ? "" : "s")} in "{album}"";
                }
            }
            catch { }
        }

        // ── Artists tab (Wave 5) ──────────────────────────────────────────────

        private void LoadArtists()
        {
            ArtistList.Items.Clear();
            ArtistTrackList.Items.Clear();
            ArtistEmptyState.Visibility = Visibility.Collapsed;

            try
            {
                var json = NativeMethods.ArtistList();
                if (json.StartsWith("error:", StringComparison.Ordinal)) return;

                var artists = JsonSerializer.Deserialize<List<string>>(json) ?? new List<string>();
                foreach (var a in artists)
                    if (!string.IsNullOrEmpty(a))
                        ArtistList.Items.Add(a);

                if (ArtistList.Items.Count == 0)
                {
                    ArtistEmptyState.Visibility = Visibility.Visible;
                    ArtistEmptyText.Text = Loc.Get("Music.EmptyLibrary");
                }
            }
            catch { }
        }

        private void ArtistList_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (ArtistList.SelectedItem is not string artist) return;

            ArtistTrackList.Items.Clear();
            ArtistEmptyState.Visibility = Visibility.Collapsed;
            try
            {
                var json = NativeMethods.TracksByArtist(artist);
                if (json.StartsWith("error:", StringComparison.Ordinal)) return;

                var tracks = JsonSerializer.Deserialize<List<TrackItem>>(json) ?? new List<TrackItem>();
                foreach (var t in tracks)
                    ArtistTrackList.Items.Add(t);

                if (tracks.Count == 0)
                {
                    ArtistEmptyState.Visibility = Visibility.Visible;
                    ArtistEmptyText.Text = "No tracks for this artist.";
                }
                else
                {
                    StatusBar.Text = $"{tracks.Count} track{(tracks.Count == 1 ? "" : "s")} by "{artist}"";
                }
            }
            catch { }
        }

        // ── Tracks tab events ─────────────────────────────────────────────────

        private void RefreshBtn_Click(object sender, RoutedEventArgs e)
        {
            SearchBox.Text = "";
            LoadTracks(query: null);
        }

        private void SearchBox_TextChanged(AutoSuggestBox sender, AutoSuggestBoxTextChangedEventArgs args)
        {
            if (args.Reason == AutoSuggestionBoxTextChangeReason.UserInput
                && string.IsNullOrWhiteSpace(sender.Text))
            {
                ApplyTrackList(_allTracks, query: null);
            }
        }

        private void SearchBox_QuerySubmitted(AutoSuggestBox sender, AutoSuggestBoxQuerySubmittedEventArgs args)
        {
            var q = (args.QueryText ?? sender.Text).Trim();
            LoadTracks(string.IsNullOrEmpty(q) ? null : q);
        }

        private async void CastBtn_Click(object sender, RoutedEventArgs e)
        {
            if (sender is Button btn && btn.Tag is TrackItem track)
            {
                var dialog = new CastDialog(track) { XamlRoot = XamlRoot };
                await dialog.ShowAsync();
            }
        }
    }
}
