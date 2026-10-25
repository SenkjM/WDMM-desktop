using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Navigation;

namespace WdmmDesktop.Pages
{
    public sealed partial class ShellPage : Page
    {
        public ShellPage()
        {
            InitializeComponent();
        }

        protected override void OnNavigatedTo(NavigationEventArgs e)
        {
            base.OnNavigatedTo(e);
            if (e.Parameter is string title && !string.IsNullOrEmpty(title))
            {
                PageTitle.Text = title;
                PageIcon.Glyph = title switch
                {
                    "Folders"   => "",
                    "Music"     => "",
                    "Playlists" => "",
                    "Manga"     => "",
                    "Network"   => "",
                    "Tasks"     => "",
                    "About"     => "",
                    _           => "",
                };
            }
        }
    }
}
