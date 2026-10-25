using System;
using System.Collections.Generic;
using System.IO;
using System.Runtime.InteropServices;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using WdmmDesktop.Dialogs;
using WdmmDesktop.Generated;
using WdmmDesktop.Pages;
using WdmmDesktop.Services;

namespace WdmmDesktop
{
    public sealed partial class MainWindow : Window
    {
        private bool _sidebarExpanded = true;
        private string _ffiVersion = "(not loaded)";

        // Labels that hide when the sidebar collapses
        private List<TextBlock> _navLabels = new();

        // Maps sidebar Tag strings → destination Page types.
        // Note: Cast is intentionally absent — casting is a dialog invoked from
        // MusicPage (Dialogs/CastDialog), not a sidebar destination (Android parity).
        private static readonly Dictionary<string, Type> _pageMap = new()
        {
            ["Folders"]   = typeof(FoldersPage),
            ["Music"]     = typeof(MusicPage),
            ["Playlists"] = typeof(PlaylistsPage),
            ["Manga"]     = typeof(MangaPage),
            ["Network"]   = typeof(NetworkPage),
            ["Tasks"]     = typeof(TasksPage),
            ["About"]     = typeof(AboutPage),
        };

        // Maps Tag string → RadioButton for SelectNavItem().
        private Dictionary<string, RadioButton> _navButtons = new();

        public MainWindow()
        {
            InitializeComponent();
            Title = "WDMM";

            SetupTitleBar();
            SetupFfi();
            CollectNavLabels();
            ApplyNavLoc();
            CollectNavButtons();

            // Start on user-preferred home tab (Settings → 主页与导航), default Folders.
            var prefs = LocalStore.Load("desktop_prefs.json", new Dialogs.SettingsDialog.DesktopPrefs());
            var home = string.IsNullOrWhiteSpace(prefs.DefaultHome) ? "Folders" : prefs.DefaultHome;
            if (_pageMap.TryGetValue(home, out var homePage))
                ContentFrame.Navigate(homePage);
            else
                ContentFrame.Navigate(typeof(FoldersPage));
            if (_navButtons.TryGetValue(home, out var homeBtn))
                homeBtn.IsChecked = true;
            else
                NavFolders.IsChecked = true;
        }

        // ── Title bar ─────────────────────────────────────────────────────────

        private void SetupTitleBar()
        {
            ExtendsContentIntoTitleBar = true;
            SetTitleBar(AppTitleBar);
        }

        // ── FFI smoke + library init ──────────────────────────────────────────

        private void SetupFfi()
        {
            // Version smoke
            try
            {
                var ver = NativeMethods.wdmm_version();
                _ffiVersion = Marshal.PtrToStringUTF8(ver.ptr) ?? "(null)";
                NativeMethods.wdmm_ffi_free_string(ver);
                FfiStatusStrip.Text = $"Core v{_ffiVersion}";
            }
            catch (Exception ex)
            {
                _ffiVersion = $"FFI error: {ex.Message}";
                FfiStatusStrip.Text = _ffiVersion;
            }

            // Initialise library DB (idempotent — Rust no-ops on second call).
            try
            {
                var dbDir = Path.Combine(
                    Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                    "Wdmm");
                Directory.CreateDirectory(dbDir);
                NativeMethods.LibraryInit(Path.Combine(dbDir, "library.db"));
                DmrPrefsStore.EnsureInit();
            }
            catch { /* FFI not loaded (Linux dev box) — tolerate */ }
        }

        // ── Sidebar toggle ────────────────────────────────────────────────────

        private void CollectNavLabels()
        {
            _navLabels = new List<TextBlock>
            {
                LblFolders, LblMusic, LblPlaylists, LblManga,
                LblNetwork, LblTasks, LblAbout,
                LblSettings
            };
        }

        private void CollectNavButtons()
        {
            _navButtons = new Dictionary<string, RadioButton>
            {
                ["Folders"]   = NavFolders,
                ["Music"]     = NavMusic,
                ["Playlists"] = NavPlaylists,
                ["Manga"]     = NavManga,
                ["Network"]   = NavNetwork,
                ["Tasks"]     = NavTasks,
                ["About"]     = NavAbout,
            };
        }


        public void ApplyNavLoc()
        {
            try
            {
                var tag = NativeMethods.KvGet("app_locale");
                Loc.ApplyLocale(string.IsNullOrEmpty(tag) || tag.StartsWith("error") ? "system" : tag);
            }
            catch { Loc.ApplyLocale("system"); }
            if (LblFolders != null) LblFolders.Text = Loc.Get("Nav.Folders");
            if (LblMusic != null) LblMusic.Text = Loc.Get("Nav.Music");
            if (LblPlaylists != null) LblPlaylists.Text = Loc.Get("Nav.Playlists");
            if (LblManga != null) LblManga.Text = Loc.Get("Nav.Manga");
            if (LblNetwork != null) LblNetwork.Text = Loc.Get("Nav.Network");
            if (LblTasks != null) LblTasks.Text = Loc.Get("Nav.Tasks");
            if (LblAbout != null) LblAbout.Text = Loc.Get("Nav.About");
            if (LblSettings != null) LblSettings.Text = Loc.Get("Nav.Settings");
        }

        private void ToggleSidebar_Click(object sender, RoutedEventArgs e)
        {
            _sidebarExpanded = !_sidebarExpanded;
            ApplySidebarState();
        }

        private void ApplySidebarState()
        {
            if (_sidebarExpanded)
            {
                SidebarColumn.Width       = new GridLength(220);
                CollapseBtn.Visibility    = Visibility.Visible;
                ExpandBtn.Visibility      = Visibility.Collapsed;
                foreach (var lbl in _navLabels) lbl.Visibility = Visibility.Visible;
            }
            else
            {
                SidebarColumn.Width       = new GridLength(52);
                CollapseBtn.Visibility    = Visibility.Collapsed;
                ExpandBtn.Visibility      = Visibility.Visible;
                foreach (var lbl in _navLabels) lbl.Visibility = Visibility.Collapsed;
            }
        }

        // ── Navigation ────────────────────────────────────────────────────────

        private void NavItem_Checked(object sender, RoutedEventArgs e)
        {
            if (sender is RadioButton rb && ContentFrame != null)
            {
                var tag = rb.Tag as string ?? "Folders";
                if (_pageMap.TryGetValue(tag, out var pageType))
                    ContentFrame.Navigate(pageType);
            }
        }

        /// <summary>
        /// Programmatically select a sidebar item — called by pages that cross-navigate.
        /// Cast uses Dialogs/CastDialog (not a sidebar destination).
        /// </summary>
        public void SelectNavItem(string tag)
        {
            if (_navButtons.TryGetValue(tag, out var btn))
                btn.IsChecked = true;
            // Setting IsChecked fires NavItem_Checked which navigates the frame.
        }

        // ── Settings modal ────────────────────────────────────────────────────

        private async void SettingsBtn_Click(object sender, RoutedEventArgs e)
        {
            var dialog = new SettingsDialog(_ffiVersion)
            {
                XamlRoot = Content.XamlRoot
            };
            await dialog.ShowAsync();
        }
    }
}
