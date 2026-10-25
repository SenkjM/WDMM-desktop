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
                "Wave D (2026-10-25) — Manga FFI wire + queue concurrency\n\n"              +
                "Folders    done   — mount UX: add/remove/reorder track/album/artist\n"   +
                "              wdmm_user_folder_add_item / remove_item / set_items FFI\n" +
                "Playlists  done   — set_entries edit UI: add/remove/reorder + FFI\n"     +
                "Music      done   — wdmm_track_list / search (FFI)\n"                    +
                "Cast       done   — CastDialog only; CastPage removed Wave C\n"          +
                "              DLNA→proxy, single-file mutex, USN prefs\n"                +
                "Settings   done   — 7 sections; all KV-backed controls live Wave C\n"    +
                "              Queue concurrency slider live Wave D (Core fdb05df)\n"     +
                "Tasks      done   — wdmm_queue_status_json / cancel (FFI)\n"             +
                "File source done — dir browse via wdmm_webdav_*; sources via wdmm_account_*\n"     +
                "Settings   done   — TabView; Sync KV + sync_trigger + backup FFI\n" +
                "Cast prefs done   — wdmm_dmr_prefs_* (Core dmr_prefs.db)\n" +
                "Manga      done   — list/get/pages + upsert/set_progress/delete (FFI)\n" +
                "              list + detail via wdmm_manga_get / list_pages\n"                      +
                "OpenAPI    opt-in — third-party only (Settings → 第三方/开发者)\n"      +
                "Local-lib  n/a    — post-Rust-refactor feature; out of scope";
        }
    }
}
