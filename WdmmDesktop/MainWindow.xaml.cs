using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using System;
using System.Runtime.InteropServices;

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
                // Uncomment after csbindgen bindings generated:
                // var version = NativeMethods.wdmm_version();
                // var versionStr = Marshal.PtrToStringAnsi(version.ptr);
                // StatusTextBlock.Text = $"WDMM Core v{versionStr}";
                // NativeMethods.wdmm_ffi_free_string(version);

                StatusTextBlock.Text = "Ready (FFI not yet hooked)";
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
