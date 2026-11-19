using System;
using System.Drawing;
using System.IO;
using System.Linq;
using System.Windows.Forms;
using ExplorerTabs.Core;

namespace ExplorerTabs.App
{
    /// <summary>收藏与标签组管理：目录与标签组的集中管理。
    /// Edits stay in a private copy; the host validates and persists Result only after Save/Open.
    /// The dialog never navigates, closes or otherwise touches Explorer windows.</summary>
    internal sealed class LibraryManagerDialog : ShellDialog
    {
        private readonly WorkspaceLibrary library;
        private readonly ListView favorites = new ListView();
        private readonly ListView groups = new ListView();
        private readonly ListView groupTabs = new ListView();
        private readonly Label status = new Label();
        private readonly ActionButton openFavorite = new ActionButton("打开为新标签", true) { Width = 118, Height = 30 };
        private readonly ActionButton renameFavorite = new ActionButton("重命名…") { Width = 92, Height = 30 };
        private readonly ActionButton removeFavorite = new ActionButton("移除收藏") { Width = 82, Height = 30 };
        private readonly ActionButton openGroup = new ActionButton("追加打开", true) { Width = 112, Height = 30 };
        private readonly ActionButton renameGroup = new ActionButton("重命名…") { Width = 92, Height = 30 };
        private readonly ActionButton removeGroup = new ActionButton("删除标签组") { Width = 112, Height = 30 };
        private readonly ActionButton addGroupTab = new ActionButton("添加目录…") { Width = 96, Height = 30 };
        private readonly ActionButton editGroupTab = new ActionButton("编辑目录…") { Width = 92, Height = 30 };
        private readonly ActionButton renameGroupTab = new ActionButton("重命名…") { Width = 80, Height = 30 };
        private readonly ActionButton moveGroupTabUp = new ActionButton("上移") { Width = 56, Height = 30 };
        private readonly ActionButton moveGroupTabDown = new ActionButton("下移") { Width = 56, Height = 30 };
        private readonly ActionButton removeGroupTab = new ActionButton("移除") { Width = 56, Height = 30 };
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
            Text = "管理收藏与标签组";
            Font = new Font("Microsoft YaHei UI", 9F);
            StartPosition = FormStartPosition.CenterParent;
            MinimizeBox = false; MaximizeBox = false; ShowInTaskbar = false;
            ClientSize = new Size(940, 548);
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
            RefreshFavorites(); RefreshGroups(); RefreshGroupTabs(); UpdateStatus();
            DialogStyle.Apply(this);
        }

        private TabPage BuildFavoritesPage()
        {
            var page = new TabPage("收藏目录") { Padding = new Padding(12, 10, 12, 10), BackColor = Color.White };
            ConfigureList(favorites, "收藏目录");
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
                Text = "双击条目或点击“打开为新标签”可打开；如有修改，会一并保存。"
            };
            page.Controls.Add(favorites); page.Controls.Add(actions); page.Controls.Add(note);
            return page;
        }

        private TabPage BuildGroupsPage()
        {
            var page = new TabPage("标签组") { Padding = new Padding(12, 10, 12, 10), BackColor = Color.White };
            ConfigureList(groups, "标签组");
            groups.Columns.Add("名称", 180);
            groups.Columns.Add("标签数", 80);
            groups.DoubleClick += (_, __) => OpenSelectedGroup();
            groups.SelectedIndexChanged += (_, __) => { UpdateGroupButtons(); RefreshGroupTabs(); };
            openGroup.Click += (_, __) => OpenSelectedGroup();
            renameGroup.Click += (_, __) => RenameSelectedGroup();
            removeGroup.Click += (_, __) => RemoveSelectedGroup();
            ConfigureList(groupTabs, "标签组内容");
            groupTabs.Columns.Add("别名", 130);
            groupTabs.Columns.Add("目录", 392);
            groupTabs.SelectedIndexChanged += (_, __) => UpdateGroupTabButtons();
            groupTabs.DoubleClick += (_, __) => RenameSelectedGroupTab();
            addGroupTab.Click += (_, __) => AddGroupTab();
            editGroupTab.Click += (_, __) => ChangeSelectedGroupTabDirectory();
            renameGroupTab.Click += (_, __) => RenameSelectedGroupTab();
            moveGroupTabUp.Click += (_, __) => MoveSelectedGroupTab(-1);
            moveGroupTabDown.Click += (_, __) => MoveSelectedGroupTab(1);
            removeGroupTab.Click += (_, __) => RemoveSelectedGroupTab();
            var body = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 2, RowCount = 1, BackColor = Color.White };
            body.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 330));
            body.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
            body.Controls.Add(SplitPane(groups, new Control[] { openGroup, renameGroup, removeGroup }), 0, 0);
            body.Controls.Add(SplitPane(groupTabs, new Control[] { addGroupTab, editGroupTab, renameGroupTab, moveGroupTabUp, moveGroupTabDown, removeGroupTab }), 1, 0);
            var note = new Label
            {
                Dock = DockStyle.Top, Height = 26, ForeColor = Color.FromArgb(91, 104, 123),
                Text = "左侧选择标签组；右侧可添加 / 编辑目录、重命名别名、上移 / 下移或移除组内标签。双击组名追加打开。"
            };
            page.Controls.Add(body); page.Controls.Add(note);
            return page;
        }

        private static Panel SplitPane(Control list, Control[] actions)
        {
            var row = new FlowLayoutPanel { Dock = DockStyle.Bottom, Height = 44, FlowDirection = FlowDirection.RightToLeft, WrapContents = false, BackColor = Color.White };
            row.Controls.AddRange(actions);
            var pane = new Panel { Dock = DockStyle.Fill, BackColor = Color.White };
            pane.Controls.Add(list); pane.Controls.Add(row);
            return pane;
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

        private WorkspaceTab? SelectedGroupTab() =>
            groupTabs.SelectedItems.Count == 0 ? null : groupTabs.SelectedItems[0].Tag as WorkspaceTab;

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
                    var item = new ListViewItem(group.Name) { Tag = group, ToolTipText = group.Name + " · " + group.Tabs.Count + " 个标签" };
                    item.SubItems.Add(group.Tabs.Count + " 个标签");
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

        private void RefreshGroupTabs(WorkspaceTab? select = null)
        {
            var group = SelectedGroup();
            groupTabs.BeginUpdate();
            try
            {
                groupTabs.Items.Clear();
                if (group != null)
                    for (int i = 0; i < group.Tabs.Count; i++)
                    {
                        var tab = group.Tabs[i];
                        var item = new ListViewItem(string.IsNullOrEmpty(tab.Alias) ? "（无别名）" : tab.Alias)
                        { Tag = tab, ToolTipText = FolderTarget.Display(tab.Directory) };
                        item.SubItems.Add(FolderTarget.Display(tab.Directory));
                        if (i == group.ActiveIndex) item.ForeColor = Color.FromArgb(31, 86, 183);
                        groupTabs.Items.Add(item);
                        if (ReferenceEquals(tab, select)) item.Selected = true;
                    }
            }
            finally { groupTabs.EndUpdate(); }
            UpdateGroupTabButtons();
        }

        private void UpdateGroupTabButtons()
        {
            var group = SelectedGroup();
            var tab = SelectedGroupTab();
            int index = group != null && tab != null ? group.Tabs.IndexOf(tab) : -1;
            bool selected = index >= 0;
            editGroupTab.Enabled = renameGroupTab.Enabled = selected;
            moveGroupTabUp.Enabled = selected && index > 0;
            moveGroupTabDown.Enabled = selected && index < group!.Tabs.Count - 1;
            removeGroupTab.Enabled = selected && group!.Tabs.Count > 1;
            addGroupTab.Enabled = group != null && group.Tabs.Count < 100;
        }

        private void UpdateStatus() =>
            status.Text = Dirty ? "有未保存的修改；保存或打开条目会保存修改，取消则放弃。" : "双击条目可打开；保存或打开时提交修改，取消则放弃。";

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

        private string? PickDirectory(string description)
        {
            using (var picker = new FolderBrowserDialog { Description = description, ShowNewFolderButton = false })
            {
                if (picker.ShowDialog(this) != DialogResult.OK) return null;
                try { return FolderTarget.Normalize(picker.SelectedPath); }
                catch (Exception error) when (error is ArgumentException || error is NotSupportedException || error is PathTooLongException)
                { status.Text = error.Message; return null; }
            }
        }

        private void AddGroupTab()
        {
            var group = SelectedGroup();
            if (group == null) return;
            if (group.Tabs.Count >= 100) { status.Text = "每个标签组最多 100 个标签。"; return; }
            string? path = PickDirectory("选择要加入标签组的目录");
            if (path == null) return;
            if (group.Tabs.Any(t => FolderTarget.Same(t.Directory, path))) { status.Text = "该目录已在标签组中。"; return; }
            string? alias = AskName("标签别名", SuggestedName(path));
            if (alias == null) return;
            var added = new WorkspaceTab { Directory = path, Alias = alias };
            group.Tabs.Add(added); MarkDirty(); RefreshGroups(group.Id); RefreshGroupTabs(added);
        }

        private void ChangeSelectedGroupTabDirectory()
        {
            var group = SelectedGroup();
            var tab = SelectedGroupTab();
            if (group == null || tab == null) return;
            string? path = PickDirectory("选择标签组条目的目录");
            if (path == null) return;
            if (group.Tabs.Any(t => !ReferenceEquals(t, tab) && FolderTarget.Same(t.Directory, path))) { status.Text = "该目录已在标签组中。"; return; }
            tab.Directory = path; MarkDirty(); RefreshGroupTabs(tab);
        }

        private void RenameSelectedGroupTab()
        {
            var tab = SelectedGroupTab();
            if (tab == null) return;
            string? alias = AskName("编辑标签别名", tab.Alias ?? SuggestedName(tab.Directory));
            if (alias == null) return;
            tab.Alias = alias; MarkDirty(); RefreshGroupTabs(tab);
        }

        private void MoveSelectedGroupTab(int delta)
        {
            var group = SelectedGroup();
            var tab = SelectedGroupTab();
            if (group == null || tab == null) return;
            int index = group.Tabs.IndexOf(tab), target = index + delta;
            if (index < 0 || target < 0 || target >= group.Tabs.Count) return;
            group.Tabs[index] = group.Tabs[target]; group.Tabs[target] = tab;
            if (group.ActiveIndex == index) group.ActiveIndex = target;
            else if (group.ActiveIndex == target) group.ActiveIndex = index;
            MarkDirty(); RefreshGroupTabs(tab);
        }

        private void RemoveSelectedGroupTab()
        {
            var group = SelectedGroup();
            var tab = SelectedGroupTab();
            if (group == null || tab == null) return;
            if (group.Tabs.Count <= 1) { status.Text = "标签组至少保留 1 个标签。"; return; }
            int index = group.Tabs.IndexOf(tab);
            group.Tabs.RemoveAt(index);
            if (group.ActiveIndex > index) group.ActiveIndex--;
            else if (group.ActiveIndex == index) group.ActiveIndex = Math.Min(index, group.Tabs.Count - 1);
            MarkDirty(); RefreshGroups(group.Id); RefreshGroupTabs();
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
