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

            List<RemoteEntry> entries;
            try
            {
                if (!string.IsNullOrEmpty(_current.Id))
                {
                    // Use account-based FFI for all providers
                    var items = await System.Threading.Tasks.Task.Run(() =>
                        CloudFfi.AccountListDir(_current.Id, CurrentPath));
                    entries = items.Select(i => new RemoteEntry
                    {
                        Name = i.Name ?? "",
                        Path = i.Path ?? "",
                        IsDir = i.IsDir,
                        Size = i.Size,
                        Modified = i.Modified
                    }).ToList();
                }
                else
                {
                    // Fallback to WebDAV-specific for legacy accounts without ID
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
                            Name = el.TryGetProperty("name", out var n) ? n.GetString() ?? "" : "",
                            Path = el.TryGetProperty("path", out var p) ? p.GetString() ?? "" : "",
                            IsDir = el.TryGetProperty("is_dir", out var d) && d.GetBoolean(),
                            Size = el.TryGetProperty("size", out var s) && s.TryGetUInt64(out var sz) ? sz : 0,
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

            try
            {
                if (!string.IsNullOrEmpty(_current.Id))
                    CloudFfi.AccountMkdir(_current.Id, path);
                else
                {
                    var r = NativeMethods.WithNativeWebDavMkdir(_current.Url, _current.Username, _current.Password, path);
                    if (r.StartsWith("error:", StringComparison.Ordinal))
                        throw new Exception(r.Substring(6));
                }
                _ = LoadDirAsync();
            }
            catch (Exception ex)
            {
                ShowEmpty(ex.Message, showRetry: true);
            }
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

            try
            {
                if (!string.IsNullOrEmpty(_current.Id))
                    CloudFfi.AccountRename(_current.Id, ent.Path, newPath);
                else
                {
                    var r = NativeMethods.WithNativeWebDavRename(_current.Url, _current.Username, _current.Password, ent.Path, newPath);
                    if (r.StartsWith("error:", StringComparison.Ordinal))
                        throw new Exception(r.Substring(6));
                }
                _ = LoadDirAsync();
            }
            catch (Exception ex)
            {
                ShowEmpty(ex.Message, showRetry: true);
            }
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

            try
            {
                if (!string.IsNullOrEmpty(_current.Id))
                    CloudFfi.AccountRemove(_current.Id, ent.Path);
                else
                {
                    var r = NativeMethods.WithNativeWebDavRemove(_current.Url, _current.Username, _current.Password, ent.Path);
                    if (r.StartsWith("error:", StringComparison.Ordinal))
                        throw new Exception(r.Substring(6));
                }
                _ = LoadDirAsync();
            }
            catch (Exception ex)
            {
                ShowEmpty(ex.Message, showRetry: true);
            }
        }

        async void ManageBtn_Click(object sender, RoutedEventArgs e)
        {
            // Inline manage dialog: list + add/edit/delete via AccountStore (FFI).
            var panel = new StackPanel { Spacing = 8, MinWidth = 480 };
            var list = new ListView { Height = 180 };
            void RefreshList()
            {
                list.Items.Clear();
                foreach (var s in AccountStore.List())
                    list.Items.Add($"{s.DisplayName}  ·  {s.ProviderType}");
            }
            RefreshList();
            panel.Children.Add(list);

            var nameBox = new TextBox { PlaceholderText = "display name" };
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

            // Dynamic credential fields container
            var credPanel = new StackPanel { Spacing = 6 };
            panel.Children.Add(credPanel);

            // WebDAV fields (visible by default)
            var urlBox = new TextBox { PlaceholderText = "https://nas/dav", Tag = "base_url" };
            var userBox = new TextBox { PlaceholderText = "username", Tag = "username" };
            var passBox = new PasswordBox { Tag = "password" };
            var remoteBox = new TextBox { PlaceholderText = "/", Text = "/", Tag = "remote_dir" };

            // Other provider fields (hidden by default)
            var cookieBox = new TextBox { PlaceholderText = "cookie", Tag = "cookie", Visibility = Visibility.Collapsed };
            var accessTokenBox = new TextBox { PlaceholderText = "access_token", Tag = "access_token", Visibility = Visibility.Collapsed };
            var driveIdBox = new TextBox { PlaceholderText = "drive_id", Tag = "drive_id", Visibility = Visibility.Collapsed };
            var sessionBox = new TextBox { PlaceholderText = "session", Tag = "session", Visibility = Visibility.Collapsed };
            var cryptPassBox = new PasswordBox { PlaceholderText = "crypt password", Tag = "crypt_password", Visibility = Visibility.Collapsed };
            var sourceAccountBox = new TextBox { PlaceholderText = "source account name", Tag = "source_account", Visibility = Visibility.Collapsed };

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
                // Hide all
                urlBox.Visibility = Visibility.Collapsed;
                userBox.Visibility = Visibility.Collapsed;
                passBox.Visibility = Visibility.Collapsed;
                remoteBox.Visibility = Visibility.Collapsed;
                cookieBox.Visibility = Visibility.Collapsed;
                accessTokenBox.Visibility = Visibility.Collapsed;
                driveIdBox.Visibility = Visibility.Collapsed;
                sessionBox.Visibility = Visibility.Collapsed;
                cryptPassBox.Visibility = Visibility.Collapsed;
                sourceAccountBox.Visibility = Visibility.Collapsed;

                switch (provider)
                {
                    case "webdav":
                        urlBox.Visibility = Visibility.Visible;
                        userBox.Visibility = Visibility.Visible;
                        passBox.Visibility = Visibility.Visible;
                        remoteBox.Visibility = Visibility.Visible;
                        break;
                    case "baidu_netdisk":
                    case "terabox":
                        cookieBox.Visibility = Visibility.Visible;
                        break;
                    case "aliyundrive_open":
                        accessTokenBox.Visibility = Visibility.Visible;
                        driveIdBox.Visibility = Visibility.Visible;
                        break;
                    case "115open":
                    case "ctfile":
                        cookieBox.Visibility = Visibility.Visible;
                        break;
                    case "123_open":
                        sessionBox.Visibility = Visibility.Visible;
                        break;
                    case "netease_music":
                        cookieBox.Visibility = Visibility.Visible;
                        break;
                    case "crypt":
                        cryptPassBox.Visibility = Visibility.Visible;
                        sourceAccountBox.Visibility = Visibility.Visible;
                        break;
                }
            }

            providerCombo.SelectionChanged += (_, __) =>
            {
                var provider = providerCombo.SelectedItem?.ToString() ?? "webdav";
                UpdateFieldsForProvider(provider);
            };

            UpdateFieldsForProvider("webdav");

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
                providerCombo.SelectedItem = editing.ProviderType;
                UpdateFieldsForProvider(editing.ProviderType);

                // Load credentials from free-form dictionary
                urlBox.Text = editing.Url;
                if (editing.Credentials.TryGetValue("username", out var u) && u.ValueKind == JsonValueKind.String)
                    userBox.Text = u.GetString() ?? "";
                if (editing.Credentials.TryGetValue("password", out var p) && p.ValueKind == JsonValueKind.String)
                    passBox.Password = p.GetString() ?? "";
                if (editing.Credentials.TryGetValue("remote_dir", out var r) && r.ValueKind == JsonValueKind.String)
                    remoteBox.Text = r.GetString() ?? "/";
                if (editing.Credentials.TryGetValue("cookie", out var c) && c.ValueKind == JsonValueKind.String)
                    cookieBox.Text = c.GetString() ?? "";
                if (editing.Credentials.TryGetValue("access_token", out var at) && at.ValueKind == JsonValueKind.String)
                    accessTokenBox.Text = at.GetString() ?? "";
                if (editing.Credentials.TryGetValue("drive_id", out var di) && di.ValueKind == JsonValueKind.String)
                    driveIdBox.Text = di.GetString() ?? "";
                if (editing.Credentials.TryGetValue("session", out var s) && s.ValueKind == JsonValueKind.String)
                    sessionBox.Text = s.GetString() ?? "";
                if (editing.Credentials.TryGetValue("password", out var cp) && cp.ValueKind == JsonValueKind.String && editing.ProviderType == "crypt")
                    cryptPassBox.Password = cp.GetString() ?? "";
                if (editing.Credentials.TryGetValue("source", out var sa) && sa.ValueKind == JsonValueKind.Object && sa.TryGetProperty("display_name", out var dn))
                    sourceAccountBox.Text = dn.GetString() ?? "";
            };

            saveBtn.Click += (_, __) =>
            {
                var a = editing ?? new WebDavAccount();
                a.DisplayNameField = (nameBox.Text ?? "").Trim();
                var provider = providerCombo.SelectedItem?.ToString() ?? "webdav";
                a.ProviderType = provider;
                a.Url = (urlBox.Text ?? "").Trim();

                // Build credentials based on provider
                a.Credentials.Clear();
                switch (provider)
                {
                    case "webdav":
                        a.Credentials["username"] = JsonDocument.Parse($"\"{userBox.Text ?? ""}\"").RootElement;
                        a.Credentials["password"] = JsonDocument.Parse($"\"{passBox.Password ?? ""}\"").RootElement;
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
                        a.Credentials["drive_id"] = JsonDocument.Parse($"\"{driveIdBox.Text ?? ""}\"").RootElement;
                        break;
                    case "123_open":
                        a.Credentials["session"] = JsonDocument.Parse($"\"{sessionBox.Text ?? ""}\"").RootElement;
                        break;
                    case "crypt":
                        a.Credentials["password"] = JsonDocument.Parse($"\"{cryptPassBox.Password ?? ""}\"").RootElement;
                        // TODO: resolve source account by name and build nested source object
                        var sourceName = sourceAccountBox.Text ?? "";
                        if (!string.IsNullOrEmpty(sourceName))
                        {
                            var sourceAcct = AccountStore.List().FirstOrDefault(x => x.DisplayNameField == sourceName);
                            if (sourceAcct != null)
                            {
                                var sourceJson = $"{{\"provider_type\":\"{sourceAcct.ProviderType}\",\"config\":{{}}}}";
                                a.Credentials["source"] = JsonDocument.Parse(sourceJson).RootElement;
                            }
                        }
                        break;
                }

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
