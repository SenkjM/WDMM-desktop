using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;
using System.Text.Json;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Navigation;
using WdmmDesktop.Generated;
using WdmmDesktop.Models;
using WdmmDesktop;

namespace WdmmDesktop.Pages
{
    public sealed partial class NetworkPage : Page
    {
        private List<WebDavAccount> _accounts = new();
        private WebDavAccount? _selected;
        private bool _isNew;
        // Cached last network path, restored from Core FFI when RememberNetworkPath is on.
        private string? _lastPath;

        public NetworkPage() => InitializeComponent();

        protected override void OnNavigatedTo(NavigationEventArgs e)
        {
            base.OnNavigatedTo(e);
            LoadAccounts();
            RestoreLastPath();
        }

        /// <summary>
        /// If RememberNetworkPath is set in Core, load the last used remote dir
        /// so it can be offered as the default when the user creates a new account.
        /// </summary>
        private void RestoreLastPath()
        {
            try
            {
                if (!NativeMethods.SettingsGetNetworkRememberLastPath()) return;
                var path = NativeMethods.SettingsGetNetworkLastPath();
                if (!string.IsNullOrEmpty(path)) _lastPath = path;
            }
            catch { /* FFI not loaded */ }
        }

        // ── Account list ──────────────────────────────────────────────────────

        private void LoadAccounts()
        {
            _accounts = LocalStore.LoadAccounts();
            AccountList.Items.Clear();
            foreach (var a in _accounts)
                AccountList.Items.Add(a);
        }

        private void AccountList_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (AccountList.SelectedItem is WebDavAccount acc)
            {
                _selected = acc;
                _isNew = false;
                PopulateForm(acc);
            }
        }

        private void AddBtn_Click(object sender, RoutedEventArgs e)
        {
            _selected = null;
            _isNew = true;
            AccountList.SelectedItem = null;
            // Pre-fill RemoteDir with the last remembered path when available.
            var defaultDir = !string.IsNullOrEmpty(_lastPath) ? _lastPath : "/wdmm";
            PopulateForm(new WebDavAccount { RemoteDir = defaultDir });
        }

        private void DeleteBtn_Click(object sender, RoutedEventArgs e)
        {
            if (_selected == null) return;
            _accounts.Remove(_selected);
            LocalStore.SaveAccounts(_accounts);
            LoadAccounts();
            ClearForm();
        }

        // ── Form ──────────────────────────────────────────────────────────────

        private void PopulateForm(WebDavAccount a)
        {
            FormPanel.IsEnabled = true;
            SaveBtn.IsEnabled   = true;
            SyncBtn.IsEnabled   = true;
            DeleteBtn.IsEnabled = !_isNew;

            FormTitle.Text   = _isNew ? "New account" : $"Edit — {a.DisplayName}";
            NameBox.Text     = a.Name;
            UrlBox.Text      = a.Url;
            UserBox.Text     = a.Username;
            PassBox.Password = a.Password;
            RemoteDirBox.Text = a.RemoteDir;

            SyncStatus.Visibility = Visibility.Collapsed;
        }

        private void ClearForm()
        {
            _selected = null;
            _isNew = false;
            FormPanel.IsEnabled = false;
            SaveBtn.IsEnabled   = false;
            SyncBtn.IsEnabled   = false;
            DeleteBtn.IsEnabled = false;
            FormTitle.Text = "Select or add an account";
            NameBox.Text = UrlBox.Text = UserBox.Text = RemoteDirBox.Text = "";
            PassBox.Password = "";
            SyncStatus.Visibility = Visibility.Collapsed;
        }

        private void SaveBtn_Click(object sender, RoutedEventArgs e)
        {
            var url = UrlBox.Text.Trim();
            if (string.IsNullOrEmpty(url))
            {
                ShowSyncStatus("URL is required.", isError: true);
                return;
            }

            if (_isNew)
            {
                var a = new WebDavAccount
                {
                    Name      = NameBox.Text.Trim(),
                    Url       = url,
                    Username  = UserBox.Text.Trim(),
                    Password  = PassBox.Password,
                    RemoteDir = string.IsNullOrWhiteSpace(RemoteDirBox.Text) ? "/wdmm" : RemoteDirBox.Text.Trim()
                };
                _accounts.Add(a);
                _selected = a;
                _isNew = false;
            }
            else if (_selected != null)
            {
                _selected.Name      = NameBox.Text.Trim();
                _selected.Url       = url;
                _selected.Username  = UserBox.Text.Trim();
                _selected.Password  = PassBox.Password;
                _selected.RemoteDir = string.IsNullOrWhiteSpace(RemoteDirBox.Text) ? "/wdmm" : RemoteDirBox.Text.Trim();
            }

            LocalStore.SaveAccounts(_accounts);
            LoadAccounts();
            ShowSyncStatus("Saved.", isError: false);
        }

        // ── Sync ──────────────────────────────────────────────────────────────

        private void SyncBtn_Click(object sender, RoutedEventArgs e)
        {
            var url      = UrlBox.Text.Trim();
            var user     = UserBox.Text.Trim();
            var pass     = PassBox.Password;
            var remDir   = string.IsNullOrWhiteSpace(RemoteDirBox.Text) ? "/wdmm" : RemoteDirBox.Text.Trim();

            if (string.IsNullOrEmpty(url))
            {
                ShowSyncStatus("URL is required to sync.", isError: true);
                return;
            }

            try
            {
                // wdmm_sync_trigger is exported; passes WebDAV credentials to Rust.
                var result = WithNativeStrings(
                    new[] { url, user, pass, remDir },
                    ptrs => NativeMethods.wdmm_sync_trigger(ptrs[0], ptrs[1], ptrs[2], ptrs[3])
                           .ToStringAndFree());

                var isErr = result.StartsWith("error:");
                ShowSyncStatus(isErr ? result : $"Sync complete: {result}",
                               isError: isErr);

                // On success, persist the remote dir as the last network path so
                // NetworkPage can restore it next session (when RememberNetworkPath is on).
                if (!isErr)
                {
                    try { NativeMethods.SettingsSetNetworkLastPath(remDir); }
                    catch { /* FFI not loaded */ }
                    _lastPath = remDir;
                }
            }
            catch (Exception ex)
            {
                ShowSyncStatus($"FFI not loaded — {ex.Message}", isError: true);
            }
        }

        private void ShowSyncStatus(string message, bool isError)
        {
            SyncStatus.Text = message;
            SyncStatus.Foreground = isError
                ? (Microsoft.UI.Xaml.Media.Brush)Application.Current.Resources["SystemFillColorCriticalBrush"]
                : (Microsoft.UI.Xaml.Media.Brush)Application.Current.Resources["TextFillColorPrimaryBrush"];
            SyncStatus.Visibility = Visibility.Visible;
        }

        // ── Helpers ───────────────────────────────────────────────────────────

        /// <summary>
        /// Allocates a block of UTF-8 native string pointers, calls <paramref name="fn"/>,
        /// then frees all allocations. Equivalent to the single-string version in NativeMethods
        /// but for multiple arguments at once.
        /// </summary>
        private static T WithNativeStrings<T>(string[] strings, Func<IntPtr[], T> fn)
        {
            var ptrs = new IntPtr[strings.Length];
            try
            {
                for (int i = 0; i < strings.Length; i++)
                {
                    var bytes = System.Text.Encoding.UTF8.GetBytes(strings[i] + '\0');
                    ptrs[i] = Marshal.AllocHGlobal(bytes.Length);
                    Marshal.Copy(bytes, 0, ptrs[i], bytes.Length);
                }
                return fn(ptrs);
            }
            finally
            {
                foreach (var p in ptrs)
                    if (p != IntPtr.Zero) Marshal.FreeHGlobal(p);
            }
        }
    }
}
