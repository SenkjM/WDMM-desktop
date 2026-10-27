using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Input;
using Microsoft.UI.Xaml.Navigation;
using WdmmDesktop.Dialogs;
using WdmmDesktop.Generated;
using WdmmDesktop.Models;
using WdmmDesktop.Services;

namespace WdmmDesktop.Pages
{
    /// <summary>
    /// 文件源 directory browser — multi-provider via CloudFfi.Account* (Core account FFI).
    /// Wave 1: ItemClick replaces DoubleTapped; SelectionMode=Extended for multi-select.
    /// Wave 2: File dispatch without play (audio/video→queue, image→viewer, manga→upsert).
    /// Wave 7: Batch download/delete/manga-import; upload via QueueEnqueue upload_file.
    /// Wave 9: Cast via CastDialog accepting a cloud file path.
    /// </summary>
    public sealed partial class NetworkPage : Page
    {
        private List<WebDavAccount> _sources = new();
        private WebDavAccount? _current;
        private readonly List<string> _stack = new() { "/" };
        // Cached current directory entries for ImageViewer siblings
        private List<RemoteEntry> _currentEntries = new();

        // ── File type helpers ─────────────────────────────────────────────────

        private static readonly HashSet<string> AudioExts = new(StringComparer.OrdinalIgnoreCase)
        {
            ".mp3", ".flac", ".ogg", ".m4a", ".aac", ".wav", ".opus", ".wma", ".alac", ".ape",
        };
        private static readonly HashSet<string> VideoExts = new(StringComparer.OrdinalIgnoreCase)
        {
            ".mp4", ".mkv", ".avi", ".mov", ".wmv", ".webm", ".ts", ".m2ts", ".flv", ".rm",
        };
        private static readonly HashSet<string> ImageExts = new(StringComparer.OrdinalIgnoreCase)
        {
            ".jpg", ".jpeg", ".png", ".webp", ".gif", ".bmp", ".tiff", ".avif", ".heic",
        };
        private static readonly HashSet<string> MangaExts = new(StringComparer.OrdinalIgnoreCase)
        {
            ".cbz", ".cbr", ".zip",
        };

        private static string FileKind(string name)
        {
            var ext = System.IO.Path.GetExtension(name);
            if (AudioExts.Contains(ext)) return "audio";
            if (VideoExts.Contains(ext)) return "video";
            if (ImageExts.Contains(ext)) return "image";
            if (MangaExts.Contains(ext)) return "manga";
            return "other";
        }

        public NetworkPage() => InitializeComponent();

        protected override void OnNavigatedTo(NavigationEventArgs e)
        {
            base.OnNavigatedTo(e);
            ApplyLoc();
            ReloadSources(selectFirst: true);
        }

        void ApplyLoc()
        {
            LblSource.Text       = Loc.Get("Network.Sources");
            ManageBtn.Content    = Loc.Get("Network.ManageSources");
            UpBtn.Content        = Loc.Get("Network.Up");
            RefreshBtn.Content   = Loc.Get("Network.Refresh");
            NewFolderBtn.Content = Loc.Get("Network.NewFolder");
            UploadBtn.Content    = Loc.Get("Network.Upload");
            EmptyText.Text       = Loc.Get("Network.Empty");
            RetryBtn.Content     = Loc.Get("Network.Retry");
            CtxRename.Text       = Loc.Get("Network.Rename");
            CtxDelete.Text       = Loc.Get("Network.Delete");
            CtxDownload.Text     = Loc.Get("Network.Download");
            CtxViewImage.Text    = Loc.Get("Network.ViewImage");
            CtxAddManga.Text     = Loc.Get("Network.AddToManga");
            CtxCast.Text         = Loc.Get("Network.Cast");
            SelectAllBtn.Content      = Loc.Get("Network.SelectAll");
            BatchDownloadBtn.Content  = Loc.Get("Network.BatchDownload");
            BatchAddMangaBtn.Content  = Loc.Get("Network.BatchAddManga");
            BatchDeleteBtn.Content    = Loc.Get("Network.BatchDelete");
        }

        string CurrentPath => _stack.Count == 0 ? "/" : _stack[^1];

        void ReloadSources(bool selectFirst)
        {
            _sources = AccountStore.List();
            SourceCombo.Items.Clear();
            foreach (var s in _sources)
                SourceCombo.Items.Add(s.DisplayName);

            // Show upload button only for webdav accounts (write assumed).
            UpdateUploadVisibility();

            if (_sources.Count == 0)
            {
                _current = null;
                EntryList.ItemsSource = null;
                ShowEmpty(Loc.Get("Network.NoSources"), showRetry: false);
                return;
            }

            if (selectFirst || SourceCombo.SelectedIndex < 0)
                SourceCombo.SelectedIndex = 0;
        }

        void UpdateUploadVisibility()
        {
            var canUpload = _current != null
                && string.Equals(_current.ProviderType, "webdav", StringComparison.OrdinalIgnoreCase);
            UploadBtn.Visibility = canUpload ? Visibility.Visible : Visibility.Collapsed;
        }

        void SourceCombo_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            var i = SourceCombo.SelectedIndex;
            if (i < 0 || i >= _sources.Count) return;
            _current = _sources[i];
            _stack.Clear();
            var root = string.IsNullOrWhiteSpace(_current.RemoteDir) ? "/" : _current.RemoteDir.TrimEnd('/');
            if (string.IsNullOrEmpty(root)) root = "/";
            if (!root.StartsWith('/')) root = "/" + root;

            try
            {
                if (NativeMethods.SettingsGetNetworkRememberLastPath())
                {
                    var last = NativeMethods.SettingsGetNetworkLastPath();
                    if (!string.IsNullOrEmpty(last) && last.StartsWith(root, StringComparison.Ordinal))
                        root = last;
                }
            }
            catch { }

            _stack.Add(root);
            UpdateUploadVisibility();
            LoadDirAsync();
        }

        async void LoadDirAsync()
        {
            if (_current == null) return;
            SetBusy(true);
            PathText.Text = CurrentPath;
            UpBtn.IsEnabled = CurrentPath != "/" && CurrentPath.TrimEnd('/') != (_current.RemoteDir?.TrimEnd('/') ?? "");

            List<RemoteEntry> entries;
            try
            {
                if (!string.IsNullOrEmpty(_current.Id))
                {
                    var items = await System.Threading.Tasks.Task.Run(() =>
                        CloudFfi.AccountListDir(_current.Id, CurrentPath));
                    entries = items.Select(i => new RemoteEntry
                    {
                        Name     = i.Name ?? "",
                        Path     = i.Path ?? "",
                        IsDir    = i.IsDir,
                        Size     = i.Size,
                        Modified = i.Modified
                    }).ToList();
                }
                else
                {
                    var json = await System.Threading.Tasks.Task.Run(() =>
                        NativeMethods.WithNativeWebDavList(
                            _current.Url, _current.Username, _current.Password, CurrentPath));

                    if (json.StartsWith("error:", StringComparison.Ordinal))
                    {
                        SetBusy(false);
                        ShowEmpty(json, showRetry: true);
                        return;
                    }

                    entries = new List<RemoteEntry>();
                    using var doc = JsonDocument.Parse(json);
                    foreach (var el in doc.RootElement.EnumerateArray())
                    {
                        entries.Add(new RemoteEntry
                        {
                            Name     = el.TryGetProperty("name",     out var n) ? n.GetString() ?? "" : "",
                            Path     = el.TryGetProperty("path",     out var p) ? p.GetString() ?? "" : "",
                            IsDir    = el.TryGetProperty("is_dir",   out var d) && d.GetBoolean(),
                            Size     = el.TryGetProperty("size",     out var s) && s.TryGetUInt64(out var sz) ? sz : 0,
                            Modified = el.TryGetProperty("modified", out var m) && m.ValueKind != JsonValueKind.Null ? m.GetString() : null,
                        });
                    }
                }
            }
            catch (Exception ex)
            {
                SetBusy(false);
                ShowEmpty(ex.Message, showRetry: true);
                return;
            }

            SetBusy(false);
            entries = entries
                .OrderByDescending(x => x.IsDir)
                .ThenBy(x => x.Name, StringComparer.OrdinalIgnoreCase)
                .ToList();

            _currentEntries = entries;
            EntryList.ItemsSource = entries;

            if (entries.Count == 0)
                ShowEmpty(Loc.Get("Network.Empty"), showRetry: false);
            else
            {
                EmptyPanel.Visibility  = Visibility.Collapsed;
                EntryList.Visibility   = Visibility.Visible;
            }

            try { NativeMethods.SettingsSetNetworkLastPath(CurrentPath); } catch { }
        }

        void ShowEmpty(string msg, bool showRetry)
        {
            EntryList.Visibility   = Visibility.Collapsed;
            EmptyPanel.Visibility  = Visibility.Visible;
            EmptyText.Text         = msg;
            RetryBtn.Visibility    = showRetry ? Visibility.Visible : Visibility.Collapsed;
        }

        void SetBusy(bool on)
        {
            BusyRing.IsActive      = on;
            BusyRing.Visibility    = on ? Visibility.Visible : Visibility.Collapsed;
            if (on)
            {
                EmptyPanel.Visibility = Visibility.Collapsed;
                EntryList.Visibility  = Visibility.Collapsed;
            }
        }

        // ── Navigation ─────────────────────────────────────────────────────────

        void UpBtn_Click(object sender, RoutedEventArgs e)
        {
            if (_stack.Count <= 1) return;
            _stack.RemoveAt(_stack.Count - 1);
            LoadDirAsync();
        }

        void RefreshBtn_Click(object sender, RoutedEventArgs e) => LoadDirAsync();

        // ── ItemClick (Wave 1) ────────────────────────────────────────────────

        /// <summary>
        /// Single-click / Enter on an entry.
        /// Directories: navigate into.  Files: dispatch by type (Wave 2).
        /// </summary>
        private async void EntryList_ItemClick(object sender, ItemClickEventArgs e)
        {
            if (e.ClickedItem is not RemoteEntry ent) return;

            if (ent.IsDir)
            {
                var next = string.IsNullOrEmpty(ent.Path) ? Combine(CurrentPath, ent.Name) : ent.Path;
                _stack.Add(next);
                LoadDirAsync();
                return;
            }

            await DispatchFileAction(ent);
        }

        // ── Selection + batch toolbar (Wave 7) ───────────────────────────────

        private void EntryList_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            var count = EntryList.SelectedItems.Count;
            if (count > 0)
            {
                BatchToolbar.Visibility    = Visibility.Visible;
                SelectionCountText.Text    = $"{count} 项已选";
            }
            else
            {
                BatchToolbar.Visibility    = Visibility.Collapsed;
                SelectionCountText.Text    = "";
            }
        }

        private void SelectAllBtn_Click(object sender, RoutedEventArgs e)
            => EntryList.SelectAll();

        private void SelectAllAccelerator_Invoked(KeyboardAccelerator sender, KeyboardAcceleratorInvokedEventArgs args)
        {
            EntryList.SelectAll();
            args.Handled = true;
        }

        private async void BatchDownloadBtn_Click(object sender, RoutedEventArgs e)
        {
            var sel = EntryList.SelectedItems.OfType<RemoteEntry>().Where(x => !x.IsDir).ToList();
            if (sel.Count == 0) return;
            int ok = 0;
            foreach (var ent in sel)
            {
                if (EnqueueDownload(ent)) ok++;
            }
            SetActionStatus($"{ok} 项已加入下载队列");
            await System.Threading.Tasks.Task.Delay(100); // allow UI tick
        }

        private async void BatchAddMangaBtn_Click(object sender, RoutedEventArgs e)
        {
            var sel = EntryList.SelectedItems.OfType<RemoteEntry>()
                .Where(x => !x.IsDir && MangaExts.Contains(System.IO.Path.GetExtension(x.Name)))
                .ToList();
            if (sel.Count == 0)
            {
                SetActionStatus("所选文件中无漫画文件（.cbz/.cbr/.zip）");
                return;
            }
            int ok = 0;
            foreach (var ent in sel)
            {
                if (MangaUpsertEntry(ent)) ok++;
            }
            SetActionStatus($"{ok} 个漫画文件已入库");
            await System.Threading.Tasks.Task.Delay(100);
        }

        private async void BatchDeleteBtn_Click(object sender, RoutedEventArgs e)
        {
            var sel = EntryList.SelectedItems.OfType<RemoteEntry>().ToList();
            if (sel.Count == 0) return;
            if (_current == null) return;

            var dlg = new ContentDialog
            {
                Title           = Loc.Get("Network.Delete"),
                Content         = $"删除 {sel.Count} 项？",
                PrimaryButtonText = Loc.Get("Network.Delete"),
                CloseButtonText  = "Cancel",
                XamlRoot         = XamlRoot
            };
            if (await dlg.ShowAsync() != ContentDialogResult.Primary) return;

            int ok = 0;
            foreach (var ent in sel)
            {
                try
                {
                    if (!string.IsNullOrEmpty(_current.Id))
                        CloudFfi.AccountRemove(_current.Id, ent.Path);
                    else
                    {
                        var r = NativeMethods.WithNativeWebDavRemove(
                            _current.Url, _current.Username, _current.Password, ent.Path);
                        if (r.StartsWith("error:", StringComparison.Ordinal))
                            continue;
                    }
                    ok++;
                }
                catch { }
            }
            SetActionStatus($"已删除 {ok} 项");
            LoadDirAsync();
        }

        // ── Upload (Wave 7) ───────────────────────────────────────────────────

        private async void UploadBtn_Click(object sender, RoutedEventArgs e)
        {
            if (_current == null) return;

            var picker = new Windows.Storage.Pickers.FileOpenPicker();
            picker.SuggestedStartLocation = Windows.Storage.Pickers.PickerLocationId.Downloads;
            picker.FileTypeFilter.Add("*");

            // WinUI 3: initialise picker with window handle.
            var hwnd = WinRT.Interop.WindowNative.GetWindowHandle(App.MainWindow);
            WinRT.Interop.InitializeWithWindow.Initialize(picker, hwnd);

            var files = await picker.PickMultipleFilesAsync();
            if (files == null || files.Count == 0) return;

            int ok = 0;
            foreach (var file in files)
            {
                var remotePath = Combine(CurrentPath, file.Name);
                var task = new
                {
                    id          = Guid.NewGuid().ToString(),
                    kind        = "upload_file",
                    local_path  = file.Path,
                    remote_path = remotePath,
                    total_bytes = 0L,
                };
                var json = System.Text.Json.JsonSerializer.Serialize(task);
                var result = NativeMethods.QueueEnqueue(json);
                if (!result.StartsWith("error:", StringComparison.Ordinal)) ok++;
            }
            SetActionStatus($"{ok} 个文件已加入上传队列");
        }

        // ── File action dispatch (Wave 2) ─────────────────────────────────────

        private async System.Threading.Tasks.Task DispatchFileAction(RemoteEntry ent)
        {
            var kind = FileKind(ent.Name);
            switch (kind)
            {
                case "audio":
                    EnqueueDownload(ent);
                    SetActionStatus($"已加入下载队列：{ent.Name}");
                    break;

                case "video":
                    EnqueueDownload(ent);
                    SetActionStatus($"已加入下载队列：{ent.Name}");
                    break;

                case "image":
                    OpenImageViewer(ent);
                    break;

                case "manga":
                    if (MangaUpsertEntry(ent))
                        SetActionStatus($"漫画已入库：{ent.Name}");
                    else
                        SetActionStatus($"入库失败：{ent.Name}");
                    break;

                default:
                    EnqueueDownload(ent);
                    SetActionStatus($"已加入下载队列：{ent.Name}");
                    break;
            }

            await System.Threading.Tasks.Task.CompletedTask;
        }

        private bool EnqueueDownload(RemoteEntry ent)
        {
            try
            {
                var task = new
                {
                    id          = Guid.NewGuid().ToString(),
                    kind        = "download_track",
                    remote_path = ent.Path,
                    total_bytes = (long)ent.Size,
                };
                var result = NativeMethods.QueueEnqueue(System.Text.Json.JsonSerializer.Serialize(task));
                return !result.StartsWith("error:", StringComparison.Ordinal);
            }
            catch { return false; }
        }

        private bool MangaUpsertEntry(RemoteEntry ent)
        {
            try
            {
                var title = System.IO.Path.GetFileNameWithoutExtension(ent.Name);
                var ext   = System.IO.Path.GetExtension(ent.Name).TrimStart('.').ToLowerInvariant();
                var manga = new
                {
                    id          = Guid.NewGuid().ToString(),
                    title       = title,
                    source_name = _current?.DisplayName ?? "",
                    remote_path = ent.Path,
                    format      = ext,
                };
                var result = NativeMethods.MangaUpsert(System.Text.Json.JsonSerializer.Serialize(manga));
                return !result.StartsWith("error:", StringComparison.Ordinal);
            }
            catch { return false; }
        }

        private void OpenImageViewer(RemoteEntry ent)
        {
            var idx = _currentEntries.IndexOf(ent);
            if (idx < 0) idx = 0;
            Frame.Navigate(typeof(ImageViewerPage), new ImageViewerArgs
            {
                AccountId  = _current?.Id ?? "",
                Siblings   = _currentEntries,
                StartIndex = idx,
            });
        }

        // ── Context menu (Wave 1/2/9) ─────────────────────────────────────────

        private void ContextMenu_Opening(object sender, object e)
        {
            var ent = EntryList.SelectedItem as RemoteEntry;
            if (ent == null) return;

            var kind = ent.IsDir ? "dir" : FileKind(ent.Name);

            // Show/hide items based on entry type
            CtxDownload.Visibility  = (!ent.IsDir)                ? Visibility.Visible : Visibility.Collapsed;
            CtxViewImage.Visibility = (kind == "image")           ? Visibility.Visible : Visibility.Collapsed;
            CtxAddManga.Visibility  = (kind == "manga")           ? Visibility.Visible : Visibility.Collapsed;
            CtxCast.Visibility      = (!ent.IsDir && kind != "image" && kind != "manga")
                                                                   ? Visibility.Visible : Visibility.Collapsed;
        }

        private async void CtxDownload_Click(object sender, RoutedEventArgs e)
        {
            if (EntryList.SelectedItem is not RemoteEntry ent || ent.IsDir) return;
            if (EnqueueDownload(ent))
                SetActionStatus($"已加入下载队列：{ent.Name}");
        }

        private void CtxViewImage_Click(object sender, RoutedEventArgs e)
        {
            if (EntryList.SelectedItem is not RemoteEntry ent) return;
            OpenImageViewer(ent);
        }

        private void CtxAddManga_Click(object sender, RoutedEventArgs e)
        {
            if (EntryList.SelectedItem is not RemoteEntry ent || ent.IsDir) return;
            if (MangaUpsertEntry(ent))
                SetActionStatus($"漫画已入库：{ent.Name}");
            else
                SetActionStatus($"入库失败：{ent.Name}");
        }

        private async void CtxCast_Click(object sender, RoutedEventArgs e)
        {
            if (EntryList.SelectedItem is not RemoteEntry ent || ent.IsDir) return;
            var dialog = new CastDialog(cloudFilePath: ent.Path, displayName: ent.Name)
            {
                XamlRoot = XamlRoot
            };
            await dialog.ShowAsync();
        }

        private async void CtxRename_Click(object sender, RoutedEventArgs e)
        {
            if (_current == null || EntryList.SelectedItem is not RemoteEntry ent) return;
            var box = new TextBox { Text = ent.Name };
            var dlg = new ContentDialog
            {
                Title             = Loc.Get("Network.Rename"),
                Content           = box,
                PrimaryButtonText = "OK",
                CloseButtonText   = "Cancel",
                XamlRoot          = XamlRoot
            };
            if (await dlg.ShowAsync() != ContentDialogResult.Primary) return;
            var name = (box.Text ?? "").Trim();
            if (string.IsNullOrEmpty(name) || name == ent.Name) return;
            var newPath = Combine(CurrentPath, name);

            try
            {
                if (!string.IsNullOrEmpty(_current.Id))
                    CloudFfi.AccountRename(_current.Id, ent.Path, newPath);
                else
                {
                    var r = NativeMethods.WithNativeWebDavRename(
                        _current.Url, _current.Username, _current.Password, ent.Path, newPath);
                    if (r.StartsWith("error:", StringComparison.Ordinal))
                        throw new Exception(r[6..]);
                }
                LoadDirAsync();
            }
            catch (Exception ex) { ShowEmpty(ex.Message, showRetry: true); }
        }

        private async void CtxDelete_Click(object sender, RoutedEventArgs e)
        {
            if (_current == null || EntryList.SelectedItem is not RemoteEntry ent) return;
            var dlg = new ContentDialog
            {
                Title             = Loc.Get("Network.Delete"),
                Content           = ent.Name,
                PrimaryButtonText = Loc.Get("Network.Delete"),
                CloseButtonText   = "Cancel",
                XamlRoot          = XamlRoot
            };
            if (await dlg.ShowAsync() != ContentDialogResult.Primary) return;

            try
            {
                if (!string.IsNullOrEmpty(_current.Id))
                    CloudFfi.AccountRemove(_current.Id, ent.Path);
                else
                {
                    var r = NativeMethods.WithNativeWebDavRemove(
                        _current.Url, _current.Username, _current.Password, ent.Path);
                    if (r.StartsWith("error:", StringComparison.Ordinal))
                        throw new Exception(r[6..]);
                }
                LoadDirAsync();
            }
            catch (Exception ex) { ShowEmpty(ex.Message, showRetry: true); }
        }

        // ── New folder ────────────────────────────────────────────────────────

        private async void NewFolderBtn_Click(object sender, RoutedEventArgs e)
        {
            if (_current == null) return;
            var box = new TextBox { PlaceholderText = "name" };
            var dlg = new ContentDialog
            {
                Title             = Loc.Get("Network.NewFolder"),
                Content           = box,
                PrimaryButtonText = "OK",
                CloseButtonText   = "Cancel",
                XamlRoot          = XamlRoot
            };
            if (await dlg.ShowAsync() != ContentDialogResult.Primary) return;
            var name = (box.Text ?? "").Trim();
            if (string.IsNullOrEmpty(name)) return;
            var path = Combine(CurrentPath, name);

            try
            {
                if (!string.IsNullOrEmpty(_current.Id))
                    CloudFfi.AccountMkdir(_current.Id, path);
                else
                {
                    var r = NativeMethods.WithNativeWebDavMkdir(
                        _current.Url, _current.Username, _current.Password, path);
                    if (r.StartsWith("error:", StringComparison.Ordinal))
                        throw new Exception(r[6..]);
                }
                LoadDirAsync();
            }
            catch (Exception ex) { ShowEmpty(ex.Message, showRetry: true); }
        }

        // ── Manage sources dialog ─────────────────────────────────────────────

        async void ManageBtn_Click(object sender, RoutedEventArgs e)
        {
            var panel = new StackPanel { Spacing = 8, MinWidth = 480 };
            var list  = new ListView { Height = 180 };
            void RefreshList()
            {
                list.Items.Clear();
                foreach (var s in AccountStore.List())
                    list.Items.Add($"{s.DisplayName}  ·  {s.ProviderType}");
            }
            RefreshList();
            panel.Children.Add(list);

            var nameBox     = new TextBox { PlaceholderText = "display name" };
            var providerCombo = new ComboBox { PlaceholderText = "provider", HorizontalAlignment = HorizontalAlignment.Stretch };
            providerCombo.Items.Add("webdav");
            providerCombo.Items.Add("baidu_netdisk");
            providerCombo.Items.Add("123_open");
            providerCombo.Items.Add("115open");
            providerCombo.Items.Add("aliyundrive_open");
            providerCombo.Items.Add("terabox");
            providerCombo.Items.Add("netease_music");
            providerCombo.Items.Add("ctfile");
            providerCombo.Items.Add("crypt");
            providerCombo.SelectedIndex = 0;

            panel.Children.Add(nameBox);
            panel.Children.Add(providerCombo);

            var credPanel = new StackPanel { Spacing = 6 };
            panel.Children.Add(credPanel);

            var urlBox            = new TextBox    { PlaceholderText = "https://nas/dav",         Tag = "base_url" };
            var userBox           = new TextBox    { PlaceholderText = "username",                Tag = "username" };
            var passBox           = new PasswordBox {                                              Tag = "password" };
            var remoteBox         = new TextBox    { PlaceholderText = "/", Text = "/",           Tag = "remote_dir" };
            var cookieBox         = new TextBox    { PlaceholderText = "cookie",                  Tag = "cookie",         Visibility = Visibility.Collapsed };
            var accessTokenBox    = new TextBox    { PlaceholderText = "access_token",            Tag = "access_token",   Visibility = Visibility.Collapsed };
            var driveIdBox        = new TextBox    { PlaceholderText = "drive_id",                Tag = "drive_id",       Visibility = Visibility.Collapsed };
            var sessionBox        = new TextBox    { PlaceholderText = "session",                 Tag = "session",        Visibility = Visibility.Collapsed };
            var cryptPassBox      = new PasswordBox { PlaceholderText = "crypt password",         Tag = "crypt_password", Visibility = Visibility.Collapsed };
            var sourceAccountBox  = new TextBox    { PlaceholderText = "source account name",     Tag = "source_account", Visibility = Visibility.Collapsed };

            credPanel.Children.Add(urlBox);
            credPanel.Children.Add(userBox);
            credPanel.Children.Add(passBox);
            credPanel.Children.Add(remoteBox);
            credPanel.Children.Add(cookieBox);
            credPanel.Children.Add(accessTokenBox);
            credPanel.Children.Add(driveIdBox);
            credPanel.Children.Add(sessionBox);
            credPanel.Children.Add(cryptPassBox);
            credPanel.Children.Add(sourceAccountBox);

            void UpdateFieldsForProvider(string provider)
            {
                urlBox.Visibility           = Visibility.Collapsed;
                userBox.Visibility          = Visibility.Collapsed;
                passBox.Visibility          = Visibility.Collapsed;
                remoteBox.Visibility        = Visibility.Collapsed;
                cookieBox.Visibility        = Visibility.Collapsed;
                accessTokenBox.Visibility   = Visibility.Collapsed;
                driveIdBox.Visibility       = Visibility.Collapsed;
                sessionBox.Visibility       = Visibility.Collapsed;
                cryptPassBox.Visibility     = Visibility.Collapsed;
                sourceAccountBox.Visibility = Visibility.Collapsed;

                switch (provider)
                {
                    case "webdav":
                        urlBox.Visibility    = Visibility.Visible;
                        userBox.Visibility   = Visibility.Visible;
                        passBox.Visibility   = Visibility.Visible;
                        remoteBox.Visibility = Visibility.Visible;
                        break;
                    case "baidu_netdisk":
                    case "terabox":
                    case "115open":
                    case "ctfile":
                    case "netease_music":
                        cookieBox.Visibility = Visibility.Visible;
                        break;
                    case "aliyundrive_open":
                        accessTokenBox.Visibility = Visibility.Visible;
                        driveIdBox.Visibility     = Visibility.Visible;
                        break;
                    case "123_open":
                        sessionBox.Visibility = Visibility.Visible;
                        break;
                    case "crypt":
                        cryptPassBox.Visibility     = Visibility.Visible;
                        sourceAccountBox.Visibility = Visibility.Visible;
                        break;
                }
            }

            providerCombo.SelectionChanged += (_, __) =>
                UpdateFieldsForProvider(providerCombo.SelectedItem?.ToString() ?? "webdav");

            UpdateFieldsForProvider("webdav");

            var row    = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 6 };
            var saveBtn = new Button { Content = "Save" };
            var delBtn  = new Button { Content = "Delete" };
            row.Children.Add(saveBtn);
            row.Children.Add(delBtn);
            panel.Children.Add(row);
            var status = new TextBlock { TextWrapping = TextWrapping.Wrap, FontSize = 12 };
            panel.Children.Add(status);

            WebDavAccount? editing = null;
            list.SelectionChanged += (_, __) =>
            {
                var idx = list.SelectedIndex;
                var all = AccountStore.List();
                if (idx < 0 || idx >= all.Count) { editing = null; return; }
                editing = all[idx];
                nameBox.Text = editing.DisplayNameField;
                providerCombo.SelectedItem = editing.ProviderType;
                UpdateFieldsForProvider(editing.ProviderType);
                urlBox.Text = editing.Url;
                if (editing.Credentials.TryGetValue("username",     out var u)  && u.ValueKind  == JsonValueKind.String) userBox.Text            = u.GetString() ?? "";
                if (editing.Credentials.TryGetValue("password",     out var p)  && p.ValueKind  == JsonValueKind.String) passBox.Password         = p.GetString() ?? "";
                if (editing.Credentials.TryGetValue("remote_dir",   out var r2) && r2.ValueKind == JsonValueKind.String) remoteBox.Text           = r2.GetString() ?? "/";
                if (editing.Credentials.TryGetValue("cookie",       out var c)  && c.ValueKind  == JsonValueKind.String) cookieBox.Text           = c.GetString() ?? "";
                if (editing.Credentials.TryGetValue("access_token", out var at) && at.ValueKind == JsonValueKind.String) accessTokenBox.Text      = at.GetString() ?? "";
                if (editing.Credentials.TryGetValue("drive_id",     out var di) && di.ValueKind == JsonValueKind.String) driveIdBox.Text          = di.GetString() ?? "";
                if (editing.Credentials.TryGetValue("session",      out var s2) && s2.ValueKind == JsonValueKind.String) sessionBox.Text          = s2.GetString() ?? "";
                if (editing.ProviderType == "crypt" && editing.Credentials.TryGetValue("password", out var cp) && cp.ValueKind == JsonValueKind.String)
                    cryptPassBox.Password = cp.GetString() ?? "";
                if (editing.Credentials.TryGetValue("source", out var sa) && sa.ValueKind == JsonValueKind.Object && sa.TryGetProperty("display_name", out var dn))
                    sourceAccountBox.Text = dn.GetString() ?? "";
            };

            saveBtn.Click += (_, __) =>
            {
                var a        = editing ?? new WebDavAccount();
                a.DisplayNameField = (nameBox.Text ?? "").Trim();
                var provider = providerCombo.SelectedItem?.ToString() ?? "webdav";
                a.ProviderType = provider;
                a.Url = (urlBox.Text ?? "").Trim();
                a.Credentials.Clear();
                switch (provider)
                {
                    case "webdav":
                        a.Credentials["username"]   = JsonDocument.Parse($"\"{userBox.Text ?? ""}\"").RootElement;
                        a.Credentials["password"]   = JsonDocument.Parse($"\"{passBox.Password ?? ""}\"").RootElement;
                        a.Credentials["remote_dir"] = JsonDocument.Parse($"\"{(string.IsNullOrWhiteSpace(remoteBox.Text) ? "/" : remoteBox.Text.Trim())}\"").RootElement;
                        break;
                    case "baidu_netdisk":
                    case "terabox":
                    case "115open":
                    case "ctfile":
                    case "netease_music":
                        a.Credentials["cookie"] = JsonDocument.Parse($"\"{cookieBox.Text ?? ""}\"").RootElement;
                        break;
                    case "aliyundrive_open":
                        a.Credentials["access_token"] = JsonDocument.Parse($"\"{accessTokenBox.Text ?? ""}\"").RootElement;
                        a.Credentials["drive_id"]     = JsonDocument.Parse($"\"{driveIdBox.Text ?? ""}\"").RootElement;
                        break;
                    case "123_open":
                        a.Credentials["session"] = JsonDocument.Parse($"\"{sessionBox.Text ?? ""}\"").RootElement;
                        break;
                    case "crypt":
                        a.Credentials["password"] = JsonDocument.Parse($"\"{cryptPassBox.Password ?? ""}\"").RootElement;
                        var sourceName = sourceAccountBox.Text ?? "";
                        if (!string.IsNullOrEmpty(sourceName))
                        {
                            var sourceAcct = AccountStore.List().FirstOrDefault(x => x.DisplayNameField == sourceName);
                            if (sourceAcct != null)
                            {
                                using var stream = new System.IO.MemoryStream();
                                using var writer = new System.Text.Json.Utf8JsonWriter(stream);
                                writer.WriteStartObject();
                                writer.WriteString("provider_type", sourceAcct.ProviderType);
                                writer.WritePropertyName("config");
                                writer.WriteStartObject();
                                foreach (var (key, value) in sourceAcct.Credentials)
                                {
                                    writer.WritePropertyName(key);
                                    value.WriteTo(writer);
                                }
                                writer.WriteEndObject();
                                writer.WriteEndObject();
                                writer.Flush();
                                var sourceJson = System.Text.Encoding.UTF8.GetString(stream.ToArray());
                                a.Credentials["source"] = JsonDocument.Parse(sourceJson).RootElement;
                            }
                        }
                        break;
                }

                var res = AccountStore.Upsert(a);
                status.Text = res.StartsWith("error:", StringComparison.Ordinal) ? res : "ok";
                if (!res.StartsWith("error:", StringComparison.Ordinal))
                {
                    editing = null;
                    RefreshList();
                    ReloadSources(selectFirst: false);
                }
            };

            delBtn.Click += (_, __) =>
            {
                if (editing == null || string.IsNullOrEmpty(editing.Id)) return;
                var res = AccountStore.Delete(editing.Id);
                status.Text = res;
                editing = null;
                RefreshList();
                ReloadSources(selectFirst: true);
            };

            var dlg = new ContentDialog
            {
                Title          = Loc.Get("Network.ManageSources"),
                Content        = panel,
                CloseButtonText = "Close",
                XamlRoot       = XamlRoot
            };
            await dlg.ShowAsync();
            ReloadSources(selectFirst: SourceCombo.SelectedIndex < 0);
        }

        // ── Helpers ───────────────────────────────────────────────────────────

        static string Combine(string parent, string name)
        {
            parent = parent.TrimEnd('/');
            if (string.IsNullOrEmpty(parent)) parent = "";
            return $"{parent}/{name.TrimStart('/')}";
        }

        void SetActionStatus(string msg)
        {
            ActionStatus.Text = msg;
        }
    }
}
