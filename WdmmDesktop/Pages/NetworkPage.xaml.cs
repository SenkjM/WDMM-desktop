using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Input;
using Microsoft.UI.Xaml.Navigation;
using WdmmDesktop.Generated;
using WdmmDesktop.Models;
using WdmmDesktop.Services;

namespace WdmmDesktop.Pages
{
    /// <summary>
    /// 文件源 directory browser — list/enter/up via <c>wdmm_webdav_*</c> FFI.
    /// Sources from <c>wdmm_account_*</c> (AccountStore). No stub / 待 FFI UI.
    /// </summary>
    public sealed partial class NetworkPage : Page
    {
        private List<WebDavAccount> _sources = new();
        private WebDavAccount? _current;
        private readonly List<string> _stack = new() { "/" };

        public NetworkPage() => InitializeComponent();

        protected override void OnNavigatedTo(NavigationEventArgs e)
        {
            base.OnNavigatedTo(e);
            ApplyLoc();
            ReloadSources(selectFirst: true);
        }

        void ApplyLoc()
        {
            LblSource.Text = Loc.Get("Network.Sources");
            ManageBtn.Content = Loc.Get("Network.ManageSources");
            UpBtn.Content = Loc.Get("Network.Up");
            RefreshBtn.Content = Loc.Get("Network.Refresh");
            NewFolderBtn.Content = Loc.Get("Network.NewFolder");
            EmptyText.Text = Loc.Get("Network.Empty");
            RetryBtn.Content = Loc.Get("Network.Retry");
            CtxRename.Text = Loc.Get("Network.Rename");
            CtxDelete.Text = Loc.Get("Network.Delete");
        }

        string CurrentPath => _stack.Count == 0 ? "/" : _stack[^1];

        void ReloadSources(bool selectFirst)
        {
            _sources = AccountStore.List();
            SourceCombo.Items.Clear();
            foreach (var s in _sources)
                SourceCombo.Items.Add(s.DisplayName);

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

        void SourceCombo_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            var i = SourceCombo.SelectedIndex;
            if (i < 0 || i >= _sources.Count) return;
            _current = _sources[i];
            _stack.Clear();
            var root = string.IsNullOrWhiteSpace(_current.RemoteDir) ? "/" : _current.RemoteDir.TrimEnd('/');
            if (string.IsNullOrEmpty(root)) root = "/";
            if (!root.StartsWith('/')) root = "/" + root;

            // Remember path
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
            _ = LoadDirAsync();
        }

        async void LoadDirAsync()
        {
            if (_current == null) return;
            SetBusy(true);
            PathText.Text = CurrentPath;
            UpBtn.IsEnabled = CurrentPath != "/" && CurrentPath.TrimEnd('/') != (_current.RemoteDir?.TrimEnd('/') ?? "");

            string json;
            try
            {
                json = await System.Threading.Tasks.Task.Run(() =>
                    NativeMethods.WithNativeWebDavList(
                        _current.Url, _current.Username, _current.Password, CurrentPath));
            }
            catch (Exception ex)
            {
                SetBusy(false);
                ShowEmpty(ex.Message, showRetry: true);
                return;
            }

            SetBusy(false);
            if (json.StartsWith("error:", StringComparison.Ordinal))
            {
                ShowEmpty(json, showRetry: true);
                return;
            }

            var entries = new List<RemoteEntry>();
            try
            {
                using var doc = JsonDocument.Parse(json);
                foreach (var el in doc.RootElement.EnumerateArray())
                {
                    entries.Add(new RemoteEntry
                    {
                        Name = el.TryGetProperty("name", out var n) ? n.GetString() ?? "" : "",
                        Path = el.TryGetProperty("path", out var p) ? p.GetString() ?? "" : "",
                        IsDir = el.TryGetProperty("is_dir", out var d) && d.GetBoolean(),
                        Size = el.TryGetProperty("size", out var s) && s.TryGetUInt64(out var sz) ? sz : 0,
                        Modified = el.TryGetProperty("modified", out var m) && m.ValueKind != JsonValueKind.Null ? m.GetString() : null,
                    });
                }
            }
            catch (Exception ex)
            {
                ShowEmpty(ex.Message, showRetry: true);
                return;
            }

            entries = entries
                .OrderByDescending(x => x.IsDir)
                .ThenBy(x => x.Name, StringComparer.OrdinalIgnoreCase)
                .ToList();

            EntryList.ItemsSource = entries;
            if (entries.Count == 0)
                ShowEmpty(Loc.Get("Network.Empty"), showRetry: false);
            else
            {
                EmptyPanel.Visibility = Visibility.Collapsed;
                EntryList.Visibility = Visibility.Visible;
            }

            try { NativeMethods.SettingsSetNetworkLastPath(CurrentPath); } catch { }
        }

        void ShowEmpty(string msg, bool showRetry)
        {
            EntryList.Visibility = Visibility.Collapsed;
            EmptyPanel.Visibility = Visibility.Visible;
            EmptyText.Text = msg;
            RetryBtn.Visibility = showRetry ? Visibility.Visible : Visibility.Collapsed;
        }

        void SetBusy(bool on)
        {
            BusyRing.IsActive = on;
            BusyRing.Visibility = on ? Visibility.Visible : Visibility.Collapsed;
            if (on)
            {
                EmptyPanel.Visibility = Visibility.Collapsed;
                EntryList.Visibility = Visibility.Collapsed;
            }
        }

        void UpBtn_Click(object sender, RoutedEventArgs e)
        {
            if (_stack.Count <= 1) return;
            _stack.RemoveAt(_stack.Count - 1);
            _ = LoadDirAsync();
        }

        void RefreshBtn_Click(object sender, RoutedEventArgs e) => _ = LoadDirAsync();

        void EntryList_DoubleTapped(object sender, DoubleTappedRoutedEventArgs e)
        {
            if (EntryList.SelectedItem is not RemoteEntry ent || !ent.IsDir) return;
            var next = string.IsNullOrEmpty(ent.Path) ? Combine(CurrentPath, ent.Name) : ent.Path;
            _stack.Add(next);
            _ = LoadDirAsync();
        }

        static string Combine(string parent, string name)
        {
            parent = parent.TrimEnd('/');
            if (string.IsNullOrEmpty(parent)) parent = "";
            return $"{parent}/{name.TrimStart('/')}";
        }

        async void NewFolderBtn_Click(object sender, RoutedEventArgs e)
        {
            if (_current == null) return;
            var box = new TextBox { PlaceholderText = "name" };
            var dlg = new ContentDialog
            {
                Title = Loc.Get("Network.NewFolder"),
                Content = box,
                PrimaryButtonText = "OK",
                CloseButtonText = "Cancel",
                XamlRoot = XamlRoot
            };
            if (await dlg.ShowAsync() != ContentDialogResult.Primary) return;
            var name = (box.Text ?? "").Trim();
            if (string.IsNullOrEmpty(name)) return;
            var path = Combine(CurrentPath, name);
            var r = NativeMethods.WithNativeWebDavMkdir(_current.Url, _current.Username, _current.Password, path);
            if (r.StartsWith("error:", StringComparison.Ordinal))
                ShowEmpty(r, showRetry: true);
            else
                _ = LoadDirAsync();
        }

        async void CtxRename_Click(object sender, RoutedEventArgs e)
        {
            if (_current == null || EntryList.SelectedItem is not RemoteEntry ent) return;
            var box = new TextBox { Text = ent.Name };
            var dlg = new ContentDialog
            {
                Title = Loc.Get("Network.Rename"),
                Content = box,
                PrimaryButtonText = "OK",
                CloseButtonText = "Cancel",
                XamlRoot = XamlRoot
            };
            if (await dlg.ShowAsync() != ContentDialogResult.Primary) return;
            var name = (box.Text ?? "").Trim();
            if (string.IsNullOrEmpty(name) || name == ent.Name) return;
            var newPath = Combine(CurrentPath, name);
            var r = NativeMethods.WithNativeWebDavRename(_current.Url, _current.Username, _current.Password, ent.Path, newPath);
            if (r.StartsWith("error:", StringComparison.Ordinal))
                ShowEmpty(r, showRetry: true);
            else
                _ = LoadDirAsync();
        }

        async void CtxDelete_Click(object sender, RoutedEventArgs e)
        {
            if (_current == null || EntryList.SelectedItem is not RemoteEntry ent) return;
            var dlg = new ContentDialog
            {
                Title = Loc.Get("Network.Delete"),
                Content = ent.Name,
                PrimaryButtonText = Loc.Get("Network.Delete"),
                CloseButtonText = "Cancel",
                XamlRoot = XamlRoot
            };
            if (await dlg.ShowAsync() != ContentDialogResult.Primary) return;
            var r = NativeMethods.WithNativeWebDavRemove(_current.Url, _current.Username, _current.Password, ent.Path);
            if (r.StartsWith("error:", StringComparison.Ordinal))
                ShowEmpty(r, showRetry: true);
            else
                _ = LoadDirAsync();
        }

        async void ManageBtn_Click(object sender, RoutedEventArgs e)
        {
            // Inline manage dialog: list + add/edit/delete via AccountStore (FFI).
            var panel = new StackPanel { Spacing = 8, MinWidth = 360 };
            var list = new ListView { Height = 180 };
            void RefreshList()
            {
                list.Items.Clear();
                foreach (var s in AccountStore.List())
                    list.Items.Add($"{s.DisplayName}  ·  {s.Url}");
            }
            RefreshList();
            panel.Children.Add(list);

            var nameBox = new TextBox { PlaceholderText = "display name" };
            var urlBox = new TextBox { PlaceholderText = "https://nas/dav" };
            var userBox = new TextBox { PlaceholderText = "username" };
            var passBox = new PasswordBox();
            var remoteBox = new TextBox { PlaceholderText = "/", Text = "/" };
            panel.Children.Add(nameBox);
            panel.Children.Add(urlBox);
            panel.Children.Add(userBox);
            panel.Children.Add(passBox);
            panel.Children.Add(remoteBox);

            var row = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 6 };
            var saveBtn = new Button { Content = "Save" };
            var delBtn = new Button { Content = "Delete" };
            row.Children.Add(saveBtn);
            row.Children.Add(delBtn);
            panel.Children.Add(row);
            var status = new TextBlock { TextWrapping = TextWrapping.Wrap, FontSize = 12 };
            panel.Children.Add(status);

            WebDavAccount? editing = null;
            list.SelectionChanged += (_, __) =>
            {
                var i = list.SelectedIndex;
                var all = AccountStore.List();
                if (i < 0 || i >= all.Count) { editing = null; return; }
                editing = all[i];
                nameBox.Text = editing.DisplayNameField;
                urlBox.Text = editing.Url;
                userBox.Text = editing.Username;
                passBox.Password = editing.Password;
                remoteBox.Text = editing.RemoteDir;
            };

            saveBtn.Click += (_, __) =>
            {
                var a = editing ?? new WebDavAccount();
                a.DisplayNameField = (nameBox.Text ?? "").Trim();
                a.Url = (urlBox.Text ?? "").Trim();
                a.Username = (userBox.Text ?? "").Trim();
                a.Password = passBox.Password ?? "";
                a.RemoteDir = string.IsNullOrWhiteSpace(remoteBox.Text) ? "/" : remoteBox.Text.Trim();
                var r = AccountStore.Upsert(a);
                status.Text = r.StartsWith("error:", StringComparison.Ordinal) ? r : "ok";
                if (!r.StartsWith("error:", StringComparison.Ordinal))
                {
                    editing = null;
                    RefreshList();
                    ReloadSources(selectFirst: false);
                }
            };
            delBtn.Click += (_, __) =>
            {
                if (editing == null || string.IsNullOrEmpty(editing.Id)) return;
                var r = AccountStore.Delete(editing.Id);
                status.Text = r;
                editing = null;
                RefreshList();
                ReloadSources(selectFirst: true);
            };

            var dlg = new ContentDialog
            {
                Title = Loc.Get("Network.ManageSources"),
                Content = panel,
                CloseButtonText = "Close",
                XamlRoot = XamlRoot
            };
            await dlg.ShowAsync();
            ReloadSources(selectFirst: SourceCombo.SelectedIndex < 0);
        }
    }
}
