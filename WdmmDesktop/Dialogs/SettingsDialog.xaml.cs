using Microsoft.UI.Xaml.Controls;

namespace WdmmDesktop.Dialogs
{
    public sealed partial class SettingsDialog : ContentDialog
    {
        public SettingsDialog(string ffiVersion)
        {
            InitializeComponent();
            FfiVersionText.Text = $"wdmm-core {ffiVersion}";
        }
    }
}
