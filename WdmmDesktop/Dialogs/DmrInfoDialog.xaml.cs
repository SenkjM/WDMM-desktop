using Microsoft.UI.Xaml.Controls;
using WdmmDesktop.Models;

namespace WdmmDesktop.Dialogs
{
    public sealed partial class DmrInfoDialog : ContentDialog
    {
        public DmrInfoDialog(DmrDevice device, DmrPref pref)
        {
            InitializeComponent();

            FriendlyNameText.Text = device.FriendlyName;
            UsnText.Text          = device.Usn;
            LocationText.Text     = device.Location;
            AvTransportText.Text  = string.IsNullOrEmpty(device.AvTransportUrl)
                                        ? "(not discovered)"
                                        : device.AvTransportUrl;

            CompatText.Text = pref.ForceDirect
                ? "Direct stream  — proxy URL sent as-is to the renderer (no transcode)."
                : "HLS transcode  — blocked until wdmm-core feat/proxy-hls-ffmpeg merges." +
                  " Cast page will refuse and show an error if this mode is chosen.";
        }
    }
}
