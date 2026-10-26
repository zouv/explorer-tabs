using System;
using System.Collections.Generic;
using System.Drawing;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Forms;
using ExplorerTabs.Core;
namespace ExplorerTabs.App
{
    internal sealed partial class MainForm
    {
        private readonly Dictionary<ContextMenuStrip, Action> managedMenuCleanups = new Dictionary<ContextMenuStrip, Action>();
        private readonly HashSet<ContextMenuStrip> closedManagedMenus = new HashSet<ContextMenuStrip>();
        private bool disposingUi;
        private FlowLayoutPanel? commandToolbar;
        private FlowLayoutPanel? commandToolbarRight;
        private IWorkspaceLibraryStore? libraryStore;
        private WorkspaceLibrary library = new WorkspaceLibrary();
        private readonly Dictionary<WindowSession, string> directoryCache = new Dictionary<WindowSession, string>();
        private readonly Dictionary<WindowSession, WorkspaceTab> pendingClosed = new Dictionary<WindowSession, WorkspaceTab>();
        private readonly Dictionary<WindowSession, CancellationTokenSource> closeReading = new Dictionary<WindowSession, CancellationTokenSource>();
        private readonly Stack<WorkspaceTab> closedTabs = new Stack<WorkspaceTab>();
        private CancellationTokenSource? workspaceOperation;
        private bool workspaceTouched, exitPreparing, exitCaptureDone;
        private DateTime nextAutosave = DateTime.UtcNow.AddSeconds(20);
        private readonly FlowLayoutPanel sessionOffer = new FlowLayoutPanel { Dock = DockStyle.Top, Height = 42, Padding = new Padding(8, 4, 8, 4), BackColor = Color.FromArgb(230, 239, 252), Visible = false };
        private IExplorerDirectoryService? DirectoryReader => explorerTabs as IExplorerDirectoryService;
        public void ConfigureLibrary(IWorkspaceLibraryStore? store, WorkspaceLibrary value)
        { WorkspaceLibrary.Validate(value); libraryStore = store; library = value.Copy(); ShowSessionOffer(); }
        private void InitializeWorkspaceUi(FlowLayoutPanel toolbar, FlowLayoutPanel toolbarRight)
        {
            commandToolbar = toolbar;
            commandToolbarRight = toolbarRight;
            toolbar.BackColor = HostTheme.Canvas;
            toolbarRight.BackColor = HostTheme.Canvas;
            var search = new IconButton(UiGlyph.Search, "搜索标签…");
            var favorites = new IconButton(UiGlyph.Favorites, "常用与标签组…");
            var session = new IconButton(UiGlyph.Session, "会话与恢复…");
            var windows = new IconButton(UiGlyph.DetachWindow, "分离窗口…");
            search.Click += (_, __) => QueueUi(ShowTabPickerAsync); favorites.Click += (_, __) => ShowLibraryMenu(favorites); session.Click += (_, __) => ShowSessionMenu(session);
            windows.Click += (_, __) =>
            {
                var menu = new ContextMenuStrip();
                var active = controller.Active;
                var close = menu.Items.Add("关闭当前 Explorer", null, (_, __) => { if (active != null) QueueUi(() => CloseTabAsync(active)); }); close.Enabled = controller.Active != null && workspaceOperation == null;
                menu.Items.Add("分离当前（不关闭）", null, (_, __) => { if (active != null) QueueMenuAction(() => Detach(active)); });
                menu.Items.Add("分离全部（不关闭）", null, (_, __) => QueueMenuAction(() => DetachAll(false))); menu.Items.Add(new ToolStripSeparator());
                menu.Items.Add("恢复窗口并退出宿主", null, (_, __) => QueueUi(() => { Close(); return Task.CompletedTask; })); ShowManagedMenu(menu, windows);
            };
            toolbar.Paint += PaintToolbarDivider;
            toolbarRight.Paint += PaintToolbarDivider;
            if (toolbar.Parent != null) toolbar.Parent.Paint += PaintToolbarDivider;
            toolbar.Controls.Clear();
            toolbarRight.Controls.Clear();
            toolbar.Controls.AddRange(new Control[] { addWindows, windows, new ToolbarDivider(), favorites, session, cancelCreation });
            // Search and settings open dialogs instead of menus; keep them visually separate on the right.
            toolbarRight.Controls.AddRange(new Control[] { search, preferences });
            newTab.Tag = HostCommand.NewTab; search.Tag = HostCommand.SearchTabs; preferences.Tag = HostCommand.Settings;
            foreach (IconButton button in CommandButtons()) button.BackColor = HostTheme.Canvas;
            tabs.SetTrailingControl(newTab);
            UpdateToolbarHints();
            var restore = new ActionButton("恢复上次会话", true) { Width = 128, Height = 30 }; var dismiss = new ActionButton("暂不恢复") { Width = 88, Height = 30 };
            restore.Click += (_, __) => { if (library.LastSession != null) QueueUi(() => OpenWorkspaceAsync(library.LastSession.Copy())); }; dismiss.Click += (_, __) => sessionOffer.Visible = false;
            sessionOffer.Controls.AddRange(new Control[] { new Label { Text = "上次的标签目录已保存", AutoSize = true, Margin = new Padding(4, 7, 12, 0) }, restore, dismiss });
            Controls.Add(sessionOffer); Controls.SetChildIndex(sessionOffer, Controls.GetChildIndex(chrome!) - 1);
            tabs.ConfigureLayout(settings.WrapTabs, settings.MaximumTabRows);
        }
        private void PaintToolbarDivider(object? sender, PaintEventArgs e)
        {
            var control = (Control)sender!;
            using (var pen = new Pen(Color.FromArgb(215, 222, 231)))
                e.Graphics.DrawLine(pen, 0, control.ClientSize.Height - 1, control.ClientSize.Width, control.ClientSize.Height - 1);
        }
        private IEnumerable<IconButton> CommandButtons()
        {
            if (commandToolbar != null) foreach (IconButton button in commandToolbar.Controls.OfType<IconButton>()) yield return button;
            if (commandToolbarRight != null) foreach (IconButton button in commandToolbarRight.Controls.OfType<IconButton>()) yield return button;
            yield return newTab;
        }
        private void UpdateToolbarHints()
        {
            foreach (IconButton button in CommandButtons())
            {
                var chord = button.Tag is HostCommand command ? ShortcutChord(command) : null;
                string hint = button.Text + (string.IsNullOrEmpty(chord) ? "" : " · " + chord);
                chromeTips.SetToolTip(button, hint); button.AccessibleDescription = hint;
            }
        }
        private string ShortcutChord(HostCommand command) =>
            settings.ShortcutsEnabled ? settings.KeyBindings.FirstOrDefault(k => k.Command == command)?.Chord ?? "" : "";
        private static void ShowShortcut(ToolStripItem item, string chord)
        { if (!string.IsNullOrEmpty(chord) && item is ToolStripMenuItem menuItem) menuItem.ShortcutKeyDisplayString = chord; }
        private void ShowSessionOffer() => sessionOffer.Visible = settings.RememberSession && library.LastSession != null && controller.Sessions.Count == 0;
        private void ShowManagedMenu(ContextMenuStrip menu, Control anchor)
        {
            if (IsDisposed || Disposing || disposingUi || closing || exitPreparing || !IsHandleCreated || anchor.IsDisposed)
            { menu.Dispose(); return; }
            HostTheme.StyleMenu(menu);
            menu.Name = anchor.Text;
            RegisterManagedMenu(menu);
            try
            {
                menu.Show(anchor, new Point(0, anchor.Height));
                // A cancelled Opening does not raise Closed; it must also release ownership/scope.
                if (!menu.Visible) QueueManagedMenuCleanup(menu);
            }
            catch (Exception error)
            {
                log.Write("Menu show failed: " + menu.Name, error);
                QueueManagedMenuCleanup(menu); throw;
            }
        }
        private void RegisterManagedMenu(ContextMenuStrip menu)
        {
            IDisposable? scope = null;
            Action releaseScope = () => { var current = scope; scope = null; current?.Dispose(); };
            managedMenuCleanups.Add(menu, () => { releaseScope(); if (!menu.IsDisposed) menu.Dispose(); });
            menu.Opening += (_, e) =>
            {
                if (e.Cancel || disposingUi || IsDisposed || closing || exitPreparing) { e.Cancel = true; return; }
                scope = EnterModal(); log.Write("Menu opened: " + menu.Name);
            };
            menu.Closed += (_, e) =>
            {
                log.Write("Menu close queued: " + menu.Name + "; reason=" + e.CloseReason);
                // Closed is still inside WinForms' SetVisibleCore/ModalMenuFilter close pipeline.
                // Do not dispose the dropdown (or restore grouped windows) until it has returned.
                QueueManagedMenuCleanup(menu);
            };
            menu.Disposed += (_, __) =>
            {
                managedMenuCleanups.Remove(menu); closedManagedMenus.Remove(menu);
                releaseScope(); log.Write("Menu disposed: " + menu.Name);
            };
        }
        private void QueueManagedMenuCleanup(ContextMenuStrip menu)
        {
            if (!managedMenuCleanups.ContainsKey(menu) || !closedManagedMenus.Add(menu)) return;
            // If the form's handle is gone, its Dispose owns the remaining cleanup.
            if (disposingUi || IsDisposed || Disposing || !IsHandleCreated) return;
            BeginInvoke(new Action(ReleaseClosedManagedMenus));
        }
        private void ReleaseClosedManagedMenus()
        {
            foreach (var menu in closedManagedMenus.ToArray())
                if (managedMenuCleanups.TryGetValue(menu, out var cleanup)) cleanup();
        }
        private void DisposeManagedMenus()
        {
            foreach (var menu in managedMenuCleanups.Keys.ToArray())
            {
                // Close must finish while the menu is alive, before disposing it.
                if (menu.Visible) menu.Close(ToolStripDropDownCloseReason.CloseCalled);
                if (managedMenuCleanups.TryGetValue(menu, out var cleanup)) cleanup();
            }
        }
        private void QueueMenuAction(Action action) => QueueUi(() => { action(); return Task.CompletedTask; });
        private void QueueUi(Func<Task> action)
        {
            if (IsDisposed || Disposing || disposingUi || !IsHandleCreated) return;
            BeginInvoke(new Action(async () =>
            {
                if (IsDisposed || Disposing || disposingUi || closing || exitPreparing) return;
                try
                {
                    // Item.Click may post work before Closed posts cleanup. Both run only after
                    // the click/close stack returns; drain closed menus before executing the action.
                    ReleaseClosedManagedMenus();
                    await action();
                }
                catch (Exception error)
                { if (!IsDisposed && !disposingUi && !closing) ReportError("操作未完成，原窗口与已保存数据保留。", error); }
            }));
        }
        private void CommitLibrary(WorkspaceLibrary candidate)
        {
            if (libraryStore == null) throw new InvalidOperationException("会话库不可用，未保存。请备份并修复 workspace.json 后重启。");
            libraryStore.Save(candidate); library = candidate.Copy();
        }
        private void ShowSessionMenu(Control anchor)
        {
            var menu = new ContextMenuStrip();
            var save = menu.Items.Add("保存当前会话", null, (_, __) => QueueUi(() => SaveSessionAsync(false))); save.Enabled = workspaceOperation == null && libraryStore != null;
            var restore = menu.Items.Add("恢复上次会话（追加标签）", null, (_, __) => { if (library.LastSession != null) QueueUi(() => OpenWorkspaceAsync(library.LastSession.Copy())); }); restore.Enabled = library.LastSession != null && workspaceOperation == null;
            var reopen = menu.Items.Add("重新打开刚关闭的目录", null, (_, __) => QueueUi(ReopenTabAsync)); reopen.Enabled = closedTabs.Count > 0 && workspaceOperation == null;
            menu.Items.Add(new ToolStripSeparator());
            var clear = menu.Items.Add("清除保存的上次会话（不关闭窗口）", null, (_, __) => QueueMenuAction(() => RunSafely("清除会话失败", () => { var copy = library.Copy(); copy.LastSession = null; CommitLibrary(copy); workspaceTouched = false; sessionOffer.Visible = false; }))); clear.Enabled = library.LastSession != null && libraryStore != null && workspaceOperation == null;
            ShowManagedMenu(menu, anchor);
        }
        private void ShowLibraryMenu(Control anchor)
        {
            var menu = new ContextMenuStrip();
            var active = controller.Active;
            var add = menu.Items.Add("收藏当前标签目录…", null, (_, __) => { if (active != null) QueueUi(() => AddFavoriteAsync(active)); }); add.Enabled = controller.Active != null && workspaceOperation == null && libraryStore != null;
            var browse = menu.Items.Add("添加常用目录…", null, (_, __) => QueueUi(AddFolderBookmarkAsync)); browse.Enabled = libraryStore != null && workspaceOperation == null;
            var save = menu.Items.Add("将当前标签保存为组…", null, (_, __) => QueueUi(SaveGroupAsync)); save.Enabled = controller.Sessions.Count > 0 && workspaceOperation == null && libraryStore != null;
            menu.Items.Add(new ToolStripSeparator());
            foreach (var favorite in library.Favorites)
            {
                var item = new ToolStripMenuItem("★ " + favorite.Name) { ToolTipText = favorite.Directory };
                item.DropDownItems.Add("打开为新标签", null, (_, __) => QueueUi(() => OpenWorkspaceAsync(new SavedWorkspace { Name = favorite.Name, Tabs = new List<WorkspaceTab> { new WorkspaceTab { Directory = favorite.Directory } } })));
                item.DropDownItems.Add("重命名…", null, (_, __) => QueueUi(() => RenameLibraryItemAsync(favorite.Id, false)));
                item.DropDownItems.Add("移除收藏（不关闭窗口）", null, (_, __) => QueueMenuAction(() => RunSafely("移除收藏失败", () => { var copy = library.Copy(); copy.Favorites.RemoveAll(f => f.Id == favorite.Id); CommitLibrary(copy); }))); item.Enabled = workspaceOperation == null; menu.Items.Add(item);
            }
            if (library.Favorites.Count == 0) menu.Items.Add(new ToolStripMenuItem("暂无常用目录") { Enabled = false }); menu.Items.Add(new ToolStripSeparator());
            foreach (var group in library.Groups)
            {
                var item = new ToolStripMenuItem("▤ " + group.Name + "  ·  " + group.Tabs.Count + " 个目录");
                item.DropDownItems.Add("打开标签组（追加）", null, (_, __) => QueueUi(() => OpenWorkspaceAsync(group.Copy())));
                item.DropDownItems.Add("重命名…", null, (_, __) => QueueUi(() => RenameLibraryItemAsync(group.Id, true)));
                item.DropDownItems.Add("删除标签组（不关闭窗口）", null, (_, __) => QueueMenuAction(() => RunSafely("删除标签组失败", () => { var copy = library.Copy(); copy.Groups.RemoveAll(g => g.Id == group.Id); CommitLibrary(copy); }))); item.Enabled = workspaceOperation == null; menu.Items.Add(item);
            }
            if (library.Groups.Count == 0) menu.Items.Add(new ToolStripMenuItem("暂无标签组") { Enabled = false }); ShowManagedMenu(menu, anchor);
        }
        private string? AskName(string title, string value)
        { using (EnterModal()) using (var dialog = new RenameTabDialog(value)) { dialog.Text = title; return dialog.ShowDialog(this) == DialogResult.OK ? TabManagementPolicy.NormalizeTitle(dialog.TabTitle) : null; } }
        private Task RenameLibraryItemAsync(string id, bool group)
        {
            var copy = library.Copy(); string? old = group ? copy.Groups.FirstOrDefault(g => g.Id == id)?.Name : copy.Favorites.FirstOrDefault(f => f.Id == id)?.Name;
            if (old == null) return Task.CompletedTask; var name = AskName(group ? "重命名标签组" : "重命名常用目录", old); if (name == null) return Task.CompletedTask;
            if (group) copy.Groups.First(g => g.Id == id).Name = name; else copy.Favorites.First(f => f.Id == id).Name = name; CommitLibrary(copy); return Task.CompletedTask;
        }
        private Task AddFolderBookmarkAsync()
        {
            string path; using (EnterModal()) using (var picker = new FolderBrowserDialog { Description = "选择要收藏的普通目录", ShowNewFolderButton = false }) { if (picker.ShowDialog(this) != DialogResult.OK) return Task.CompletedTask; path = FolderTarget.Normalize(picker.SelectedPath); }
            AddBookmark(path, path); return Task.CompletedTask;
        }
        private async Task AddFavoriteAsync(WindowSession session)
        {
            if (workspaceOperation != null || !controller.Sessions.Contains(session)) return;
            using (var token = new CancellationTokenSource(3000)) { string path = await ReadTabDirectoryAsync(session, token.Token); if (!controller.Sessions.Contains(session) || closing || exitPreparing) return; AddBookmark(path, session.DisplayTitle); }
        }
        private void AddBookmark(string path, string suggestedName)
        {
            string? name = AskName("收藏目录", suggestedName); if (name == null) return;
            var copy = library.Copy(); var existing = copy.Favorites.FirstOrDefault(f => FolderTarget.Same(f.Directory, path));
            if (existing == null) copy.Favorites.Add(new FolderBookmark { Name = name, Directory = path }); else existing.Name = name; CommitLibrary(copy); status.Text = "已收藏：" + name;
        }
    }
}
