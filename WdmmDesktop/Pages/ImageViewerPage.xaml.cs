using System;
using System.Collections.Generic;
using System.Linq;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media.Imaging;
using Microsoft.UI.Xaml.Navigation;
using WdmmDesktop.Models;
using WdmmDesktop.Services;

namespace WdmmDesktop.Pages
{
    /// <summary>
    /// Standalone image viewer.  Navigated from NetworkPage with an
    /// <see cref="ImageViewerArgs"/> parameter that carries the account id,
    /// sibling entries list, and the index of the entry to show first.
    /// Allows sliding through all image siblings in the same directory.
    /// </summary>
    public sealed partial class ImageViewerPage : Page
    {
        // Image file extensions recognised by the viewer.
        private static readonly HashSet<string> ImageExts = new(StringComparer.OrdinalIgnoreCase)
        {
            ".jpg", ".jpeg", ".png", ".webp", ".gif", ".bmp", ".tiff", ".avif", ".heic",
        };

        private string _accountId = "";
        private List<RemoteEntry> _images = new();
        private int _current = 0;

        public ImageViewerPage() => InitializeComponent();

        protected override void OnNavigatedTo(NavigationEventArgs e)
        {
            base.OnNavigatedTo(e);
            if (e.Parameter is not ImageViewerArgs args) return;

            _accountId = args.AccountId;
            // Filter sibling list to image entries only; keep stable order.
            _images = args.Siblings
                .Where(s => !s.IsDir && ImageExts.Contains(System.IO.Path.GetExtension(s.Name)))
                .ToList();
            _current = Math.Clamp(args.StartIndex, 0, Math.Max(0, _images.Count - 1));

            if (_images.Count == 0)
            {
                ShowStatus("No images in this directory.");
                return;
            }

            ShowImage(_current);
        }

        // ── Navigation ────────────────────────────────────────────────────────

        private void PrevBtn_Click(object sender, RoutedEventArgs e)
        {
            if (_images.Count == 0) return;
            _current = (_current - 1 + _images.Count) % _images.Count;
            ShowImage(_current);
        }

        private void NextBtn_Click(object sender, RoutedEventArgs e)
        {
            if (_images.Count == 0) return;
            _current = (_current + 1) % _images.Count;
            ShowImage(_current);
        }

        private void BackBtn_Click(object sender, RoutedEventArgs e)
            => Frame.GoBack();

        // ── Image loading ─────────────────────────────────────────────────────

        private async void ShowImage(int index)
        {
            if (index < 0 || index >= _images.Count) return;
            var entry = _images[index];

            TitleText.Text = entry.Name;
            IndexText.Text = $"{index + 1} / {_images.Count}";
            PathHint.Text = entry.Path;
            PrevBtn.IsEnabled = _images.Count > 1;
            NextBtn.IsEnabled = _images.Count > 1;

            // Reset
            MainImage.Source = null;
            MainImage.Visibility = Visibility.Collapsed;
            ShowStatus("", loading: true);

            string? localPath = null;
            string? rawUrl = null;

            // Try to get cached path or raw URL via AccountGetEntry.
            if (!string.IsNullOrEmpty(_accountId))
            {
                try
                {
                    var item = await System.Threading.Tasks.Task.Run(
                        () => CloudFfi.AccountGetEntry(_accountId, entry.Path));
                    if (item != null)
                    {
                        if (!string.IsNullOrEmpty(item.LocalPath)) localPath = item.LocalPath;
                        if (!string.IsNullOrEmpty(item.RawUrl))    rawUrl    = item.RawUrl;
                    }
                }
                catch { /* best-effort */ }
            }

            Uri? imageUri = null;
            if (!string.IsNullOrEmpty(localPath) && System.IO.File.Exists(localPath))
                imageUri = new Uri(localPath);
            else if (!string.IsNullOrEmpty(rawUrl)
                     && (rawUrl.StartsWith("http://", StringComparison.OrdinalIgnoreCase)
                      || rawUrl.StartsWith("https://", StringComparison.OrdinalIgnoreCase)))
                imageUri = new Uri(rawUrl);

            if (imageUri == null)
            {
                ShowStatus("Image not cached locally and no direct URL available.\nCache the file via Download first.");
                return;
            }

            try
            {
                var bmp = new BitmapImage(imageUri);
                MainImage.Source = bmp;
                MainImage.Visibility = Visibility.Visible;
                StatusPanel.Visibility = Visibility.Collapsed;
                // Reset zoom when changing image
                ImageScroll.ChangeView(null, null, 1.0f);
            }
            catch (Exception ex)
            {
                ShowStatus($"Could not load image: {ex.Message}");
            }
        }

        private void ShowStatus(string message, bool loading = false)
        {
            StatusPanel.Visibility = Visibility.Visible;
            LoadingRing.IsActive   = loading;
            LoadingRing.Visibility = loading ? Visibility.Visible : Visibility.Collapsed;
            StatusText.Text = message;
            if (!loading && !string.IsNullOrEmpty(message))
                MainImage.Visibility = Visibility.Collapsed;
        }
    }

    /// <summary>Navigation parameter passed from NetworkPage to ImageViewerPage.</summary>
    public sealed class ImageViewerArgs
    {
        /// <summary>Account ID from <see cref="WebDavAccount.Id"/> — used for AccountGetEntry.</summary>
        public required string AccountId { get; init; }
        /// <summary>All entries in the current directory (including non-images).</summary>
        public required List<RemoteEntry> Siblings { get; init; }
        /// <summary>Index within <see cref="Siblings"/> of the entry the user clicked.</summary>
        public required int StartIndex { get; init; }
    }
}
