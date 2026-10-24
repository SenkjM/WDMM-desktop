using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Navigation;

namespace WdmmDesktop.Pages
{
    // Manga storage tables (manga_series / manga_chapters) exist in wdmm-core at d8b2217,
    // but no CRUD methods, FFI exports, or OpenAPI routes are present yet.
    // This page is a library-shaped stub that:
    //   • always shows the blocked InfoBar with the exact API names missing
    //   • renders placeholder series cards so the page has the right visual shape
    //   • leaves Refresh disabled (no-op) until APIs land
    //
    // Long-term wiring path (when wdmm-core exports them):
    //   FFI:    wdmm_manga_list()   → JSON array of manga_series rows
    //           wdmm_manga_chapters(series_id) → JSON array of chapter rows
    //   Or via OpenAPI: GET /v1/manga  /  GET /v1/manga/:id/chapters
    public sealed partial class MangaPage : Page
    {
        public MangaPage() => InitializeComponent();

        protected override void OnNavigatedTo(NavigationEventArgs e)
        {
            base.OnNavigatedTo(e);
            PopulatePlaceholders();
        }

        private void PopulatePlaceholders()
        {
            // Placeholder items give the grid its correct shape while the real API is absent.
            var items = new[]
            {
                new SeriesPlaceholder("Series 1",  "—"),
                new SeriesPlaceholder("Series 2",  "—"),
                new SeriesPlaceholder("Series 3",  "—"),
                new SeriesPlaceholder("Series 4",  "—"),
                new SeriesPlaceholder("Series 5",  "—"),
                new SeriesPlaceholder("Series 6",  "—"),
            };

            SeriesGrid.ItemsSource = items;
            StatusBar.Text = "No data — wdmm_manga_list not yet available";
        }

        private void RefreshBtn_Click(object sender, RoutedEventArgs e)
        {
            // No-op until the API is wired; the InfoBar explains why.
        }

        /// <summary>Placeholder POCO for the greyed-out series cards.</summary>
        private sealed class SeriesPlaceholder
        {
            public string Title       { get; }
            public string ChapterHint { get; }

            public SeriesPlaceholder(string title, string chapterHint)
            {
                Title = title; ChapterHint = chapterHint;
            }
        }
    }
}
