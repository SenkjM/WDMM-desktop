using Microsoft.UI.Xaml;

namespace WdmmDesktop
{
    public partial class App : Application
    {
        /// <summary>
        /// The single application window. Exposed so pages can reach the main
        /// window to request sidebar selection changes (e.g. MusicPage → CastPage).
        /// </summary>
        public static MainWindow? MainWindow { get; private set; }

        public App()
        {
            InitializeComponent();
        }

        protected override void OnLaunched(LaunchActivatedEventArgs args)
        {
            MainWindow = new MainWindow();
            MainWindow.Activate();
        }
    }
}
