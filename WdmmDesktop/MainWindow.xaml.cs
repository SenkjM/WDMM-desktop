using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;
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

        public MainWindow()
        {
            InitializeComponent();
            Title = "WDMM";

            SetupTitleBar();
            SetupFfi();
            CollectNavLabels();

            // Start on the first destination
            ContentFrame.Navigate(typeof(ShellPage), "Folders");
        }

        // ---------- title bar ----------

        private void SetupTitleBar()
        {
            ExtendsContentIntoTitleBar = true;
            SetTitleBar(AppTitleBar);
        }

        // ---------- FFI smoke ----------

        private void SetupFfi()
        {
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
        }

        // ---------- sidebar toggle ----------

        private void CollectNavLabels()
        {
            _navLabels = new List<TextBlock>
            {
                LblFolders, LblMusic, LblPlaylists,
                LblManga, LblNetwork, LblTasks, LblAbout,
                LblSettings
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
                SidebarColumn.Width = new GridLength(220);
                CollapseBtn.Visibility = Visibility.Visible;
                ExpandBtn.Visibility = Visibility.Collapsed;
                foreach (var lbl in _navLabels) lbl.Visibility = Visibility.Visible;
            }
            else
            {
                SidebarColumn.Width = new GridLength(52);
                CollapseBtn.Visibility = Visibility.Collapsed;
                ExpandBtn.Visibility = Visibility.Visible;
                foreach (var lbl in _navLabels) lbl.Visibility = Visibility.Collapsed;
            }
        }

        // ---------- navigation ----------

        private void NavItem_Checked(object sender, RoutedEventArgs e)
        {
            if (sender is RadioButton rb && ContentFrame != null)
            {
                var destination = rb.Tag as string ?? "Folders";
                ContentFrame.Navigate(typeof(ShellPage), destination);
            }
        }

        // ---------- settings modal ----------

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
