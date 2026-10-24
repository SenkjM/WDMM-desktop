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
        // Local browser only. Remote WebDAV listing is a stub until
        // wdmm-core exports wdmm_webdav_list (see the TODO note in the XAML).
        private string _currentPath = string.Empty;

        public FoldersPage() => InitializeComponent();

        protected override void OnNavigatedTo(NavigationEventArgs e)
        {
            base.OnNavigatedTo(e);
            // Default to the user's home so the page is immediately useful.
            NavigateTo(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile));
        }

        private void PathBox_KeyDown(object sender, KeyRoutedEventArgs e)
        {
            if (e.Key == Windows.System.VirtualKey.Enter)
                NavigateTo(PathBox.Text.Trim());
        }

        private void BrowseBtn_Click(object sender, RoutedEventArgs e)
        {
            // Navigate to whatever path is currently in the box.
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

        private void NavigateTo(string path)
        {
            ItemsList.Items.Clear();

            if (string.IsNullOrEmpty(path) || !Directory.Exists(path))
            {
                PathBox.Text = path;
                UpBtn.IsEnabled = false;
                return;
            }

            try
            {
                _currentPath = path;
                PathBox.Text = path;
                UpBtn.IsEnabled = Directory.GetParent(path) != null;

                foreach (var dir in Directory.GetDirectories(path))
                {
                    var name = Path.GetFileName(dir);
                    ItemsList.Items.Add(new FileEntry(name, dir, isDirectory: true, 0));
                }

                foreach (var file in Directory.GetFiles(path))
                {
                    var name  = Path.GetFileName(file);
                    long size = 0;
                    try { size = new FileInfo(file).Length; } catch { }
                    ItemsList.Items.Add(new FileEntry(name, file, isDirectory: false, size));
                }
            }
            catch (UnauthorizedAccessException) { /* skip inaccessible entries */ }
            catch (IOException) { /* transient FS error */ }
        }

        /// <summary>One row in the folder view. Self-contained so it can render without a model file.</summary>
        public sealed class FileEntry
        {
            public string Name     { get; }
            public string FullPath { get; }
            public bool   IsDirectory { get; }
            public long   SizeBytes { get; }
            public string IconGlyph => IsDirectory ? "" : ""; // Folder / File
            public string Meta      => IsDirectory ? "folder" : FormatSize(SizeBytes);

            public FileEntry(string name, string fullPath, bool isDirectory, long sizeBytes)
            {
                Name = name; FullPath = fullPath; IsDirectory = isDirectory; SizeBytes = sizeBytes;
            }

            private static string FormatSize(long bytes) => bytes switch
            {
                < 1024            => $"{bytes} B",
                < 1024 * 1024     => $"{bytes / 1024.0:F1} KB",
                < 1024L * 1024 * 1024 => $"{bytes / (1024.0 * 1024):F1} MB",
                _                 => $"{bytes / (1024.0 * 1024 * 1024):F2} GB"
            };
        }
    }
}
