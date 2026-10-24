using System;
using System.Collections.Generic;
using System.IO;
using System.Runtime.InteropServices;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using WdmmDesktop.Dialogs;
using WdmmDesktop.Generated;
using WdmmDesktop.Pages;

namespace WdmmDesktop
{
    public sealed partial class MainWindow : Window
    {
        private bool _sidebarExpanded = true;
        private string _ffiVersion = "(not loaded)";

        // Labels that hide when the sidebar collapses
        private List<TextBlock> _navLabels = new();

        // Maps sidebar Tag strings → destination Page types.
        private static readonly Dictionary<string, Type> _pageMap = new()
        {
            ["Folders"]   = typeof(FoldersPage),
            ["Music"]     = typeof(MusicPage),
            ["Playlists"] = typeof(PlaylistsPage),
            ["Manga"]     = typeof(MangaPage),
            ["Network"]   = typeof(NetworkPage),
            ["Tasks"]     = typeof(TasksPage),
            ["Cast"]      = typeof(CastPage),
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
            CollectNavButtons();

            // Start on Folders
            ContentFrame.Navigate(typeof(FoldersPage));
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
            }
            catch { /* FFI not loaded (Linux dev box) — tolerate */ }
        }

        // ── Sidebar toggle ────────────────────────────────────────────────────

        private void CollectNavLabels()
        {
            _navLabels = new List<TextBlock>
            {
                LblFolders, LblMusic, LblPlaylists, LblManga,
                LblNetwork, LblTasks, LblCast, LblAbout,
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
                ["Cast"]      = NavCast,
                ["About"]     = NavAbout,
            };
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
        /// Programmatically select a sidebar item — called by pages that cross-navigate
        /// (e.g. MusicPage Cast button → CastPage).
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
