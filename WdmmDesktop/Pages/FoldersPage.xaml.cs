using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using System.Text.Json.Serialization;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Input;
using Microsoft.UI.Xaml.Navigation;
using WdmmDesktop.Generated;
using WdmmDesktop.Models;

namespace WdmmDesktop.Pages
{
    /// <summary>
    /// Folders page — Android user-folder tree (我的文件夹 virtual root).
    /// Data comes exclusively from wdmm_user_folder_list_children / _create /
    /// _rename / _delete / _list_items FFI. No OS path browsing.
    /// </summary>
    public sealed partial class FoldersPage : Page
    {
        // ── Breadcrumb stack ─────────────────────────────────────────────────
        // Each entry is (id, displayName). null id = virtual root.
        private readonly List<(string? Id, string Name)> _crumbs = new();

        public FoldersPage() => InitializeComponent();

        protected override void OnNavigatedTo(NavigationEventArgs e)
        {
            base.OnNavigatedTo(e);
            // Always start at the virtual root on first load.
            NavigateTo(folderId: null, folderName: "我的文件夹");
        }

        // ── Navigation ────────────────────────────────────────────────────────

        private void NavigateTo(string? folderId, string folderName)
        {
            // Update crumb stack: if folderId is already in stack, pop back to it.
            // Otherwise push it.
            if (folderId == null)
            {
                _crumbs.Clear();
                _crumbs.Add((null, "我的文件夹"));
            }
            else
            {
                // Check whether target is already in the stack (navigating backward).
                int idx = _crumbs.FindIndex(c => c.Id == folderId);
                if (idx >= 0)
                    _crumbs.RemoveRange(idx + 1, _crumbs.Count - idx - 1);
                else
                    _crumbs.Add((folderId, folderName));
            }

            RebuildBreadcrumb();
            LoadFolder(folderId);
        }

        // ── Breadcrumb rendering ──────────────────────────────────────────────

        private void RebuildBreadcrumb()
        {
            BreadcrumbPanel.Children.Clear();
            for (int i = 0; i < _crumbs.Count; i++)
            {
                var (id, name) = _crumbs[i];
                bool isLast = i == _crumbs.Count - 1;

                if (isLast)
                {
                    // Current folder: plain bold text
                    BreadcrumbPanel.Children.Add(new TextBlock
                    {
                        Text = name,
                        FontSize = 12,
                        FontWeight = Microsoft.UI.Text.FontWeights.SemiBold,
                        VerticalAlignment = VerticalAlignment.Center,
                        Margin = new Thickness(0, 0, 2, 0)
                    });
                }
                else
                {
                    // Ancestor: hyperlink-style button
                    var btn = new Button
                    {
                        Content = name,
                        FontSize = 12,
                        Background = new Microsoft.UI.Xaml.Media.SolidColorBrush(
                            Microsoft.UI.Colors.Transparent),
                        BorderThickness = new Thickness(0),
                        Padding = new Thickness(0),
                        VerticalAlignment = VerticalAlignment.Center,
                        Tag = (id, name)
                    };
                    btn.Click += BreadcrumbBtn_Click;
                    BreadcrumbPanel.Children.Add(btn);

                    BreadcrumbPanel.Children.Add(new TextBlock
                    {
                        Text = "›",
                        FontSize = 14,
                        Opacity = 0.4,
                        VerticalAlignment = VerticalAlignment.Center,
                        Margin = new Thickness(2, 0, 2, 0)
                    });
                }
            }
        }

        private void BreadcrumbBtn_Click(object sender, RoutedEventArgs e)
        {
            if (sender is Button btn && btn.Tag is ValueTuple<string?, string> t)
                NavigateTo(t.Item1, t.Item2);
        }

        // ── Data loading ──────────────────────────────────────────────────────

        private void LoadFolder(string? folderId)
        {
            ItemsList.Items.Clear();
            SetEmptyState(false, "");
            StatusBar.Text = "";

            // Child folders
            var folders = new List<FolderRow>();
            try
            {
                var json = NativeMethods.UserFolderListChildren(folderId);
                if (!json.StartsWith("error:"))
                    folders = JsonSerializer.Deserialize<List<FolderRow>>(json)
                              ?? new List<FolderRow>();
                else
                    StatusBar.Text = json;
            }
            catch { /* FFI not loaded on Linux dev box */ }

            // Items at this folder
            var items = new List<ItemRow>();
            try
            {
                var itemFolderId = string.IsNullOrEmpty(folderId) ? "root" : folderId;
                var json = NativeMethods.UserFolderListItems(itemFolderId);
                if (!json.StartsWith("error:"))
                    items = JsonSerializer.Deserialize<List<ItemRow>>(json)
                            ?? new List<ItemRow>();
            }
            catch { }

            // Build display rows: folders first, then items
            foreach (var f in folders)
                ItemsList.Items.Add(new FolderDisplayRow(f));

            foreach (var it in items)
                ItemsList.Items.Add(new ItemDisplayRow(it));

            if (ItemsList.Items.Count == 0)
                SetEmptyState(true, "此文件夹为空。\n点击「New folder」创建子文件夹。");
            else
                StatusBar.Text = $"{folders.Count} 个文件夹，{items.Count} 个项目";
        }

        private void SetEmptyState(bool visible, string message)
        {
            EmptyState.Visibility = visible ? Visibility.Visible : Visibility.Collapsed;
            EmptyText.Text = message;
        }

        // ── Events ────────────────────────────────────────────────────────────

        private void ItemsList_ItemClick(object sender, ItemClickEventArgs e)
        {
            if (e.ClickedItem is FolderDisplayRow row)
                NavigateTo(row.FolderId, row.Name);
            // ItemDisplayRow: no navigation action — item detail shown inline via buttons
        }

        private void ItemsList_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            // Selection used only to highlight; ItemClick enters folders.
        }

        // ── New folder ────────────────────────────────────────────────────────

        private async void NewFolderBtn_Click(object sender, RoutedEventArgs e)
        {
            var nameBox = new TextBox
            {
                PlaceholderText = "Folder name",
                MinWidth = 260
            };
            var dlg = new ContentDialog
            {
                Title             = "New folder",
                Content           = nameBox,
                PrimaryButtonText = "Create",
                CloseButtonText   = "Cancel",
                XamlRoot          = XamlRoot
            };

            if (await dlg.ShowAsync() != ContentDialogResult.Primary) return;

            var name = nameBox.Text.Trim();
            if (string.IsNullOrEmpty(name)) return;

            // Current folder id (null = virtual root)
            string? parentId = _crumbs.Count > 0 ? _crumbs[^1].Id : null;
            var createJson = parentId == null
                ? $"{{\"name\":\"{EscapeJson(name)}\",\"sort_order\":0}}"
                : $"{{\"name\":\"{EscapeJson(name)}\",\"parent_id\":\"{EscapeJson(parentId)}\",\"sort_order\":0}}";

            try
            {
                var result = NativeMethods.UserFolderCreate(createJson);
                if (result.StartsWith("error:"))
                {
                    StatusBar.Text = result;
                    return;
                }
                // Reload current folder
                LoadFolder(parentId);
            }
            catch (Exception ex)
            {
                StatusBar.Text = $"Create failed: {ex.Message}";
            }
        }

        // ── Rename ────────────────────────────────────────────────────────────

        private async void RenameBtn_Click(object sender, RoutedEventArgs e)
        {
            if (sender is not Button btn || btn.Tag is not FolderDisplayRow row) return;

            var nameBox = new TextBox
            {
                Text     = row.Name,
                MinWidth = 260
            };
            var dlg = new ContentDialog
            {
                Title             = "Rename folder",
                Content           = nameBox,
                PrimaryButtonText = "Rename",
                CloseButtonText   = "Cancel",
                XamlRoot          = XamlRoot
            };

            if (await dlg.ShowAsync() != ContentDialogResult.Primary) return;

            var newName = nameBox.Text.Trim();
            if (string.IsNullOrEmpty(newName) || newName == row.Name) return;

            try
            {
                var result = NativeMethods.UserFolderRename(row.FolderId, newName);
                if (result.StartsWith("error:"))
                    StatusBar.Text = result;
                else
                {
                    // If renamed folder is in breadcrumb, update display name
                    for (int i = 0; i < _crumbs.Count; i++)
                        if (_crumbs[i].Id == row.FolderId)
                            _crumbs[i] = (row.FolderId, newName);
                    RebuildBreadcrumb();

                    string? currentId = _crumbs.Count > 0 ? _crumbs[^1].Id : null;
                    LoadFolder(currentId);
                }
            }
            catch (Exception ex)
            {
                StatusBar.Text = $"Rename failed: {ex.Message}";
            }
        }

        // ── Delete ────────────────────────────────────────────────────────────

        private async void DeleteBtn_Click(object sender, RoutedEventArgs e)
        {
            if (sender is not Button btn || btn.Tag is not FolderDisplayRow row) return;

            var confirmDlg = new ContentDialog
            {
                Title             = "Delete folder",
                Content           = $"Delete \"{row.Name}\" and all its contents? This cannot be undone.",
                PrimaryButtonText = "Delete",
                CloseButtonText   = "Cancel",
                XamlRoot          = XamlRoot,
                DefaultButton     = ContentDialogButton.Close
            };

            if (await confirmDlg.ShowAsync() != ContentDialogResult.Primary) return;

            try
            {
                var result = NativeMethods.UserFolderDelete(row.FolderId);
                if (result.StartsWith("error:"))
                    StatusBar.Text = result;
                else
                {
                    string? currentId = _crumbs.Count > 0 ? _crumbs[^1].Id : null;
                    LoadFolder(currentId);
                }
            }
            catch (Exception ex)
            {
                StatusBar.Text = $"Delete failed: {ex.Message}";
            }
        }

        // ── Add item to folder ────────────────────────────────────────────────

        private async void AddItemBtn_Click(object sender, RoutedEventArgs e)
        {
            string? currentFolderId = _crumbs.Count > 0 ? _crumbs[^1].Id : null;

            // Load track list for picker (artist / album / track grouping)
            var tracks = new List<TrackItem>();
            try
            {
                var json = NativeMethods.TrackList();
                if (!json.StartsWith("error:"))
                    tracks = JsonSerializer.Deserialize<List<TrackItem>>(json) ?? new();
            }
            catch { /* FFI not loaded */ }

            var allItems = BuildPickerItems(tracks);

            // Build picker UI
            var searchBox = new TextBox
            {
                PlaceholderText = "Search tracks, albums, artists…",
                MinWidth        = 340
            };
            var resultList = new ListView
            {
                MinHeight     = 300,
                SelectionMode = ListViewSelectionMode.Single
            };
            foreach (var it in allItems)
                resultList.Items.Add(it);

            searchBox.TextChanged += (_, _) =>
            {
                var q = searchBox.Text.Trim();
                resultList.Items.Clear();
                foreach (var it in allItems)
                    if (q.Length == 0 || it.DisplayName.Contains(q, StringComparison.OrdinalIgnoreCase))
                        resultList.Items.Add(it);
            };

            var panel = new StackPanel { Spacing = 8 };
            panel.Children.Add(searchBox);
            panel.Children.Add(resultList);

            var dlg = new ContentDialog
            {
                Title             = "Add item to folder",
                Content           = panel,
                PrimaryButtonText = "Add",
                CloseButtonText   = "Cancel",
                XamlRoot          = XamlRoot
            };
            if (await dlg.ShowAsync() != ContentDialogResult.Primary) return;
            if (resultList.SelectedItem is not PickerItem picked) return;

            var folderId  = string.IsNullOrEmpty(currentFolderId) ? "root" : currentFolderId;
            var addJson   = $"{{\"folder_id\":\"{EscapeJson(folderId)}\","          +
                             $"\"ref_kind\":\"{EscapeJson(picked.RefKind)}\","        +
                             $"\"ref_key\":\"{EscapeJson(picked.RefKey)}\","          +
                             "\"sort_order\":0}";

            try
            {
                var result = NativeMethods.UserFolderAddItem(addJson);
                if (result.StartsWith("error:")) { StatusBar.Text = result; return; }
                LoadFolder(currentFolderId);
            }
            catch (Exception ex) { StatusBar.Text = $"Add failed: {ex.Message}"; }
        }

        private static List<PickerItem> BuildPickerItems(List<TrackItem> tracks)
        {
            var items   = new List<PickerItem>();
            var albums  = new SortedSet<string>(StringComparer.OrdinalIgnoreCase);
            var artists = new SortedSet<string>(StringComparer.OrdinalIgnoreCase);

            foreach (var t in tracks)
            {
                if (!string.IsNullOrEmpty(t.Album))  albums.Add(t.Album);
                if (!string.IsNullOrEmpty(t.Artist)) artists.Add(t.Artist);
            }

            foreach (var a in artists)
                items.Add(new PickerItem("artist", a, $"🎤 {a}", "Artist"));
            foreach (var a in albums)
                items.Add(new PickerItem("album", a, $"💿 {a}", "Album"));
            foreach (var t in tracks.OrderBy(x => x.Title))
                items.Add(new PickerItem("track", t.Id, $"♪ {t.Title}", t.SubLine));

            return items;
        }

        // ── Remove item from folder ───────────────────────────────────────────

        private async void RemoveItemBtn_Click(object sender, RoutedEventArgs e)
        {
            if (sender is not Button btn || btn.Tag is not ItemDisplayRow row) return;

            var dlg = new ContentDialog
            {
                Title             = "Remove item",
                Content           = $"Remove \"{row.Name}\" from this folder?",
                PrimaryButtonText = "Remove",
                CloseButtonText   = "Cancel",
                XamlRoot          = XamlRoot,
                DefaultButton     = ContentDialogButton.Close
            };
            if (await dlg.ShowAsync() != ContentDialogResult.Primary) return;

            try
            {
                var result = NativeMethods.UserFolderRemoveItem(row.FolderId, row.RefKind, row.RefKey);
                if (result.StartsWith("error:")) { StatusBar.Text = result; return; }
                string? currentId = _crumbs.Count > 0 ? _crumbs[^1].Id : null;
                LoadFolder(currentId);
            }
            catch (Exception ex) { StatusBar.Text = $"Remove failed: {ex.Message}"; }
        }

        // ── Reorder items ─────────────────────────────────────────────────────

        private void MoveItemUpBtn_Click(object sender, RoutedEventArgs e)
        {
            if (sender is not Button btn || btn.Tag is not ItemDisplayRow row) return;
            ReorderItem(row, -1);
        }

        private void MoveItemDownBtn_Click(object sender, RoutedEventArgs e)
        {
            if (sender is not Button btn || btn.Tag is not ItemDisplayRow row) return;
            ReorderItem(row, +1);
        }

        private void ReorderItem(ItemDisplayRow row, int delta)
        {
            var items = ItemsList.Items.OfType<ItemDisplayRow>().ToList();
            var idx   = items.FindIndex(r =>
                r.FolderId == row.FolderId &&
                r.RefKind  == row.RefKind  &&
                r.RefKey   == row.RefKey);
            if (idx < 0) return;
            var newIdx = idx + delta;
            if (newIdx < 0 || newIdx >= items.Count) return;

            // Swap
            (items[idx], items[newIdx]) = (items[newIdx], items[idx]);

            // Build set_items JSON with contiguous sort_order values
            var parts = items.Select((it, i) =>
                $"{{\"folder_id\":\"{EscapeJson(it.FolderId)}\"," +
                $"\"ref_kind\":\"{EscapeJson(it.RefKind)}\"," +
                $"\"ref_key\":\"{EscapeJson(it.RefKey)}\"," +
                $"\"sort_order\":{i}}}");
            var jsonArray = $"[{string.Join(",", parts)}]";

            string? currentFolderId = _crumbs.Count > 0 ? _crumbs[^1].Id : null;
            var folderId = string.IsNullOrEmpty(currentFolderId) ? "root" : currentFolderId;

            try
            {
                var result = NativeMethods.UserFolderSetItems(folderId, jsonArray);
                if (result.StartsWith("error:")) { StatusBar.Text = result; return; }
                LoadFolder(currentFolderId);
            }
            catch (Exception ex) { StatusBar.Text = $"Reorder failed: {ex.Message}"; }
        }

        // ── Helpers ───────────────────────────────────────────────────────────

        private static string EscapeJson(string s)
            => s.Replace("\\", "\\\\").Replace("\"", "\\\"");

        // ── Data models ───────────────────────────────────────────────────────

        // Raw FFI folder JSON shape
        public sealed class FolderRow
        {
            [JsonPropertyName("id")]         public string Id        { get; set; } = "";
            [JsonPropertyName("name")]       public string Name      { get; set; } = "";
            [JsonPropertyName("parent_id")]  public string? ParentId { get; set; }
            [JsonPropertyName("sort_order")] public int SortOrder    { get; set; }
        }

        // Raw FFI item JSON shape
        public sealed class ItemRow
        {
            [JsonPropertyName("folder_id")]  public string FolderId  { get; set; } = "";
            [JsonPropertyName("ref_kind")]   public string RefKind   { get; set; } = "";
            [JsonPropertyName("ref_key")]    public string RefKey    { get; set; } = "";
            [JsonPropertyName("sort_order")] public int SortOrder    { get; set; }
            [JsonPropertyName("added_at")]   public long AddedAt     { get; set; }
        }

        // Display row for a folder (used in ListView DataTemplate binding)
        public sealed class FolderDisplayRow
        {
            public string  FolderId   { get; }
            public string  Name       { get; }
            public bool    IsFolder   => true;
            public bool    IsItem     => false;
            public string  IconGlyph  => "";   // Folder (matches sidebar Folders glyph)
            public string  SubLine    => "文件夹";

            public FolderDisplayRow(FolderRow r)
            {
                FolderId = r.Id;
                Name     = r.Name;
            }
        }

        // Display row for a leaf item (used in ListView DataTemplate binding)
        public sealed class ItemDisplayRow
        {
            public string  FolderId   { get; }
            public string  RefKind    { get; }
            public string  RefKey     { get; }
            public int     SortOrder  { get; }
            public string  Name       { get; }
            public bool    IsFolder   => false;
            public bool    IsItem     => true;
            public string  IconGlyph  { get; }
            public string  SubLine    { get; }

            public ItemDisplayRow(ItemRow r)
            {
                FolderId  = r.FolderId;
                RefKind   = r.RefKind;
                RefKey    = r.RefKey;
                SortOrder = r.SortOrder;
                Name      = r.RefKey;
                SubLine   = r.RefKind;
                IconGlyph = r.RefKind switch
                {
                    "track"  => "",  // Music note
                    "album"  => "",  // Album / disc
                    "artist" => "",  // Contact / artist
                    _        => ""   // Generic item / file
                };
            }
        }

        // Picker entry shown in the "Add item" dialog.
        public sealed class PickerItem
        {
            public string RefKind    { get; }
            public string RefKey     { get; }
            public string DisplayName{ get; }
            public string SubLine    { get; }

            public PickerItem(string refKind, string refKey, string displayName, string subLine)
            {
                RefKind     = refKind;
                RefKey      = refKey;
                DisplayName = displayName;
                SubLine     = subLine;
            }

            public override string ToString() => DisplayName;
        }
    }
}
