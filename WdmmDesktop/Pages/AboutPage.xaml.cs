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

            // Parity matrix — keep in sync with docs/DESKTOP-PARITY.md
            ParityText.Text =
                "Folders    part  — local browser (path bar, Up, double-click);\n"     +
                "              remote: BLOCKED wdmm_webdav_list not in FFI @ d8b2217\n" +
                "Music      real  — wdmm_track_list / search\n"                         +
                "Playlists  bridge— OpenAPI GET/POST/DELETE /v1/playlists;\n"           +
                "              direct FFI wdmm_playlist_list not yet exported\n"        +
                "Manga      stub  — BLOCKED wdmm_manga_list / chapter APIs not in\n"   +
                "              FFI or OpenAPI @ d8b2217; storage tables exist\n"        +
                "Network    part  — wdmm_sync_trigger; account CRUD local JSON\n"      +
                "Tasks      real  — wdmm_queue_status_json / cancel\n"                  +
                "Cast/DLNA  real  — wdmm_dlna_discover_json / wdmm_dlna_cast\n"        +
                "              HLS DEFERRED — pending Core R12 / proxy-hls\n"          +
                "Settings   real  — wdmm_openapi_start / token_get / token_rotate\n"   +
                "About      real  — wdmm_version";
        }
    }
}
