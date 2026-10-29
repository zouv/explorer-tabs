using System;
using System.Drawing;
using System.IO;
using System.Linq;
using System.Windows.Forms;
using ExplorerTabs.Core;

namespace ExplorerTabs.App
{
    /// <summary>“收藏”管理：目录与标签组的集中管理。
    /// Edits stay in a private copy; the host validates and persists Result only after Save/Open.
    /// The dialog never navigates, closes or otherwise touches Explorer windows.</summary>
    internal sealed class LibraryManagerDialog : ShellDialog
    {
        private readonly WorkspaceLibrary library;
        private readonly ListView favorites = new ListView();
        private readonly ListView groups = new ListView();
        private readonly Label status = new Label();
        private readonly ActionButton openFavorite = new ActionButton("打开为新标签", true) { Width = 118, Height = 30 };
        private readonly ActionButton renameFavorite = new ActionButton("重命名…") { Width = 92, Height = 30 };
        private readonly ActionButton removeFavorite = new ActionButton("移除") { Width = 82, Height = 30 };
        private readonly ActionButton openGroup = new ActionButton("打开标签组", true) { Width = 112, Height = 30 };
        private readonly ActionButton renameGroup = new ActionButton("重命名…") { Width = 92, Height = 30 };
        private readonly ActionButton removeGroup = new ActionButton("删除标签组") { Width = 112, Height = 30 };
        private readonly ActionButton save = new ActionButton("保存", true) { Width = 90, Height = 30 };
        private readonly ActionButton cancel = new ActionButton("取消") { DialogResult = DialogResult.Cancel, Width = 90, Height = 30 };

        public WorkspaceLibrary Result { get; private set; }
        public bool Dirty { get; private set; }
        public FolderBookmark? FavoriteToOpen { get; private set; }
        public SavedWorkspace? GroupToOpen { get; private set; }

        public LibraryManagerDialog(WorkspaceLibrary current)
        {
            library = current.Copy();
            Result = current.Copy();
            Text = "“收藏”管理 · 目录与标签组";
            Font = new Font("Microsoft YaHei UI", 9F);
            StartPosition = FormStartPosition.CenterParent;
            MinimizeBox = false; MaximizeBox = false; ShowInTaskbar = false;
            ClientSize = new Size(820, 548);
            BackColor = Color.White;

            var pages = new TabControl { Dock = DockStyle.Fill };
            pages.TabPages.Add(BuildFavoritesPage());
            pages.TabPages.Add(BuildGroupsPage());

            var outer = new TableLayoutPanel { Dock = DockStyle.Fill, Padding = new Padding(14, 12, 14, 10), ColumnCount = 1, RowCount = 3, BackColor = Color.White };
            outer.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
            outer.RowStyles.Add(new RowStyle(SizeType.Absolute, 26));
            outer.RowStyles.Add(new RowStyle(SizeType.Absolute, 38));
            status.Dock = DockStyle.Fill; status.AutoEllipsis = true; status.ForeColor = Color.FromArgb(108, 122, 143);
            var buttons = new FlowLayoutPanel { Dock = DockStyle.Fill, FlowDirection = FlowDirection.RightToLeft, WrapContents = false, BackColor = Color.White };
            save.Click += (_, __) => { Result = library.Copy(); DialogResult = DialogResult.OK; };
            buttons.Controls.AddRange(new Control[] { cancel, save });
            outer.Controls.Add(pages, 0, 0); outer.Controls.Add(status, 0, 1); outer.Controls.Add(buttons, 0, 2);
            Controls.Add(outer);
            AcceptButton = save; CancelButton = cancel;
            RefreshFavorites(); RefreshGroups(); UpdateStatus();
            DialogStyle.Apply(this);
        }

        private TabPage BuildFavoritesPage()
        {
            var page = new TabPage("目录管理") { Padding = new Padding(12, 10, 12, 10), BackColor = Color.White };
            ConfigureList(favorites, "常用目录");
            favorites.Columns.Add("名称", 220);
            favorites.Columns.Add("目录", 520);
            favorites.DoubleClick += (_, __) => OpenSelectedFavorite();
            favorites.SelectedIndexChanged += (_, __) => UpdateFavoriteButtons();
            openFavorite.Click += (_, __) => OpenSelectedFavorite();
            renameFavorite.Click += (_, __) => RenameSelectedFavorite();
            removeFavorite.Click += (_, __) => RemoveSelectedFavorite();
            var add = new ActionButton("收藏目录…") { Width = 108, Height = 30 };
            add.Click += (_, __) => AddFavorite();
            var actions = new FlowLayoutPanel { Dock = DockStyle.Bottom, Height = 44, FlowDirection = FlowDirection.RightToLeft, WrapContents = false, BackColor = Color.White };
            actions.Controls.AddRange(new Control[] { openFavorite, renameFavorite, removeFavorite, add });
            var note = new Label
            {
                Dock = DockStyle.Top, Height = 26, ForeColor = Color.FromArgb(91, 104, 123),
                Text = "双击条目或点击“打开为新标签”可直接打开；修改在点击“保存”后生效。"
            };
            page.Controls.Add(favorites); page.Controls.Add(actions); page.Controls.Add(note);
            return page;
        }

        private TabPage BuildGroupsPage()
        {
            var page = new TabPage("组管理") { Padding = new Padding(12, 10, 12, 10), BackColor = Color.White };
            ConfigureList(groups, "标签组");
            groups.Columns.Add("名称", 320);
            groups.Columns.Add("目录数", 140);
            groups.DoubleClick += (_, __) => OpenSelectedGroup();
            groups.SelectedIndexChanged += (_, __) => UpdateGroupButtons();
            openGroup.Click += (_, __) => OpenSelectedGroup();
            renameGroup.Click += (_, __) => RenameSelectedGroup();
            removeGroup.Click += (_, __) => RemoveSelectedGroup();
            var actions = new FlowLayoutPanel { Dock = DockStyle.Bottom, Height = 44, FlowDirection = FlowDirection.RightToLeft, WrapContents = false, BackColor = Color.White };
            actions.Controls.AddRange(new Control[] { openGroup, renameGroup, removeGroup });
            var note = new Label
            {
                Dock = DockStyle.Top, Height = 26, ForeColor = Color.FromArgb(91, 104, 123),
                Text = "双击条目或点击“打开标签组”会追加为多个标签；删除不会关闭任何窗口。"
            };
            page.Controls.Add(groups); page.Controls.Add(actions); page.Controls.Add(note);
            return page;
        }

        private static void ConfigureList(ListView list, string accessibleName)
        {
            list.Dock = DockStyle.Fill;
            list.View = View.Details; list.FullRowSelect = true; list.MultiSelect = false;
            list.BorderStyle = BorderStyle.FixedSingle; list.ShowItemToolTips = true;
            list.HideSelection = false; list.AccessibleName = accessibleName;
        }

        private FolderBookmark? SelectedFavorite() =>
            favorites.SelectedItems.Count == 0 ? null : favorites.SelectedItems[0].Tag as FolderBookmark;

        private SavedWorkspace? SelectedGroup() =>
            groups.SelectedItems.Count == 0 ? null : groups.SelectedItems[0].Tag as SavedWorkspace;

        private void RefreshFavorites(string? selectId = null)
        {
            favorites.BeginUpdate();
            try
            {
                favorites.Items.Clear();
                foreach (var favorite in library.Favorites)
                {
                    var item = new ListViewItem(favorite.Name) { Tag = favorite, ToolTipText = FolderTarget.Display(favorite.Directory) };
                    item.SubItems.Add(FolderTarget.Display(favorite.Directory));
                    favorites.Items.Add(item);
                    if (favorite.Id == selectId) item.Selected = true;
                }
            }
            finally { favorites.EndUpdate(); }
            UpdateFavoriteButtons();
        }

        private void RefreshGroups(string? selectId = null)
        {
            groups.BeginUpdate();
            try
            {
                groups.Items.Clear();
                foreach (var group in library.Groups)
                {
                    var item = new ListViewItem(group.Name) { Tag = group, ToolTipText = group.Name + " · " + group.Tabs.Count + " 个目录" };
                    item.SubItems.Add(group.Tabs.Count + " 个目录");
                    groups.Items.Add(item);
                    if (group.Id == selectId) item.Selected = true;
                }
            }
            finally { groups.EndUpdate(); }
            UpdateGroupButtons();
        }

        private void UpdateFavoriteButtons()
        {
            bool selected = SelectedFavorite() != null;
            openFavorite.Enabled = renameFavorite.Enabled = removeFavorite.Enabled = selected;
        }

        private void UpdateGroupButtons()
        {
            bool selected = SelectedGroup() != null;
            openGroup.Enabled = renameGroup.Enabled = removeGroup.Enabled = selected;
        }

        private void UpdateStatus() =>
            status.Text = Dirty ? "有未保存的修改；点击“保存”后生效。" : "双击条目可直接打开；修改在点击“保存”后生效。";

        private void MarkDirty() { Dirty = true; UpdateStatus(); }

        private void OpenSelectedFavorite()
        {
            var favorite = SelectedFavorite();
            if (favorite == null) return;
            FavoriteToOpen = favorite.Copy();
            Result = library.Copy();
            DialogResult = DialogResult.OK;
        }

        private void RenameSelectedFavorite()
        {
            var favorite = SelectedFavorite();
            if (favorite == null) return;
            string? name = AskName("重命名收藏目录", favorite.Name);
            if (name == null) return;
            favorite.Name = name; MarkDirty(); RefreshFavorites(favorite.Id);
        }

        private void RemoveSelectedFavorite()
        {
            var favorite = SelectedFavorite();
            if (favorite == null) return;
            library.Favorites.Remove(favorite); MarkDirty(); RefreshFavorites();
        }

        private void AddFavorite()
        {
            string path;
            using (var picker = new FolderBrowserDialog { Description = "选择要收藏的普通目录", ShowNewFolderButton = false })
            {
                if (picker.ShowDialog(this) != DialogResult.OK) return;
                try { path = FolderTarget.Normalize(picker.SelectedPath); }
                catch (Exception error) when (error is ArgumentException || error is NotSupportedException || error is PathTooLongException)
                { status.Text = error.Message; return; }
            }
            if (library.Favorites.Any(f => FolderTarget.Same(f.Directory, path)))
            { status.Text = "该目录已在收藏中。"; return; }
            string? name = AskName("收藏目录", SuggestedName(path));
            if (name == null) return;
            var favorite = new FolderBookmark { Name = name, Directory = path };
            library.Favorites.Add(favorite); MarkDirty(); RefreshFavorites(favorite.Id);
        }

        private void OpenSelectedGroup()
        {
            var group = SelectedGroup();
            if (group == null) return;
            GroupToOpen = group.Copy();
            Result = library.Copy();
            DialogResult = DialogResult.OK;
        }

        private void RenameSelectedGroup()
        {
            var group = SelectedGroup();
            if (group == null) return;
            string? name = AskName("重命名标签组", group.Name);
            if (name == null) return;
            group.Name = name; MarkDirty(); RefreshGroups(group.Id);
        }

        private void RemoveSelectedGroup()
        {
            var group = SelectedGroup();
            if (group == null) return;
            library.Groups.Remove(group); MarkDirty(); RefreshGroups();
        }

        private string? AskName(string title, string value)
        {
            using (var dialog = new RenameTabDialog(value, title))
                return dialog.ShowDialog(this) == DialogResult.OK ? TabManagementPolicy.NormalizeTitle(dialog.TabTitle) : null;
        }

        private static string SuggestedName(string path)
        {
            string trimmed = path.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
            string name = Path.GetFileName(trimmed);
            return string.IsNullOrEmpty(name) ? path : name;
        }
    }
}
