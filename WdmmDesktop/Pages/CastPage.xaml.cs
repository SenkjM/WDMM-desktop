using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;
using System.Text.Json;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Navigation;
using WdmmDesktop.Dialogs;
using WdmmDesktop.Generated;
using WdmmDesktop.Models;
using WdmmDesktop;

namespace WdmmDesktop.Pages
{
    /// <summary>
    /// Cast / DLNA page — Android parity.
    ///
    /// Locked cast rules (identical to Android):
    ///   1. DLNA cast ALWAYS goes through the local proxy — never a raw file:// or
    ///      direct path URL to the renderer.
    ///   2. Single-file mutex: before registering a new proxy token the previous one
    ///      is unregistered, so only one file is ever proxied at a time.
    ///   3. No automatic HLS ↔ direct fallback.  The user's per-DMR compat choice
    ///      (ForceDirect flag) is honoured verbatim; the code never silently switches.
    ///   4. USN-keyed DMR preferences are persisted via LocalStore.
    ///   5. HLS mode (ForceDirect = false) uses wdmm_proxy_register_hls (wdmm-core
    ///      e236ece). LocalStream and NetworkDirect cast refuse with explicit errors
    ///      until desktop cloud/crypt drivers exist — no silent fallback.
    /// </summary>
    public sealed partial class CastPage : Page
    {
        // Single-file mutex: track the active token and whether it was registered as
        // HLS (requires ProxyUnregisterHls) or file (requires ProxyUnregister).
        private static string? _activeProxyToken;
        private static bool    _activeTokenIsHls;

        private List<DmrDevice> _devices = new();
        private Dictionary<string, DmrPref> _prefs = new();

        public CastPage() => InitializeComponent();

        protected override void OnNavigatedTo(NavigationEventArgs e)
        {
            base.OnNavigatedTo(e);

            _prefs = LocalStore.LoadDmrPrefs();

            // Consume a pending cast track handed off from MusicPage.
            var pending = AppState.PendingCastTrack;
            if (pending != null)
            {
                AppState.PendingCastTrack = null;
                SourceBox.Text = pending.Path;
                PendingTrackText.Text      = $"{pending.Title} — {pending.SubLine}";
                PendingTrackBadge.Visibility = Visibility.Visible;
            }
        }

        // ── Source selection ──────────────────────────────────────────────────

        private void BrowseSourceBtn_Click(object sender, RoutedEventArgs e)
        {
            // Inline path entry is sufficient for now.
            // TODO: FolderPicker / FilePicker via StorageFile once HWND handoff is cleaner.
            SourceBox.Focus(FocusState.Keyboard);
        }

        private void ClearPendingBtn_Click(object sender, RoutedEventArgs e)
        {
            AppState.PendingCastTrack = null;
            PendingTrackBadge.Visibility = Visibility.Collapsed;
            PendingTrackText.Text = "";
        }

        // ── Discovery ─────────────────────────────────────────────────────────

        private async void DiscoverBtn_Click(object sender, RoutedEventArgs e)
        {
            DiscoverBtn.IsEnabled   = false;
            DiscoverProgress.IsActive    = true;
            DiscoverProgress.Visibility  = Visibility.Visible;
            EmptyState.Visibility        = Visibility.Collapsed;
            DmrList.Items.Clear();
            SetStatus("", isError: false);

            try
            {
                // Run FFI discovery on a background thread to keep the UI responsive.
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
                DmrList.Items.Add(d);
        }

        // ── Per-DMR compat toggle ─────────────────────────────────────────────

        private void CompatToggle_Click(object sender, RoutedEventArgs e)
        {
            if (sender is not ToggleButton tb || tb.DataContext is not DmrDevice dev) return;

            if (!_prefs.TryGetValue(dev.Usn, out var pref))
                pref = new DmrPref();

            // checked = HLS mode (ForceDirect=false, ProxyRegisterHls @ e236ece).
            // unchecked = direct mode (ForceDirect=true, ProxyRegisterFile).
            // Rule 3: no silent fallback — honour verbatim; no guard reverting to direct.
            pref.ForceDirect = tb.IsChecked != true;
            _prefs[dev.Usn] = pref;
            LocalStore.SaveDmrPrefs(_prefs);
        }

        // ── Info dialog ───────────────────────────────────────────────────────

        private async void InfoBtn_Click(object sender, RoutedEventArgs e)
        {
            if (sender is not Button btn || btn.DataContext is not DmrDevice dev) return;

            _prefs.TryGetValue(dev.Usn, out var pref);
            var dlg = new DmrInfoDialog(dev, pref ?? new DmrPref())
            {
                XamlRoot = Content.XamlRoot
            };
            await dlg.ShowAsync();
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

            CastBtn_DoDisable(btn);
            SetStatus("Registering proxy…", isError: false);

            try
            {
                string proxyUrl = await System.Threading.Tasks.Task.Run(() =>
                    PerformProxyCast(filePath, dev, forceDirect));

                if (proxyUrl.StartsWith("error:"))
                {
                    SetStatus(proxyUrl, isError: true);
                    return;
                }

                SetStatus($"Casting {System.IO.Path.GetFileName(filePath)} → {dev.FriendlyName}", isError: false);
            }
            catch (Exception ex)
            {
                SetStatus($"Cast failed: {ex.Message}", isError: true);
            }
            finally
            {
                CastBtn_DoEnable(btn);
            }
        }

        /// <summary>
        /// Core cast flow (runs on background thread):
        ///   1. Infer ContentDeliveryKind; refuse LocalStream / NetworkDirect until
        ///      desktop cloud/crypt drivers exist (no silent fallback).
        ///   2. Single-file mutex: unregister previous token (HLS or file).
        ///   3. ForceDirect → ProxyRegisterFile; else → ProxyRegisterHls.
        ///   4. Build proxy URL and issue wdmm_dlna_cast.
        /// Returns the proxy URL on success or "error:…" on failure.
        /// </summary>
        private static string PerformProxyCast(string filePath, DmrDevice dev, bool forceDirect)
        {
            try
            {
                // Infer ContentDeliveryKind: http(s) URLs → NetworkDirect; else LocalFile.
                // LocalStream has no SourceBox representation (no cloud/crypt drivers on desktop).
                var kind = filePath.StartsWith("http://", StringComparison.OrdinalIgnoreCase) ||
                           filePath.StartsWith("https://", StringComparison.OrdinalIgnoreCase)
                    ? Models.ContentDeliveryKind.NetworkDirect
                    : Models.ContentDeliveryKind.LocalFile;

                // ── LocalStream hook ──────────────────────────────────────────────
                // TODO(proxy-cutover): call ProxyRegisterStream once a real stream
                // source exists. FFI is present at wdmm-core e236ece but no desktop
                // driver supplies a WdmmProxyRangeCallback yet. Do NOT invent a bridge.
                if (kind == Models.ContentDeliveryKind.LocalStream)
                    return "error: LocalStream cast not available — no cloud/crypt drivers " +
                           "on desktop. Blocked until a real stream source exists (wdmm-core e236ece).";

                // ── NetworkDirect refuse ──────────────────────────────────────────
                // Locked rule 1: DLNA cast always through proxy. A bare http(s) URL
                // has no local file to proxy via register_file/register_hls.
                // Future: materialize → LocalFile, or register_stream with a fetch
                // callback — not supported yet. Refuse clearly; no silent fallback.
                if (kind == Models.ContentDeliveryKind.NetworkDirect)
                    return "error: NetworkDirect source has no local file. Cast a local " +
                           "file or cache the remote file first (locked rule 1: proxy-only cast).";

                // ── LocalFile — Rule 2: single-file mutex ─────────────────────────
                // Call the matching unregister to avoid leaking HLS transcode sessions.
                if (!string.IsNullOrEmpty(_activeProxyToken))
                {
                    try
                    {
                        if (_activeTokenIsHls)
                            NativeMethods.ProxyUnregisterHls(_activeProxyToken);
                        else
                            NativeMethods.ProxyUnregister(_activeProxyToken);
                    }
                    catch { /* best-effort */ }
                    _activeProxyToken = null;
                    _activeTokenIsHls = false;
                }

                // Ensure proxy is running (no-op if already up).
                var proxyStartResult = NativeMethods.ProxyStart(0);
                if (proxyStartResult.StartsWith("error:"))
                    return proxyStartResult;

                string token, urlPath;
                int    port;

                if (forceDirect)
                {
                    // ForceDirect=true → ProxyRegisterFile (direct-stream, no transcode).
                    var regJson = NativeMethods.ProxyRegisterFile(
                        filePath, System.IO.Path.GetFileName(filePath));
                    if (regJson.StartsWith("error:")) return regJson;

                    using var doc  = JsonDocument.Parse(regJson);
                    var root       = doc.RootElement;
                    token          = root.GetProperty("token").GetString()         ?? "";
                    urlPath        = root.GetProperty("path_template").GetString()  ?? "";
                    port           = root.GetProperty("port").GetInt32();
                    _activeTokenIsHls = false;
                }
                else
                {
                    // ForceDirect=false → ProxyRegisterHls (HLS transcode via ffmpeg).
                    // device_id: Usn is the stable per-locked-rule-4 device key.
                    var deviceId = string.IsNullOrEmpty(dev.Usn)
                        ? $"{dev.FriendlyName}_{dev.Location}"
                        : dev.Usn;

                    var regJson = NativeMethods.ProxyRegisterHls(deviceId, filePath, modeJson: null);
                    if (regJson.StartsWith("error:")) return regJson;

                    using var doc  = JsonDocument.Parse(regJson);
                    var root       = doc.RootElement;
                    token          = root.GetProperty("token").GetString()             ?? "";
                    // NOTE: HLS returns "playlist_template", not "path_template".
                    urlPath        = root.GetProperty("playlist_template").GetString() ?? "";
                    port           = root.GetProperty("port").GetInt32();
                    _activeTokenIsHls = true;
                }

                _activeProxyToken = token;

                // Rule 1: build the proxy URL — never pass filePath directly to the renderer.
                var proxyUrl = $"http://127.0.0.1:{port}{urlPath}";

                // Cast via AVTransport SOAP.
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
            if (string.IsNullOrEmpty(message))
            {
                CastStatus.Visibility = Visibility.Collapsed;
                return;
            }
            CastStatus.Text = message;
            CastStatus.Foreground = isError
                ? (Microsoft.UI.Xaml.Media.Brush)Application.Current.Resources["SystemFillColorCriticalBrush"]
                : (Microsoft.UI.Xaml.Media.Brush)Application.Current.Resources["TextFillColorPrimaryBrush"];
            CastStatus.Visibility = Visibility.Visible;
        }

        private static void CastBtn_DoDisable(Button btn) => btn.IsEnabled = false;
        private static void CastBtn_DoEnable(Button btn)  => btn.IsEnabled = true;
    }
}
