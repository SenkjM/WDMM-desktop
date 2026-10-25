using System;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using WdmmDesktop.Models;
using WdmmDesktop.Services;

namespace WdmmDesktop.Dialogs
{
    public sealed partial class DmrInfoDialog : ContentDialog
    {
        private readonly DmrDevice _device;
        private DmrPref _pref;

        /// <summary>Non-null when the user saved prefs this session.</summary>
        public DmrPref? SavedPref { get; private set; }

        public DmrInfoDialog(DmrDevice device, DmrPref pref)
        {
            InitializeComponent();
            _device = device;
            _pref = pref;
            if (string.IsNullOrEmpty(_pref.Usn)) _pref.Usn = device.Usn;

            FriendlyNameText.Text = device.FriendlyName;
            UsnText.Text          = device.Usn;
            LocationText.Text     = device.Location;
            AvTransportText.Text  = string.IsNullOrEmpty(device.AvTransportUrl)
                                        ? "(not discovered)"
                                        : device.AvTransportUrl;
            OverrideNameBox.Text  = pref.OverrideName ?? "";
            ForceDirectToggle.IsOn = pref.ForceDirect;

            CompatText.Text = pref.ForceDirect
                ? "Direct stream — proxy URL sent as-is (no transcode)."
                : "HLS transcode — wdmm_proxy_register_hls with device_id = USN.";

            PrimaryButtonClick += OnPrimarySave;
        }

        private void ForceDirectToggle_Toggled(object sender, RoutedEventArgs e)
        {
            CompatText.Text = ForceDirectToggle.IsOn
                ? "Direct stream — proxy URL sent as-is (no transcode)."
                : "HLS transcode — wdmm_proxy_register_hls with device_id = USN.";
        }

        private void OnPrimarySave(ContentDialog sender, ContentDialogButtonClickEventArgs args)
        {
            _pref.Usn = _device.Usn;
            _pref.ForceDirect = ForceDirectToggle.IsOn;
            var name = OverrideNameBox.Text?.Trim();
            _pref.OverrideName = string.IsNullOrEmpty(name) ? null : name;
            _pref.UpdatedAt = DateTime.UtcNow.ToString("o");
            var r = DmrPrefsStore.Upsert(_pref);
            if (r.StartsWith("error:", StringComparison.Ordinal))
            {
                args.Cancel = true;
                SaveErrorText.Text = r;
                SaveErrorText.Visibility = Visibility.Visible;
                return;
            }
            SavedPref = _pref;
        }
    }
}
