using System;
using System.Collections.Generic;
using System.Text.Json;
using System.Text.Json.Serialization;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Input;
using Microsoft.UI.Xaml.Navigation;
using WdmmDesktop.Generated;

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

        private void ItemsList_DoubleTapped(object sender, DoubleTappedRoutedEventArgs e)
        {
            if (ItemsList.SelectedItem is FolderDisplayRow row)
                NavigateTo(row.FolderId, row.Name);
        }

        private void ItemsList_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            // Selection used only to highlight; double-tap enters folder.
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

        // ── Helpers ───────────────────────────────────────────────────────────

        private static string EscapeJson(string s)
            => s.Replace("\\", "\\\\").Replace("\"", "\\\"");

        // ── Data models ───────────────────────────────────────────────────────

        // Raw FFI folder JSON shape
        private sealed class FolderRow
        {
            [JsonPropertyName("id")]         public string Id        { get; set; } = "";
            [JsonPropertyName("name")]       public string Name      { get; set; } = "";
            [JsonPropertyName("parent_id")]  public string? ParentId { get; set; }
            [JsonPropertyName("sort_order")] public int SortOrder    { get; set; }
        }

        // Raw FFI item JSON shape
        private sealed class ItemRow
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
            public string  Name       { get; }
            public bool    IsFolder   => false;
            public string  IconGlyph  { get; }
            public string  SubLine    { get; }

            public ItemDisplayRow(ItemRow r)
            {
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
    }
}
