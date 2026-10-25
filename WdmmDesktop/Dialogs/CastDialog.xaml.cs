using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;
using System.Text.Json;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Controls.Primitives;
using WdmmDesktop.Generated;
using WdmmDesktop.Models;
using WdmmDesktop.Services;

namespace WdmmDesktop.Dialogs
{
    /// <summary>
    /// Cast / DLNA dialog — opens from MusicPage (or anywhere) with an optional
    /// pending track. Contains full DMR discover + proxy-cast logic identical to
    /// the locked rules on CastPage. No sidebar entry.
    /// </summary>
    public sealed partial class CastDialog : ContentDialog
    {
        // Single-file mutex: track the active cast token across all operations.
        // CastPage was removed in Wave C — this dialog is the sole owner.
        private static string? _activeProxyToken;
        private static bool    _activeTokenIsHls;

        private List<DmrDevice> _devices = new();
        private Dictionary<string, DmrPref> _prefs = new();

        public CastDialog(TrackItem? pendingTrack = null)
        {
            InitializeComponent();
            DmrPrefsStore.EnsureInit();
            _prefs = DmrPrefsStore.LoadAll();

            if (pendingTrack != null)
            {
                SourceBox.Text               = pendingTrack.Path;
                PendingTrackText.Text        = $"{pendingTrack.Title} — {pendingTrack.SubLine}";
                PendingTrackBadge.Visibility = Visibility.Visible;
            }
        }

        // ── Pending track badge ───────────────────────────────────────────────

        private void ClearPendingBtn_Click(object sender, RoutedEventArgs e)
        {
            PendingTrackBadge.Visibility = Visibility.Collapsed;
            PendingTrackText.Text        = "";
        }

        // ── Discovery ─────────────────────────────────────────────────────────

        private async void DiscoverBtn_Click(object sender, RoutedEventArgs e)
        {
            DiscoverBtn.IsEnabled        = false;
            DiscoverProgress.IsActive    = true;
            DiscoverProgress.Visibility  = Visibility.Visible;
            EmptyState.Visibility        = Visibility.Collapsed;
            DmrList.Items.Clear();
            SetStatus("", isError: false);

            try
            {
                string json = await System.Threading.Tasks.Task.Run(() =>
                {
                    try { return NativeMethods.DlnaDiscoverJson(timeoutMs: 2000); }
                    catch { return "[]"; }
                });
                _devices = JsonSerializer.Deserialize<List<DmrDevice>>(json)
                           ?? new List<DmrDevice>();
            }
            catch
            {
                _devices = new List<DmrDevice>();
            }

            ApplyDevices();
            DiscoverProgress.IsActive   = false;
            DiscoverProgress.Visibility = Visibility.Collapsed;
            DiscoverBtn.IsEnabled       = true;
        }

        private void ApplyDevices()
        {
            DmrList.Items.Clear();
            if (_devices.Count == 0)
            {
                EmptyState.Visibility = Visibility.Visible;
                EmptyText.Text = "No DLNA renderers found. Check that devices are on the same network segment.";
                return;
            }
            EmptyState.Visibility = Visibility.Collapsed;
            foreach (var d in _devices)
            {
                if (_prefs.TryGetValue(d.Usn, out var pref) && !string.IsNullOrWhiteSpace(pref.OverrideName))
                    d.DisplayName = pref.OverrideName!;
                else
                    d.DisplayName = d.FriendlyName;
                DmrList.Items.Add(d);
            }
        }

        // ── Per-DMR compat toggle ─────────────────────────────────────────────

        private void CompatToggle_Click(object sender, RoutedEventArgs e)
        {
            if (sender is not ToggleButton tb || tb.DataContext is not DmrDevice dev) return;
            if (!_prefs.TryGetValue(dev.Usn, out var pref))
                pref = new DmrPref { Usn = dev.Usn };
            pref.Usn = dev.Usn;
            pref.ForceDirect = tb.IsChecked != true;
            _prefs[dev.Usn] = pref;
            DmrPrefsStore.Upsert(pref);
        }

        // ── Info dialog ───────────────────────────────────────────────────────

        private async void InfoBtn_Click(object sender, RoutedEventArgs e)
        {
            if (sender is not Button btn || btn.DataContext is not DmrDevice dev) return;
            _prefs.TryGetValue(dev.Usn, out var pref);
            var dlg = new DmrInfoDialog(dev, pref ?? new DmrPref { Usn = dev.Usn }) { XamlRoot = this.XamlRoot };
            await dlg.ShowAsync();
            if (dlg.SavedPref != null)
            {
                _prefs[dev.Usn] = dlg.SavedPref;
                ApplyDevices();
            }
        }

        // ── Cast ──────────────────────────────────────────────────────────────

        private async void CastBtn_Click(object sender, RoutedEventArgs e)
        {
            if (sender is not Button btn || btn.DataContext is not DmrDevice dev) return;

            var filePath = SourceBox.Text.Trim();
            if (string.IsNullOrEmpty(filePath))
            {
                SetStatus("Select or enter a source file first.", isError: true);
                return;
            }

            _prefs.TryGetValue(dev.Usn, out var pref);
            bool forceDirect = pref?.ForceDirect ?? true;

            btn.IsEnabled = false;
            SetStatus("Registering proxy…", isError: false);

            try
            {
                string proxyUrl = await System.Threading.Tasks.Task.Run(() =>
                    PerformProxyCast(filePath, dev, forceDirect));

                if (proxyUrl.StartsWith("error:"))
                    SetStatus(proxyUrl, isError: true);
                else
                {
                    var label = string.IsNullOrEmpty(dev.DisplayName) ? dev.FriendlyName : dev.DisplayName;
                    SetStatus($"Casting {System.IO.Path.GetFileName(filePath)} → {label}", isError: false);
                }
            }
            catch (Exception ex)
            {
                SetStatus($"Cast failed: {ex.Message}", isError: true);
            }
            finally
            {
                btn.IsEnabled = true;
            }
        }

        /// <summary>
        /// Core proxy-cast flow (background thread).
        /// Locked rules: DLNA→proxy only; single-file mutex; no auto HLS↔direct fallback.
        /// </summary>
        private static string PerformProxyCast(string filePath, DmrDevice dev, bool forceDirect)
        {
            try
            {
                // Infer ContentDeliveryKind.
                var kind = filePath.StartsWith("http://", StringComparison.OrdinalIgnoreCase) ||
                           filePath.StartsWith("https://", StringComparison.OrdinalIgnoreCase)
                    ? Models.ContentDeliveryKind.NetworkDirect
                    : Models.ContentDeliveryKind.LocalFile;

                if (kind == Models.ContentDeliveryKind.LocalStream)
                    return "error: LocalStream cast not available — no cloud/crypt drivers on desktop.";

                if (kind == Models.ContentDeliveryKind.NetworkDirect)
                    return "error: NetworkDirect source has no local file. Cast a local file or cache it first.";

                // Single-file mutex: unregister previous token.
                if (!string.IsNullOrEmpty(_activeProxyToken))
                {
                    try
                    {
                        if (_activeTokenIsHls) NativeMethods.ProxyUnregisterHls(_activeProxyToken);
                        else                   NativeMethods.ProxyUnregister(_activeProxyToken);
                    }
                    catch { }
                    _activeProxyToken = null;
                    _activeTokenIsHls = false;
                }

                var proxyStartResult = NativeMethods.ProxyStart(0);
                if (proxyStartResult.StartsWith("error:")) return proxyStartResult;

                string token, urlPath;
                int    port;

                if (forceDirect)
                {
                    var regJson = NativeMethods.ProxyRegisterFile(filePath, System.IO.Path.GetFileName(filePath));
                    if (regJson.StartsWith("error:")) return regJson;
                    using var doc = JsonDocument.Parse(regJson);
                    var root      = doc.RootElement;
                    token         = root.GetProperty("token").GetString()        ?? "";
                    urlPath       = root.GetProperty("path_template").GetString() ?? "";
                    port          = root.GetProperty("port").GetInt32();
                    _activeTokenIsHls = false;
                }
                else
                {
                    var deviceId = string.IsNullOrEmpty(dev.Usn)
                        ? $"{dev.FriendlyName}_{dev.Location}"
                        : dev.Usn;
                    var regJson  = NativeMethods.ProxyRegisterHls(deviceId, filePath, modeJson: null);
                    if (regJson.StartsWith("error:")) return regJson;
                    using var doc = JsonDocument.Parse(regJson);
                    var root      = doc.RootElement;
                    token         = root.GetProperty("token").GetString()             ?? "";
                    urlPath       = root.GetProperty("playlist_template").GetString() ?? "";
                    port          = root.GetProperty("port").GetInt32();
                    _activeTokenIsHls = true;
                }

                _activeProxyToken = token;
                var proxyUrl = $"http://127.0.0.1:{port}{urlPath}";

                var locBytes = System.Text.Encoding.UTF8.GetBytes(dev.Location + '\0');
                var locPtr   = Marshal.AllocHGlobal(locBytes.Length);
                try
                {
                    Marshal.Copy(locBytes, 0, locPtr, locBytes.Length);
                    var urlBytes = System.Text.Encoding.UTF8.GetBytes(proxyUrl + '\0');
                    var urlPtr   = Marshal.AllocHGlobal(urlBytes.Length);
                    try
                    {
                        Marshal.Copy(urlBytes, 0, urlPtr, urlBytes.Length);
                        var castResult = NativeMethods.wdmm_dlna_cast(locPtr, urlPtr).ToStringAndFree();
                        if (castResult.StartsWith("error:")) return castResult;
                    }
                    finally { Marshal.FreeHGlobal(urlPtr); }
                }
                finally { Marshal.FreeHGlobal(locPtr); }

                return proxyUrl;
            }
            catch (Exception ex)
            {
                return $"error: {ex.Message}";
            }
        }

        // ── Helpers ───────────────────────────────────────────────────────────

        private void SetStatus(string message, bool isError)
        {
            if (string.IsNullOrEmpty(message)) { CastStatus.Visibility = Visibility.Collapsed; return; }
            CastStatus.Text = message;
            CastStatus.Foreground = isError
                ? (Microsoft.UI.Xaml.Media.Brush)Application.Current.Resources["SystemFillColorCriticalBrush"]
                : (Microsoft.UI.Xaml.Media.Brush)Application.Current.Resources["TextFillColorPrimaryBrush"];
            CastStatus.Visibility = Visibility.Visible;
        }
    }
}
