using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using WdmmDesktop.Generated;

namespace WdmmDesktop.Dialogs
{
    public sealed partial class SettingsDialog : ContentDialog
    {
        public SettingsDialog(string ffiVersion)
        {
            InitializeComponent();
            FfiVersionText.Text = $"wdmm-core {ffiVersion}";
            LoadFfiState();
            PrimaryButtonClick += SettingsDialog_PrimaryButtonClick;
        }

        // Populate port and token from FFI if available.
        // wdmm_ffi.dll is absent on Linux dev machines — tolerate load failure.
        private void LoadFfiState()
        {
            try
            {
                var port = NativeMethods.OpenApiPort();
                if (port > 0)
                    PortBox.Text = port.ToString();

                var token = NativeMethods.TokenGet();
                if (!string.IsNullOrEmpty(token))
                    TokenBox.Text = token;
            }
            catch
            {
                // FFI not loaded — leave defaults, Save will no-op.
            }
        }

        // Save: (re-)start the OpenAPI server on the requested port,
        // then refresh the displayed port + token from the live FFI.
        private void SettingsDialog_PrimaryButtonClick(
            ContentDialog sender,
            ContentDialogButtonClickEventArgs args)
        {
            var deferral = args.GetDeferral();
            try
            {
                if (!ushort.TryParse(PortBox.Text.Trim(), out var port) || port < 1024)
                {
                    ShowStatus("Port must be 1024–65535.", isError: true);
                    args.Cancel = true;
                    return;
                }

                try
                {
                    var result = NativeMethods.StartOpenApi(port);
                    if (result.StartsWith("error:"))
                    {
                        ShowStatus(result, isError: true);
                        args.Cancel = true;
                        return;
                    }

                    ShowStatus("", isError: false);

                    // result is the bearer token on success; re-read to confirm.
                    var token = NativeMethods.TokenGet();
                    TokenBox.Text = string.IsNullOrEmpty(token) ? result : token;

                    var boundPort = NativeMethods.OpenApiPort();
                    if (boundPort > 0)
                        PortBox.Text = boundPort.ToString();
                }
                catch
                {
                    // FFI not loaded (Linux dev) — treat Save as a no-op and close.
                }
            }
            finally
            {
                deferral.Complete();
            }
        }

        // Rotate the bearer token and refresh the display.
        private void RotateTokenBtn_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                var newToken = NativeMethods.TokenRotate();
                if (newToken.StartsWith("error:"))
                {
                    ShowStatus(newToken, isError: true);
                }
                else
                {
                    TokenBox.Text = newToken;
                    ShowStatus("Token rotated.", isError: false);
                }
            }
            catch
            {
                ShowStatus("FFI not loaded — rotate unavailable.", isError: true);
            }
        }

        private void ShowStatus(string message, bool isError)
        {
            if (string.IsNullOrEmpty(message))
            {
                SaveStatusText.Visibility = Visibility.Collapsed;
                return;
            }
            SaveStatusText.Text = message;
            SaveStatusText.Foreground = isError
                ? (Microsoft.UI.Xaml.Media.Brush)Application.Current.Resources["SystemFillColorCriticalBrush"]
                : (Microsoft.UI.Xaml.Media.Brush)Application.Current.Resources["TextFillColorPrimaryBrush"];
            SaveStatusText.Visibility = Visibility.Visible;
        }
    }
}
