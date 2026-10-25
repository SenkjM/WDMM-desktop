using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using WdmmDesktop.Generated;
using WdmmDesktop.Models;
using WdmmDesktop.Services;

namespace WdmmDesktop.Dialogs
{
    /// <summary>
    /// Settings dialog — Android settings_screen.dart section parity via TabView.
    /// Sync &amp; Backup mirrors sync_screen.dart: KV + wdmm_sync_trigger +
    /// wdmm_backup_* (WDMMBK01/WDMMEN01). Sources via AccountStore FFI.
    /// </summary>
    public sealed partial class SettingsDialog : ContentDialog
    {
        private readonly string _ffiVersion;

        // Local prefs file (managed by LocalStore).
        private const string PrefsFile = "desktop_prefs.json";

        public SettingsDialog(string ffiVersion)
        {
            InitializeComponent();
            _ffiVersion = ffiVersion;
            FfiVersionText.Text = $"wdmm-core {ffiVersion}";

            LoadLocalPrefs();
            LoadFfiState();
            LoadFfiKvSettings();
            LoadAccounts();
            PopulateSyncSources();
            ApplySyncLoc();
        }

        // ── TabView section switching (panels live inside TabViewItem content) ─

        private void SettingsTabs_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            // Content is hosted in each TabViewItem — nothing to show/hide.
        }

        // ── Local prefs persistence ───────────────────────────────────────────

        // Public so System.Text.Json's default resolver can build a contract for it.
        public sealed class DesktopPrefs
        {
            public string DefaultHome { get; set; } = "Folders";
            public bool   RememberNetworkPath { get; set; } = true;
            public string Theme { get; set; } = "Default";
            public string? SyncRemoteDir { get; set; }
            public string? SyncUrl { get; set; }
            public string? SyncUser { get; set; }
            public string? SyncPass { get; set; }
            // Bind mode / port are OpenAPI (third-party) prefs, kept here too.
            public int    OpenApiPort { get; set; } = 8080;
            public int    BindMode { get; set; } = 0;
        }

        private DesktopPrefs _prefs = new();

        private void LoadLocalPrefs()
        {
            _prefs = LocalStore.Load(PrefsFile, new DesktopPrefs());

            // Default home combo
            SelectComboByTag(DefaultHomeCombo, _prefs.DefaultHome);

            // Remember network path — prefer FFI value (Core KV) over LocalStore cache.
            try
            {
                _prefs.RememberNetworkPath = NativeMethods.SettingsGetNetworkRememberLastPath();
            }
            catch { /* FFI not loaded; keep LocalStore value */ }
            RememberNetworkPathToggle.IsOn = _prefs.RememberNetworkPath;

            // Theme
            SelectComboByTag(ThemeCombo, _prefs.Theme);

            // Sync fields (credentials come from selected source via AccountStore)
            SyncRemoteDirBox.Text = _prefs.SyncRemoteDir ?? "";

            // OpenAPI port / bind mode
            PortBox.Text      = _prefs.OpenApiPort.ToString();
            BindModeCombo.SelectedIndex = _prefs.BindMode;
        }

        private static void SelectComboByTag(ComboBox combo, string tag)
        {
            foreach (var item in combo.Items.OfType<ComboBoxItem>())
            {
                if ((item.Tag as string) == tag) { item.IsSelected = true; return; }
            }
        }

        private static string SelectedComboTag(ComboBox combo)
        {
            if (combo.SelectedItem is ComboBoxItem ci) return ci.Tag as string ?? "";
            return "";
        }

        // ── Section 1: 主页与导航 ───────────────────────────────────────────────

        private void SaveHomeBtn_Click(object sender, RoutedEventArgs e)
        {
            _prefs.DefaultHome         = SelectedComboTag(DefaultHomeCombo);
            _prefs.RememberNetworkPath = RememberNetworkPathToggle.IsOn;
            LocalStore.Save(PrefsFile, _prefs);

            // Also persist to Core via settings FFI so NetworkPage can read it next session.
            try { NativeMethods.SettingsSetNetworkRememberLastPath(_prefs.RememberNetworkPath); }
            catch { /* FFI not loaded */ }

            ShowInlineStatus(HomeStatusText, "已保存。", isError: false);
        }

        // ── Section 5: 杂项 (主题) ───────────────────────────────────────────────

        private void SaveMiscBtn_Click(object sender, RoutedEventArgs e)
        {
            _prefs.Theme = SelectedComboTag(ThemeCombo);
            LocalStore.Save(PrefsFile, _prefs);

            // Apply theme immediately to the main window content root.
            ApplyTheme(_prefs.Theme);

            // Persist locale + snack_duration to Core KV (best-effort).
            var errs = new System.Text.StringBuilder();
            try
            {
                var locale = SelectedComboTag(LocaleCombo);
                Loc.ApplyLocale(locale);
                try { (App.MainWindow as MainWindow)?.ApplyNavLoc(); } catch { }
                string r;
                if (!string.IsNullOrEmpty(locale) && locale != "system")
                    r = NativeMethods.KvSet("app_locale", locale);
                else
                    r = NativeMethods.KvDelete("app_locale");
                if (r.StartsWith("error:")) errs.Append(r).Append(' ');
            }
            catch { errs.Append("locale-FFI-unavailable "); }

            try
            {
                var secs = (int)SnackDurationBox.Value;
                var r = NativeMethods.KvSet("snack_duration", secs.ToString());
                if (r.StartsWith("error:")) errs.Append(r).Append(' ');
            }
            catch { errs.Append("snack-FFI-unavailable "); }

            var msg = errs.Length > 0
                ? $"主题已应用，KV 部分失败：{errs}"
                : "已应用。";
            ShowInlineStatus(MiscStatusText, msg, isError: errs.Length > 0);
        }

        private static void ApplyTheme(string theme)
        {
            if (App.MainWindow?.Content is FrameworkElement root)
            {
                root.RequestedTheme = theme switch
                {
                    "Light"  => ElementTheme.Light,
                    "Dark"   => ElementTheme.Dark,
                    _        => ElementTheme.Default
                };
            }
        }

        // ── Section 6: 同步与备份 ─────────────────────────────────────────────────


        private void ApplySyncLoc()
        {
            if (TabSync != null) TabSync.Header = Loc.Get("Settings.SyncBackup");
            if (TriggerSyncBtn != null) TriggerSyncBtn.Content = Loc.Get("Settings.SyncNow");
        }

        private void TriggerSyncBtn_Click(object sender, RoutedEventArgs e)
        {
            var acc = SyncSourceCombo?.SelectedItem as WebDavAccount;
            var url = acc?.Url?.Trim() ?? "";
            var user = acc?.Username ?? "";
            var pass = acc?.Password ?? "";
            var dir = string.IsNullOrWhiteSpace(SyncRemoteDirBox?.Text) ? "/wdmm-sync" : SyncRemoteDirBox.Text.Trim();
            _prefs.SyncRemoteDir = dir;
            LocalStore.Save(PrefsFile, _prefs);
            try { NativeMethods.KvSet("sync_remote_root", dir); } catch { }
            if (string.IsNullOrEmpty(url))
            {
                ShowInlineStatus(SyncStatusText, "请先在文件源中添加源", isError: true);
                return;
            }
            string result;
            try { result = NativeMethods.WithNativeSyncTrigger(url, user, pass, dir); }
            catch (Exception ex) { result = "error:" + ex.Message; }
            ShowInlineStatus(SyncStatusText, result, isError: result.StartsWith("error:"));
        }

        private void SaveSyncBtn_Click(object sender, RoutedEventArgs e)
        {
            var errs = new System.Text.StringBuilder();
            void Set(string k, string v)
            {
                try
                {
                    var r = NativeMethods.KvSet(k, v);
                    if (r.StartsWith("error:")) errs.Append(k).Append(':').Append(r).Append(' ');
                }
                catch (Exception ex) { errs.Append(k).Append(':').Append(ex.Message).Append(' '); }
            }
            var dir = string.IsNullOrWhiteSpace(SyncRemoteDirBox?.Text) ? "/wdmm-sync" : SyncRemoteDirBox.Text.Trim();
            _prefs.SyncRemoteDir = dir;
            LocalStore.Save(PrefsFile, _prefs);
            Set("sync_remote_root", dir);
            Set("sync_interval", SelectedComboTag(SyncIntervalCombo));
            Set("playlist_sync_enabled", PlaylistSyncToggle.IsOn ? "1" : "0");
            Set("sync_encrypt_password", SyncEncryptPasswordToggle.IsOn ? "1" : "0");
            var bakDir = string.IsNullOrWhiteSpace(BackupRemoteDirBox?.Text)
                ? "/WebdavMediaManager/backup/"
                : BackupRemoteDirBox.Text.Trim();
            if (!bakDir.EndsWith("/")) bakDir += "/";
            BackupRemoteDirBox.Text = bakDir;
            Set("backup_remote_path", bakDir);
            // Persist vault passphrase when the box is non-empty (empty = leave prior KV).
            if (BackupPassphraseBox != null && !string.IsNullOrEmpty(BackupPassphraseBox.Password))
                Set("vault_passphrase", BackupPassphraseBox.Password);
            ShowInlineStatus(SyncStatusText,
                errs.Length > 0 ? $"部分保存失败：{errs}" : "同步设置已保存。",
                isError: errs.Length > 0);
        }

        private void ShowSyncStatus(string msg, bool isError)
        {
            SyncStatusText.Text = msg;
            SyncStatusText.Foreground = isError
                ? (Microsoft.UI.Xaml.Media.Brush)Application.Current.Resources["SystemFillColorCriticalBrush"]
                : (Microsoft.UI.Xaml.Media.Brush)Application.Current.Resources["TextFillColorPrimaryBrush"];
            SyncStatusText.Visibility = Visibility.Visible;
        }


        private (string url, string user, string pass)? SyncCredsOrNull()
        {
            var acc = SyncSourceCombo?.SelectedItem as WebDavAccount;
            var url = acc?.Url?.Trim() ?? "";
            if (string.IsNullOrEmpty(url)) return null;
            return (url, acc?.Username ?? "", acc?.Password ?? "");
        }

        private string BackupRemoteDir()
        {
            var d = string.IsNullOrWhiteSpace(BackupRemoteDirBox?.Text)
                ? "/WebdavMediaManager/backup/"
                : BackupRemoteDirBox.Text.Trim();
            if (!d.EndsWith("/")) d += "/";
            return d;
        }

        private async void BackupUploadBtn_Click(object sender, RoutedEventArgs e)
        {
            var creds = SyncCredsOrNull();
            if (creds == null)
            {
                ShowInlineStatus(BackupStatusText, "请先在文件源中添加源", isError: true);
                return;
            }
            var (url, user, pass) = creds.Value;
            var dir = BackupRemoteDir();
            var phrase = BackupPassphraseBox?.Password ?? "";
            BackupUploadBtn.IsEnabled = false;
            ShowInlineStatus(BackupStatusText, "正在备份并上传…", isError: false);
            string result;
            try
            {
                result = await System.Threading.Tasks.Task.Run(() =>
                    NativeMethods.BackupUpload(url, user, pass, dir, phrase));
            }
            catch (Exception ex) { result = "error:" + ex.Message; }
            finally { BackupUploadBtn.IsEnabled = true; }
            ShowInlineStatus(BackupStatusText, result, isError: result.StartsWith("error:"));
            if (!result.StartsWith("error:"))
                BackupRefreshBtn_Click(sender, e);
        }

        private async void BackupRefreshBtn_Click(object sender, RoutedEventArgs e)
        {
            var creds = SyncCredsOrNull();
            if (creds == null)
            {
                ShowInlineStatus(BackupStatusText, "请先在文件源中添加源", isError: true);
                return;
            }
            var (url, user, pass) = creds.Value;
            var dir = BackupRemoteDir();
            BackupRefreshBtn.IsEnabled = false;
            string result;
            try
            {
                result = await System.Threading.Tasks.Task.Run(() =>
                    NativeMethods.BackupList(url, user, pass, dir));
            }
            catch (Exception ex) { result = "error:" + ex.Message; }
            finally { BackupRefreshBtn.IsEnabled = true; }

            if (result.StartsWith("error:"))
            {
                ShowInlineStatus(BackupStatusText, result, isError: true);
                return;
            }
            try
            {
                var names = System.Text.Json.JsonSerializer.Deserialize<System.Collections.Generic.List<string>>(result)
                            ?? new System.Collections.Generic.List<string>();
                BackupFileCombo.Items.Clear();
                foreach (var n in names) BackupFileCombo.Items.Add(n);
                if (BackupFileCombo.Items.Count > 0) BackupFileCombo.SelectedIndex = 0;
                ShowInlineStatus(BackupStatusText,
                    names.Count == 0 ? "远端无备份文件。" : $"已加载 {names.Count} 个备份。",
                    isError: false);
            }
            catch (Exception ex)
            {
                ShowInlineStatus(BackupStatusText, "error:parse:" + ex.Message, isError: true);
            }
        }

        private async void BackupRestoreBtn_Click(object sender, RoutedEventArgs e)
        {
            var creds = SyncCredsOrNull();
            if (creds == null)
            {
                ShowInlineStatus(BackupStatusText, "请先在文件源中添加源", isError: true);
                return;
            }
            var name = BackupFileCombo?.SelectedItem as string;
            if (string.IsNullOrEmpty(name))
            {
                ShowInlineStatus(BackupStatusText, "请先刷新并选择备份文件", isError: true);
                return;
            }
            var confirm = new ContentDialog
            {
                Title = "确认恢复",
                Content = $"将用远端备份「{name}」覆盖本地媒体库与设置。此操作不可撤销。继续？",
                PrimaryButtonText = "恢复",
                CloseButtonText = "取消",
                DefaultButton = ContentDialogButton.Close,
                XamlRoot = this.XamlRoot,
            };
            if (await confirm.ShowAsync() != ContentDialogResult.Primary) return;

            var (url, user, pass) = creds.Value;
            var dir = BackupRemoteDir();
            var remote = dir.TrimEnd('/') + "/" + name.TrimStart('/');
            var phrase = BackupPassphraseBox?.Password ?? "";
            BackupRestoreBtn.IsEnabled = false;
            ShowInlineStatus(BackupStatusText, "正在下载并恢复…", isError: false);
            string result;
            try
            {
                result = await System.Threading.Tasks.Task.Run(() =>
                    NativeMethods.BackupDownloadRestore(url, user, pass, remote, phrase));
            }
            catch (Exception ex) { result = "error:" + ex.Message; }
            finally { BackupRestoreBtn.IsEnabled = true; }
            ShowInlineStatus(BackupStatusText, result, isError: result.StartsWith("error:"));
        }

        // ── Section 7: OpenAPI (第三方/开发者) ───────────────────────────────────

        private void LoadFfiState()
        {
            try
            {
                var port = NativeMethods.OpenApiPort();
                if (port > 0) PortBox.Text = port.ToString();
                var token = NativeMethods.TokenGet();
                if (!string.IsNullOrEmpty(token)) TokenBox.Text = token;
            }
            catch { /* FFI not loaded */ }
        }


        private void PopulateSyncSources()
        {
            if (SyncSourceCombo == null) return;
            SyncSourceCombo.Items.Clear();
            foreach (var a in AccountStore.List())
                SyncSourceCombo.Items.Add(a);
            SyncSourceCombo.DisplayMemberPath = "DisplayName";
            if (SyncSourceCombo.Items.Count > 0) SyncSourceCombo.SelectedIndex = 0;
        }

        private void LoadAccounts()
        {
            try
            {
                var accounts = AccountStore.List();
                AccountCountText.Text = accounts.Count == 0
                    ? "尚未添加源。"
                    : $"已配置 {accounts.Count} 个源。";
            }
            catch { AccountCountText.Text = "源信息不可用。"; }
        }

        private void StartApiBtn_Click(object sender, RoutedEventArgs e)
        {
            if (!ushort.TryParse(PortBox.Text.Trim(), out var port) || port < 1024)
            {
                ShowApiStatus("端口须为 1024–65535。", isError: true);
                return;
            }
            try
            {
                var result = NativeMethods.StartOpenApi(port);
                if (result.StartsWith("error:")) { ShowApiStatus(result, isError: true); return; }
                _prefs.OpenApiPort = port;
                _prefs.BindMode    = BindModeCombo.SelectedIndex;
                LocalStore.Save(PrefsFile, _prefs);

                var token = NativeMethods.TokenGet();
                TokenBox.Text = string.IsNullOrEmpty(token) ? result : token;
                var boundPort = NativeMethods.OpenApiPort();
                if (boundPort > 0) PortBox.Text = boundPort.ToString();
                ShowApiStatus($"OpenAPI 已启动（:{boundPort}）。", isError: false);
            }
            catch (Exception ex)
            {
                ShowApiStatus($"启动失败：{ex.Message}", isError: true);
            }
        }

        private void StopApiBtn_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                NativeMethods.StopOpenApi();
                TokenBox.Text = "";
                ShowApiStatus("OpenAPI 已停止。", isError: false);
            }
            catch (Exception ex)
            {
                ShowApiStatus($"停止失败：{ex.Message}", isError: true);
            }
        }

        private void RotateTokenBtn_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                var newToken = NativeMethods.TokenRotate();
                if (newToken.StartsWith("error:"))
                    ShowApiStatus(newToken, isError: true);
                else
                {
                    TokenBox.Text = newToken;
                    ShowApiStatus("Token 已轮换。", isError: false);
                }
            }
            catch (Exception ex)
            {
                ShowApiStatus($"FFI 不可用：{ex.Message}", isError: true);
            }
        }

        // ── Status helpers ──────────────────────────────────────────────────────

        private static void ShowInlineStatus(TextBlock tb, string msg, bool isError)
        {
            tb.Text = msg;
            tb.Foreground = isError
                ? (Microsoft.UI.Xaml.Media.Brush)Application.Current.Resources["SystemFillColorCriticalBrush"]
                : (Microsoft.UI.Xaml.Media.Brush)Application.Current.Resources["TextFillColorPrimaryBrush"];
            tb.Visibility = Visibility.Visible;
        }

        private void ShowApiStatus(string msg, bool isError)
        {
            ShowInlineStatus(SaveStatusText, msg, isError);
        }

        // ── Section 2: 媒体库 (KV-backed) ──────────────────────────────────────

        private void SaveLibraryBtn_Click(object sender, RoutedEventArgs e)
        {
            var errs = new System.Text.StringBuilder();
            void Set(string k, string v)
            {
                var r = NativeMethods.KvSet(k, v);
                if (r.StartsWith("error:")) errs.Append(k).Append(':').Append(r).Append(' ');
            }
            try
            {
                Set("media_scan_recursive", ScanRecursiveToggle.IsOn ? "1" : "0");
                Set("media_scan_max_depth", ((int)ScanDepthBox.Value).ToString());
                // Keep legacy scan_* mirrors for older Desktop builds reading them.
                Set("scan_recursive", ScanRecursiveToggle.IsOn ? "1" : "0");
                Set("scan_depth",     ((int)ScanDepthBox.Value).ToString());
                Set("scan_audio",     ScanAudioToggle.IsOn       ? "1" : "0");
                Set("scan_video",     ScanVideoToggle.IsOn       ? "1" : "0");
                Set("scan_manga",     ScanMangaToggle.IsOn       ? "1" : "0");
            }
            catch (Exception ex) { errs.Append(ex.Message); }

            ShowInlineStatus(LibraryStatusText,
                errs.Length > 0 ? $"部分保存失败：{errs}" : "已保存。",
                isError: errs.Length > 0);
        }

        // ── Section 3: 文件源 (KV-backed) ─────────────────────────────────────

        private void SaveNetwork2Btn_Click(object sender, RoutedEventArgs e)
        {
            var errs = new System.Text.StringBuilder();
            void Set(string k, string v)
            {
                var r = NativeMethods.KvSet(k, v);
                if (r.StartsWith("error:")) errs.Append(k).Append(':').Append(r).Append(' ');
            }
            try
            {
                Set("crypt_sequential_download",  CryptSequentialDownloadToggle.IsOn ? "1" : "0");
                Set("prefer_libsodium_secretbox", PreferLibsodiumToggle.IsOn        ? "1" : "0");
                var partSize  = DownloadPartSizeBox.Text.Trim();
                var partCount = DownloadPartCountBox.Text.Trim();
                if (!string.IsNullOrEmpty(partSize))  Set("download_part_size",  partSize);
                if (!string.IsNullOrEmpty(partCount)) Set("download_part_count", partCount);
                Set("dlna_remux",     DlnaRemuxToggle.IsOn     ? "1" : "0");
                Set("dlna_transcode", DlnaTranscodeToggle.IsOn ? "1" : "0");
            }
            catch (Exception ex) { errs.Append(ex.Message); }

            ShowInlineStatus(Network2StatusText,
                errs.Length > 0 ? $"部分保存失败：{errs}" : "已保存。",
                isError: errs.Length > 0);
        }

        // ── Section 4: 视频/文件类型 (KV-backed) ──────────────────────────────

        private void SaveVideoFileBtn_Click(object sender, RoutedEventArgs e)
        {
            var json = FileTypeConfigBox.Text.Trim();
            if (string.IsNullOrEmpty(json))
            {
                ShowInlineStatus(VideoFileStatusText, "请输入 JSON 配置。", isError: true);
                return;
            }
            try
            {
                // Validate JSON before persisting.
                JsonDocument.Parse(json);
                var r = NativeMethods.KvSet("file_type_config_json", json);
                ShowInlineStatus(VideoFileStatusText,
                    r.StartsWith("error:") ? r : "已保存。",
                    isError: r.StartsWith("error:"));
            }
            catch (JsonException je)
            {
                ShowInlineStatus(VideoFileStatusText, $"JSON 格式错误：{je.Message}", isError: true);
            }
            catch (Exception ex)
            {
                ShowInlineStatus(VideoFileStatusText, $"FFI 失败：{ex.Message}", isError: true);
            }
        }

        // ── KV helpers ────────────────────────────────────────────────────────

        /// <summary>Load KV-backed settings from Core FFI into the UI controls.</summary>
        private void LoadFfiKvSettings()
        {
            try
            {
                // ── Library ───────────────────────────────────────────────────
                // Prefer Android key names; fall back to legacy Desktop keys.
                ScanRecursiveToggle.IsOn = KvBool("media_scan_recursive",
                    defaultVal: KvBool("scan_recursive", defaultVal: true));
                ScanDepthBox.Value       = KvInt("media_scan_max_depth",
                    defaultVal: KvInt("scan_depth", defaultVal: 1));
                ScanAudioToggle.IsOn     = KvBool("scan_audio",     defaultVal: true);
                ScanVideoToggle.IsOn     = KvBool("scan_video",     defaultVal: true);
                ScanMangaToggle.IsOn     = KvBool("scan_manga",     defaultVal: false);

                // Sync & Backup (Android-shaped)
                var remote = KvString("sync_remote_root");
                if (!string.IsNullOrEmpty(remote)) SyncRemoteDirBox.Text = remote;
                else if (!string.IsNullOrEmpty(_prefs.SyncRemoteDir)) SyncRemoteDirBox.Text = _prefs.SyncRemoteDir ?? "";
                SelectComboByTag(SyncIntervalCombo, KvString("sync_interval").Length > 0
                    ? KvString("sync_interval") : "off");
                PlaylistSyncToggle.IsOn = KvBool("playlist_sync_enabled", defaultVal: false);
                SyncEncryptPasswordToggle.IsOn = KvBool("sync_encrypt_password", defaultVal: false);
                var bak = KvString("backup_remote_path");
                BackupRemoteDirBox.Text = string.IsNullOrEmpty(bak) ? "/WebdavMediaManager/backup/" : bak;
                var vault = KvString("vault_passphrase");
                if (!string.IsNullOrEmpty(vault) && BackupPassphraseBox != null)
                    BackupPassphraseBox.Password = vault;

                // ── Network2 ─────────────────────────────────────────────────
                CryptSequentialDownloadToggle.IsOn = KvBool("crypt_sequential_download", false);
                PreferLibsodiumToggle.IsOn          = KvBool("prefer_libsodium_secretbox", false);
                DownloadPartSizeBox.Text            = KvString("download_part_size");
                DownloadPartCountBox.Text            = KvString("download_part_count");
                DlnaRemuxToggle.IsOn                 = KvBool("dlna_remux",     false);
                DlnaTranscodeToggle.IsOn             = KvBool("dlna_transcode", false);

                // ── VideoFile ─────────────────────────────────────────────────
                FileTypeConfigBox.Text = KvString("file_type_config_json");

                // ── Misc ──────────────────────────────────────────────────────
                var locale = KvString("app_locale");
                if (!string.IsNullOrEmpty(locale)) SelectComboByTag(LocaleCombo, locale);
                SnackDurationBox.Value = KvInt("snack_duration", defaultVal: 3);

                // ── Queue concurrency (Wave D) ─────────────────────────────
                // Guard so setting the initial value doesn't fire the save handler.
                _queueConcurrencyChanging = true;
                try   { QueueConcurrencyBox.Value = NativeMethods.QueueGetConcurrency(); }
                finally { _queueConcurrencyChanging = false; }
            }
            catch { /* FFI not loaded; controls keep XAML defaults */ }
        }

        // ── Queue concurrency (Wave D — wdmm_queue_get/set_concurrency) ───────

        private bool _queueConcurrencyChanging;

        private void QueueConcurrencyBox_ValueChanged(
            NumberBox sender, NumberBoxValueChangedEventArgs args)
        {
            // Guard against re-entrancy from the initial load assignment.
            if (_queueConcurrencyChanging) return;
            // ValueChanged can fire while XAML still wiring later named controls.
            if (QueueConcurrencyStatusText is null) return;
            if (double.IsNaN(args.NewValue)) return;

            var n = (int)Math.Clamp(args.NewValue, 1, 8);
            _queueConcurrencyChanging = true;
            try
            {
                var result = NativeMethods.QueueSetConcurrency(n);
                QueueConcurrencyStatusText.Text = result.StartsWith("error:", StringComparison.Ordinal)
                    ? result
                    : $"并发数已设为 {n}。";
            }
            catch (Exception ex)
            {
                QueueConcurrencyStatusText.Text = $"FFI 不可用：{ex.Message}";
            }
            finally { _queueConcurrencyChanging = false; }
        }

        private static bool KvBool(string key, bool defaultVal)
        {
            try
            {
                var v = NativeMethods.KvGet(key);
                if (string.IsNullOrEmpty(v)) return defaultVal;
                return v == "1" || v.Equals("true", StringComparison.OrdinalIgnoreCase);
            }
            catch { return defaultVal; }
        }

        private static int KvInt(string key, int defaultVal)
        {
            try
            {
                var v = NativeMethods.KvGet(key);
                return int.TryParse(v, out var n) ? n : defaultVal;
            }
            catch { return defaultVal; }
        }

        private static string KvString(string key)
        {
            try { return NativeMethods.KvGet(key) ?? ""; }
            catch { return ""; }
        }
    }
}
