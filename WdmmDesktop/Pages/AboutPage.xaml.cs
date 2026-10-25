using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Navigation;
using WdmmDesktop.Generated;

namespace WdmmDesktop.Pages
{
    public sealed partial class AboutPage : Page
    {
        public AboutPage() => InitializeComponent();

        protected override void OnNavigatedTo(NavigationEventArgs e)
        {
            base.OnNavigatedTo(e);
            LoadInfo();
        }

        private void LoadInfo()
        {
            string ver = "(FFI not loaded)";
            try { ver = NativeMethods.GetVersion(); } catch { }

            VersionText.Text = $"wdmm-core {ver}";
            FfiDetailText.Text = $"Library version: {ver}";

            try
            {
                bool running = NativeMethods.IsOpenApiRunning();
                int port     = NativeMethods.OpenApiPort();
                ApiStatusText.Text = running ? "OpenAPI: running" : "OpenAPI: stopped";
                ApiPortText.Text   = running ? $"(:{port})" : "";
            }
            catch
            {
                ApiStatusText.Text = "OpenAPI: unavailable (FFI not loaded)";
                ApiPortText.Text   = "";
            }

            // Parity matrix — honest done / in progress / blocked.
            // Keep in sync with docs/DESKTOP-PARITY.md. Never mark stubs as real.
            ParityText.Text =
                "Folders    done   — user-folder tree via FFI (我的文件夹 root)\n"     +
                "Playlists  done   — FFI (PlaylistList/Create/Delete/ListEntries)\n"    +
                "              no OpenAPI on this path\n"                                +
                "Music      done   — wdmm_track_list / search (FFI)\n"                  +
                "Cast       done   — CastDialog (device picker), no nav tab\n"          +
                "              DLNA→proxy only, single-file mutex, USN prefs\n"        +
                "Settings   part   — 7 sections; Home/Theme/Sync live, others 待 FFI\n" +
                "Tasks      done   — wdmm_queue_status_json / cancel (FFI)\n"            +
                "Network    part   — wdmm_sync_trigger; accounts local JSON\n"          +
                "Manga      blocked— no manga FFI (wdmm_manga_* not exported)\n"        +
                "OpenAPI    opt-in — third-party only (Settings → 第三方/开发者)\n"     +
                "Local-lib  n/a    — post-Rust-refactor feature; out of scope now";
        }
    }
}
