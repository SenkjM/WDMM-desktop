using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using System;
using System.Runtime.InteropServices;
using WdmmDesktop.Generated;

namespace WdmmDesktop
{
    public sealed partial class MainWindow : Window
    {
        public MainWindow()
        {
            InitializeComponent();
            Title = "WDMM Desktop";

            // FFI smoke test — call wdmm_version() from wdmm-ffi
            try
            {
                var version = NativeMethods.wdmm_version();
                var versionStr = Marshal.PtrToStringUTF8(version.ptr) ?? "(null)";
                StatusTextBlock.Text = $"WDMM Core v{versionStr}";
                NativeMethods.wdmm_ffi_free_string(version);
            }
            catch (Exception ex)
            {
                StatusTextBlock.Text = $"FFI load error: {ex.Message}";
            }
        }

        private void ToggleOpenApi_Click(object sender, RoutedEventArgs e)
        {
            // TODO: call wdmm-openapi start/stop via FFI
            StatusTextBlock.Text = "OpenAPI toggle not yet implemented";
        }
    }
}