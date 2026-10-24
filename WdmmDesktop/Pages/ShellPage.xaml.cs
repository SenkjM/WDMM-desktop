using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;

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
            }
        }
    }
}
