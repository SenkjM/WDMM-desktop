using System;
using System.Collections.Generic;
using System.Text.Json;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Navigation;
using WdmmDesktop.Generated;
using WdmmDesktop.Models;
using WdmmDesktop;

namespace WdmmDesktop.Pages
{
    public sealed partial class MusicPage : Page
    {
        private List<TrackItem> _allTracks = new();
        private bool _libraryInitAttempted;

        public MusicPage() => InitializeComponent();

        protected override void OnNavigatedTo(NavigationEventArgs e)
        {
            base.OnNavigatedTo(e);
            EnsureLibraryInit();
            LoadTracks(query: null);
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
                // FFI not loaded or library not yet initialised — show empty state.
                _allTracks = new List<TrackItem>();
            }

            ApplyList(_allTracks, query);
        }

        private void ApplyList(List<TrackItem> tracks, string? query)
        {
            TrackList.Items.Clear();

            if (tracks.Count == 0)
            {
                EmptyState.Visibility = Visibility.Visible;
                EmptyText.Text = string.IsNullOrWhiteSpace(query)
                    ? "No tracks in library. Sync from Network page to populate."
                    : $"No results for "{query}".";
                StatusBar.Text = "";
            }
            else
            {
                EmptyState.Visibility = Visibility.Collapsed;
                foreach (var t in tracks)
                    TrackList.Items.Add(t);
                StatusBar.Text = $"{tracks.Count} track{(tracks.Count == 1 ? "" : "s")}";
                if (!string.IsNullOrWhiteSpace(query))
                    StatusBar.Text += $" matching "{query}"";
            }
        }

        // ── Events ────────────────────────────────────────────────────────────

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
                // User cleared the box — restore full list.
                ApplyList(_allTracks, query: null);
            }
        }

        private void SearchBox_QuerySubmitted(AutoSuggestBox sender, AutoSuggestBoxQuerySubmittedEventArgs args)
        {
            var q = (args.QueryText ?? sender.Text).Trim();
            LoadTracks(string.IsNullOrEmpty(q) ? null : q);
        }

        private void CastBtn_Click(object sender, RoutedEventArgs e)
        {
            if (sender is Button btn && btn.Tag is TrackItem track)
            {
                // Hand off selected track to CastPage via cross-page state.
                AppState.PendingCastTrack = track;
                // Navigate to CastPage — MainWindow will handle the routing.
                Frame.Navigate(typeof(CastPage));
                // Mirror the sidebar selection via the MainWindow.
                (App.MainWindow as MainWindow)?.SelectNavItem("Cast");
            }
        }
    }
}
