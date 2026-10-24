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
    ///   5. HLS mode (ForceDirect = false) is gated: wdmm_proxy_start_hls is not yet
    ///      exported — the UI shows the toggle but blocks the cast with an error message
    ///      until feat/proxy-hls-ffmpeg merges.
    /// </summary>
    public sealed partial class CastPage : Page
    {
        // Single-file mutex: the active proxy token (if any) for the current cast session.
        private static string? _activeProxyToken;

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

            // Resolve or create the pref entry for this USN.
            if (!_prefs.TryGetValue(dev.Usn, out var pref))
                pref = new DmrPref();

            // Toggle: currently showing Direct → switch to HLS and vice-versa.
            // HLS (ForceDirect=false) is blocked until feat/proxy-hls-ffmpeg lands.
            bool wantsHls = tb.IsChecked == true; // checked = HLS requested
            if (wantsHls)
            {
                // HLS stub guard.
                tb.IsChecked = false;
                SetStatus(
                    "HLS mode blocked — wdmm_proxy_start_hls not yet exported." +
                    " Waiting on wdmm-core feat/proxy-hls-ffmpeg. Using direct stream.",
                    isError: true);
                pref.ForceDirect = true;
            }
            else
            {
                pref.ForceDirect = true; // unchecked = direct
            }

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

            // HLS mode guard (rule 5 — no proxy_start_hls yet).
            if (!forceDirect)
            {
                SetStatus(
                    "HLS blocked — wdmm_proxy_start_hls not yet in FFI (feat/proxy-hls-ffmpeg). " +
                    "Casting in direct mode instead.",
                    isError: true);
                forceDirect = true; // silently degrade would violate rule 3; show error and stop.
                return;             // rule 3: no silent fallback — user must choose.
            }

            CastBtn_DoDisable(btn);
            SetStatus("Registering proxy…", isError: false);

            try
            {
                string proxyUrl = await System.Threading.Tasks.Task.Run(() =>
                    PerformProxyCast(filePath, dev));

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
        ///   1. Unregister any previously active proxy token (single-file mutex).
        ///   2. Register the new file with the proxy.
        ///   3. Build the proxy URL and issue wdmm_dlna_cast.
        /// Returns the proxy URL on success or "error:…" on failure.
        /// </summary>
        private static string PerformProxyCast(string filePath, DmrDevice dev)
        {
            try
            {
                // Rule 2: single-file mutex — always release the previous token first.
                if (!string.IsNullOrEmpty(_activeProxyToken))
                {
                    try { NativeMethods.ProxyUnregister(_activeProxyToken); }
                    catch { /* best-effort */ }
                    _activeProxyToken = null;
                }

                // Ensure proxy is running (no-op if already up).
                var proxyStartResult = NativeMethods.ProxyStart(0);
                if (proxyStartResult.StartsWith("error:"))
                    return proxyStartResult;

                // Register the file — returns JSON: {"token":"…","path_template":"/s/…","port":N}
                var regJson = NativeMethods.ProxyRegisterFile(filePath, System.IO.Path.GetFileName(filePath));
                if (regJson.StartsWith("error:")) return regJson;

                using var doc = JsonDocument.Parse(regJson);
                var root = doc.RootElement;
                var token        = root.GetProperty("token").GetString() ?? "";
                var pathTemplate = root.GetProperty("path_template").GetString() ?? "";
                var port         = root.GetProperty("port").GetInt32();

                _activeProxyToken = token;

                // Rule 1: build the proxy URL — never pass filePath directly to the renderer.
                var proxyUrl = $"http://127.0.0.1:{port}{pathTemplate}";

                // Cast via AVTransport SOAP.
                var bytes = System.Text.Encoding.UTF8.GetBytes(dev.Location + '\0');
                var locPtr = Marshal.AllocHGlobal(bytes.Length);
                try
                {
                    Marshal.Copy(bytes, 0, locPtr, bytes.Length);
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
