using System;
using System.Collections.Generic;
using System.IO;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Input;
using Microsoft.UI.Xaml.Navigation;

namespace WdmmDesktop.Pages
{
    public sealed partial class FoldersPage : Page
    {
        // Local browser only. Remote WebDAV listing is blocked until
        // wdmm-core exports wdmm_webdav_list (not present at d8b2217).
        // When it lands, replace the RemoteBlockedBar with a real tree driven by that FFI.
        private string _currentPath = string.Empty;

        public FoldersPage() => InitializeComponent();

        protected override void OnNavigatedTo(NavigationEventArgs e)
        {
            base.OnNavigatedTo(e);
            // Default to the user's home so the page is immediately useful.
            NavigateTo(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile));
        }

        // ── Source toggle ─────────────────────────────────────────────────────

        private void SourceToggle_Toggled(object sender, RoutedEventArgs e)
        {
            bool remote = SourceToggle.IsOn;

            // Show/hide the appropriate panel.
            LocalGrid.Visibility        = remote ? Visibility.Collapsed : Visibility.Visible;
            RemoteBlockedBar.Visibility = remote ? Visibility.Visible   : Visibility.Collapsed;
            RemoteBlockedBar.IsOpen     = remote;

            if (remote)
                StatusBar.Text = "";
            else
                // Refresh the local view when switching back.
                NavigateTo(_currentPath);
        }

        // ── Path bar ──────────────────────────────────────────────────────────

        private void PathBox_KeyDown(object sender, KeyRoutedEventArgs e)
        {
            if (e.Key == Windows.System.VirtualKey.Enter)
                NavigateTo(PathBox.Text.Trim());
        }

        private void BrowseBtn_Click(object sender, RoutedEventArgs e)
        {
            NavigateTo(PathBox.Text.Trim());
        }

        private void UpBtn_Click(object sender, RoutedEventArgs e)
        {
            var parent = Directory.GetParent(_currentPath);
            if (parent != null) NavigateTo(parent.FullName);
        }

        private void ItemsList_DoubleTapped(object sender, DoubleTappedRoutedEventArgs e)
        {
            if (ItemsList.SelectedItem is FileEntry fe && fe.IsDirectory)
                NavigateTo(fe.FullPath);
        }

        // ── Navigation ────────────────────────────────────────────────────────

        private void NavigateTo(string path)
        {
            ItemsList.Items.Clear();
            SetEmptyState(false, "");

            if (string.IsNullOrEmpty(path))
            {
                PathBox.Text = path;
                UpBtn.IsEnabled = false;
                SetEmptyState(true, "Enter a path above or click Browse…");
                StatusBar.Text = "";
                return;
            }

            if (!Directory.Exists(path))
            {
                PathBox.Text = path;
                UpBtn.IsEnabled = false;
                SetEmptyState(true, $"Path not found: {path}");
                StatusBar.Text = "";
                return;
            }

            try
            {
                _currentPath = path;
                PathBox.Text = path;
                UpBtn.IsEnabled = Directory.GetParent(path) != null;

                int dirs = 0, files = 0;

                foreach (var dir in Directory.GetDirectories(path))
                {
                    ItemsList.Items.Add(new FileEntry(Path.GetFileName(dir), dir, isDirectory: true, 0));
                    dirs++;
                }

                foreach (var file in Directory.GetFiles(path))
                {
                    long size = 0;
                    try { size = new FileInfo(file).Length; } catch { }
                    ItemsList.Items.Add(new FileEntry(Path.GetFileName(file), file, isDirectory: false, size));
                    files++;
                }

                if (dirs == 0 && files == 0)
                    SetEmptyState(true, "This folder is empty.");
                else
                    StatusBar.Text = $"{dirs} folder{(dirs == 1 ? "" : "s")}, {files} file{(files == 1 ? "" : "s")}";
            }
            catch (UnauthorizedAccessException)
            {
                SetEmptyState(true, "Access denied — cannot list this folder.");
                StatusBar.Text = "";
            }
            catch (IOException ex)
            {
                SetEmptyState(true, $"I/O error: {ex.Message}");
                StatusBar.Text = "";
            }
        }

        private void SetEmptyState(bool visible, string message)
        {
            EmptyState.Visibility = visible ? Visibility.Visible : Visibility.Collapsed;
            EmptyText.Text = message;
            if (!visible && ItemsList.Items.Count > 0)
                StatusBar.Text = ""; // will be set after counting in NavigateTo
        }

        // ── File entry model ──────────────────────────────────────────────────

        /// <summary>One row in the local folder view.</summary>
        public sealed class FileEntry
        {
            public string Name       { get; }
            public string FullPath   { get; }
            public bool   IsDirectory { get; }
            public long   SizeBytes  { get; }
            public string IconGlyph  => IsDirectory ? "" : ""; // Folder / File
            public string Meta       => IsDirectory ? "folder" : FormatSize(SizeBytes);

            public FileEntry(string name, string fullPath, bool isDirectory, long sizeBytes)
            {
                Name = name; FullPath = fullPath; IsDirectory = isDirectory; SizeBytes = sizeBytes;
            }

            private static string FormatSize(long bytes) => bytes switch
            {
                < 1024                => $"{bytes} B",
                < 1024 * 1024         => $"{bytes / 1024.0:F1} KB",
                < 1024L * 1024 * 1024 => $"{bytes / (1024.0 * 1024):F1} MB",
                _                     => $"{bytes / (1024.0 * 1024 * 1024):F2} GB"
            };
        }
    }
}
