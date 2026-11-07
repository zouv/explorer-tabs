using System;
using System.Collections.Generic;
using System.Drawing;
using System.Globalization;
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
        internal Func<IReadOnlyList<WindowIdentity>> ExplorerDiscovery { get; set; } = NativeWindowApi.DiscoverExplorers;
        private readonly Dictionary<WindowSession, WorkspaceTab> pendingClosed = new Dictionary<WindowSession, WorkspaceTab>();
        private readonly Dictionary<WindowSession, CancellationTokenSource> closeReading = new Dictionary<WindowSession, CancellationTokenSource>();
        private readonly Stack<WorkspaceTab> closedTabs = new Stack<WorkspaceTab>();
        private CancellationTokenSource? workspaceOperation;
        // Automatic session saving shares workspaceOperation but must not expose the cancel glyph.
        private bool workspaceOperationSilent;
        private bool workspaceTouched, exitPreparing, exitCaptureDone;
        private DateTime nextAutosave = DateTime.UtcNow.AddSeconds(20);
        private readonly FlowLayoutPanel sessionOffer = new FlowLayoutPanel { Dock = DockStyle.Top, AutoSize = true, AutoSizeMode = AutoSizeMode.GrowAndShrink, Padding = new Padding(8, 4, 8, 4), BackColor = Color.FromArgb(230, 239, 252), Visible = false };
        private readonly Label sessionOfferText = new Label { AutoSize = true, Margin = new Padding(4, 5, 4, 5) };
        private readonly ActionButton sessionOfferRestore = new ActionButton("重新打开上次会话", true) { Width = 176, Height = 30 };
        private readonly ActionButton sessionOfferDismiss = new ActionButton("暂不打开") { Width = 88, Height = 30 };
        private SavedWorkspace? offeredSession;
        private int startupMissingWindows;
        private bool sessionOfferDismissed;
        private IExplorerDirectoryService? DirectoryReader => explorerTabs as IExplorerDirectoryService;
        public void ConfigureLibrary(IWorkspaceLibraryStore? store, WorkspaceLibrary value)
        { WorkspaceLibrary.Validate(value); libraryStore = store; library = value.Copy(); ShowSessionOffer(); }
        public void ConfigureStartupRecovery(int missingWindows)
        {
            if (missingWindows < 1 || missingWindows > 100) throw new ArgumentOutOfRangeException(nameof(missingWindows));
            startupMissingWindows = missingWindows;
        }
        private void InitializeWorkspaceUi(FlowLayoutPanel toolbar, FlowLayoutPanel toolbarRight)
        {
            commandToolbar = toolbar;
            commandToolbarRight = toolbarRight;
            toolbar.BackColor = HostTheme.Canvas;
            toolbarRight.BackColor = HostTheme.Canvas;
            var search = new IconButton(UiGlyph.Search, "搜索标签…");
            var favorites = new IconButton(UiGlyph.Favorites, "收藏与标签组…");
            var session = new IconButton(UiGlyph.Session, "会话与恢复…");
            var windows = new IconButton(UiGlyph.DetachWindow, "分离标签…");
            search.Click += (_, __) => QueueUi(ShowTabPickerAsync); favorites.Click += (_, __) => ShowLibraryMenu(favorites); session.Click += (_, __) => ShowSessionMenu(session);
            windows.Click += (_, __) => ShowManagedMenu(CreateDetachMenu(), windows);
            toolbar.Paint += PaintToolbarDivider;
            toolbarRight.Paint += PaintToolbarDivider;
            if (toolbar.Parent != null) toolbar.Parent.Paint += PaintToolbarDivider;
            toolbar.Controls.Clear();
            toolbarRight.Controls.Clear();
            toolbar.Controls.AddRange(new Control[] { addWindows, windows, new ToolbarDivider(), favorites, session, cancelCreation });
            // Search opens a dialog; keep it visually separate on the right.
            toolbarRight.Controls.AddRange(new Control[] { search });
            newTab.Tag = HostCommand.NewTab; search.Tag = HostCommand.SearchTabs;
            foreach (IconButton button in CommandButtons()) button.BackColor = HostTheme.Canvas;
            tabs.SetTrailingControl(newTab);
            UpdateToolbarHints();
            sessionOfferRestore.Click += (_, __) =>
            {
                if (offeredSession == null || !CanOpenWorkspace(offeredSession)) return;
                var snapshot = offeredSession.Copy();
                QueueUi(() =>
                {
                    if (!CanOpenWorkspace(snapshot)) return Task.CompletedTask;
                    sessionOfferDismissed = true;
                    return OpenWorkspaceAsync(snapshot);
                });
            };
            sessionOfferDismiss.Click += (_, __) => { sessionOfferDismissed = true; sessionOffer.Visible = false; };
            sessionOfferRestore.AccessibleDescription = "按保存的目录创建新的 Explorer 标签；不恢复原窗口的选中项、滚动位置或导航历史。";
            chromeTips.SetToolTip(sessionOfferRestore, sessionOfferRestore.AccessibleDescription);
            sessionOffer.Controls.AddRange(new Control[] { sessionOfferText, sessionOfferRestore, sessionOfferDismiss });
            sessionOffer.SetFlowBreak(sessionOfferText, true);
            sessionOffer.SizeChanged += (_, __) => LayoutSessionOffer();
            Controls.Add(sessionOffer); Controls.SetChildIndex(sessionOffer, Controls.GetChildIndex(chrome!) - 1);
            tabs.ConfigureLayout(settings.WrapTabs, settings.MaximumTabRows);
        }
        // Menu opening owns a modal scope. Do not include modalDepth here: queued actions
        // release that scope before running, while dialog/hotkey entry guards remain unchanged.
        private bool MenuIdle => !closing && !exitPreparing && !disposingUi && !IsDisposed &&
            workspaceOperation == null && creation == null && !explorerTabs.IsBusy;
        private bool CanUseTab(WindowSession? session) => MenuIdle && session != null &&
            controller.Sessions.Contains(session) && session.State == SessionState.Attached &&
            session.CloseRequestedUtc == null && !closeReading.ContainsKey(session);
        private bool CanReadDirectory(WindowSession session) => DirectoryReader?.IsReading != true &&
            (DirectoryReader != null || directoryCache.ContainsKey(session));
        private bool CanDetachTabs => MenuIdle && closeReading.Count == 0 &&
            controller.Sessions.Count > 0 && controller.Sessions.All(s => s.CloseRequestedUtc == null);
        private bool CanCaptureWorkspace => MenuIdle && libraryStore != null && closeReading.Count == 0 &&
            DirectoryReader?.IsReading != true && controller.Sessions.Count > 0 &&
            controller.Sessions.All(s => s.State == SessionState.Attached && s.CloseRequestedUtc == null && CanReadDirectory(s));
        private bool CanOpenWorkspace(SavedWorkspace saved) => MenuIdle && saved.Tabs.Count > 0 &&
            saved.Tabs.Count + controller.Sessions.Count <= 100;
        private static string MenuText(string text)
        {
            string singleLine = text.Replace("\r", " ").Replace("\n", " ").Replace("\t", " ");
            var positions = StringInfo.ParseCombiningCharacters(singleLine);
            if (positions.Length > 32) singleLine = singleLine.Substring(0, positions[31]) + "…";
            return singleLine.Replace("&", "&&");
        }
        private void AddExitMenuItems(ContextMenuStrip menu, bool includeClose = true)
        {
            menu.ShowItemToolTips = true;
            var retain = menu.Items.Add("退出并保留窗口", null, (_, __) => QueueMenuAction(CloseRestoringWindows));
            retain.Enabled = !closing && !exitPreparing && !disposingUi;
            retain.ToolTipText = "退出 ExplorerTabs；所有标签恢复为独立 Explorer 窗口，目录保持打开；恢复未完成时保留应用。";
            if (!includeClose) return;
            var close = menu.Items.Add("关闭所有标签并退出", null, (_, __) => QueueMenuAction(RequestExitByClosingSessions));
            close.Enabled = !closing && !exitPreparing && !disposingUi && controller.Sessions.All(s => s.State == SessionState.Attached);
            close.ToolTipText = "关闭所有标签对应的 Explorer 窗口，再退出 ExplorerTabs；窗口未关闭时保留应用。";
            ShowShortcut(close, "Alt+F4");
            menu.ShowItemToolTips = true;
        }
        private ContextMenuStrip CreateDetachMenu()
        {
            var menu = new ContextMenuStrip { ShowItemToolTips = true };
            var active = controller.Active;
            var detach = menu.Items.Add("分离当前标签", null, (_, __) => { if (active != null) QueueMenuAction(() => Detach(active)); });
            detach.Enabled = CanDetachTabs && active != null && controller.Sessions.Contains(active);
            detach.ToolTipText = "将当前标签恢复为独立 Explorer 窗口，目录保持打开。";
            var all = menu.Items.Add("分离所有标签", null, (_, __) => QueueMenuAction(() => DetachAll(false)));
            all.Enabled = CanDetachTabs;
            all.ToolTipText = "将所有标签恢复为独立 Explorer 窗口；ExplorerTabs 保持运行。";
            menu.Items.Add(new ToolStripSeparator());
            AddExitMenuItems(menu, includeClose: false);
            return menu;
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
        {
            // ShortcutKeyDisplayString is painted by WinForms in the menu's right-aligned
            // shortcut column; never append the chord to the item text itself.
            if (!string.IsNullOrEmpty(chord) && item is ToolStripMenuItem menuItem)
            { menuItem.ShowShortcutKeys = true; menuItem.ShortcutKeyDisplayString = chord; }
        }
        private void ShowSessionOffer()
        {
            offeredSession = library.LastSession?.Copy();
            bool canOffer = settings.RememberSession && offeredSession != null;
            if (startupMissingWindows > 0)
            {
                sessionOfferText.Text = $"上次的 {startupMissingWindows} 个原窗口已失效，无法恢复原窗口。\n" +
                    (offeredSession == null ? "没有可用的已保存会话；窗口标题不能用于重建目录，请手动新建标签。" :
                    canOffer ? $"已保存 {offeredSession.Tabs.Count} 个目录，可重新打开为新标签。" :
                    $"已保存 {offeredSession.Tabs.Count} 个目录；记住会话已关闭，可从“会话与恢复”菜单手动打开。");
            }
            else sessionOfferText.Text = offeredSession == null ? "" : $"上次会话已保存 {offeredSession.Tabs.Count} 个目录，可重新打开为新标签。";
            sessionOfferRestore.Visible = canOffer;
            RefreshSessionOfferControls();
            sessionOffer.Visible = !sessionOfferDismissed && controller.Sessions.Count == 0 && (startupMissingWindows > 0 || canOffer);
            LayoutSessionOffer();
        }
        private void RefreshSessionOfferControls() => sessionOfferRestore.Enabled = settings.RememberSession &&
            offeredSession != null && CanOpenWorkspace(offeredSession);
        private void LayoutSessionOffer()
        {
            int width = Math.Max(1, sessionOffer.ClientSize.Width - sessionOffer.Padding.Horizontal - sessionOfferText.Margin.Horizontal);
            if (sessionOfferText.MaximumSize.Width != width) sessionOfferText.MaximumSize = new Size(width, 0);
        }
        private void ShowAddWindowsMenu()
        {
            if (closing || exitPreparing || workspaceOperation != null || creation != null || explorerTabs.IsBusy) return;
            ShowManagedMenu(CreateAddWindowsMenu(), addWindows);
        }
        private ContextMenuStrip CreateAddWindowsMenu()
        {
            var menu = new ContextMenuStrip();
            IReadOnlyList<WindowIdentity> discovered;
            try { discovered = ExplorerDiscovery() ?? Array.Empty<WindowIdentity>(); }
            catch (Exception error) { log.Write("Attach menu discovery failed", error); discovered = Array.Empty<WindowIdentity>(); }
            var attached = new HashSet<long>(controller.Sessions.Select(s => s.Identity.Handle));
            var candidates = discovered.Where(w => !attached.Contains(w.Handle))
                .GroupBy(w => w.Handle).Select(group => group.First()).ToArray();
            foreach (var identity in candidates)
            {
                var displayTitle = string.IsNullOrWhiteSpace(identity.Title) ? "（无标题窗口）" : identity.Title;
                var text = MenuText(displayTitle);
                var item = new ToolStripMenuItem(text)
                {
                    ToolTipText = "加入为标签：" + displayTitle + "\n接管此 Explorer 窗口，不重新打开目录。",
                    Enabled = MenuIdle
                };
                item.Click += (_, __) => QueueMenuAction(() => AttachSelectedWindows(new[] { identity }));
                menu.Items.Add(item);
            }
            if (candidates.Length == 0) menu.Items.Add(new ToolStripMenuItem("暂无可加入的 Explorer 窗口") { Enabled = false });
            menu.Items.Add(new ToolStripSeparator());
            var choose = menu.Items.Add("选择窗口…", null, (_, __) => QueueMenuAction(OpenWindowPicker));
            choose.Enabled = MenuIdle;
            choose.ToolTipText = "打开窗口选择器，可一次选择多个 Explorer 窗口加入。";
            menu.ShowItemToolTips = true;
            return menu;
        }
        private void ShowManagedMenu(ContextMenuStrip menu, Control anchor)
        {
            if (anchor.IsDisposed) { menu.Dispose(); return; }
            ShowManagedMenuCore(menu, anchor.Text, () => menu.Show(anchor, new Point(0, anchor.Height)));
        }
        private void ShowManagedMenuAtScreenPoint(ContextMenuStrip menu, Point screenLocation) =>
            ShowManagedMenuCore(menu, "应用菜单", () => menu.Show(screenLocation));
        private void ShowManagedMenuCore(ContextMenuStrip menu, string name, Action show)
        {
            if (IsDisposed || Disposing || disposingUi || closing || exitPreparing || !IsHandleCreated)
            { menu.Dispose(); return; }
            HostTheme.StyleMenu(menu);
            menu.Name = name;
            RegisterManagedMenu(menu);
            try
            {
                show();
                // A cancelled Opening does not raise Closed; it must also release ownership/scope.
                if (!menu.Visible) QueueManagedMenuCleanup(menu);
            }
            catch (Exception error)
            {
                log.Write("Menu show failed: " + menu.Name, error);
                QueueManagedMenuCleanup(menu); throw;
            }
        }
        private void RegisterManagedMenu(ContextMenuStrip menu) => RegisterMenuLifetime(menu, true);
        private void RegisterMenuLifetime(ContextMenuStrip menu, bool disposeOnClose)
        {
            IDisposable? scope = null;
            Action releaseScope = () => { var current = scope; scope = null; current?.Dispose(); };
            managedMenuCleanups.Add(menu, () => { closedManagedMenus.Remove(menu); releaseScope(); if (disposeOnClose && !menu.IsDisposed) menu.Dispose(); });
            menu.Opening += (_, e) =>
            {
                if (e.Cancel || disposingUi || IsDisposed || closing || exitPreparing) { e.Cancel = true; return; }
                closedManagedMenus.Remove(menu);
                if (scope == null) scope = EnterModal(); log.Write("Menu opened: " + menu.Name);
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
                if (!menu.IsDisposed) menu.Dispose();
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
            if (sessionOffer.Visible) ShowSessionOffer();
        }
        private ContextMenuStrip CreateSessionMenu()
        {
            var menu = new ContextMenuStrip { ShowItemToolTips = true };
            var save = menu.Items.Add("保存当前会话", null, (_, __) => QueueUi(() => SaveSessionAsync(false)));
            save.Enabled = CanCaptureWorkspace;
            save.ToolTipText = "更新用于下次恢复的会话快照，覆盖此前快照；长期保存请使用标签组。";
            ShowShortcut(save, ShortcutChord(HostCommand.SaveSession));
            var snapshot = library.LastSession?.Copy();
            var restore = menu.Items.Add("恢复上次会话（追加）", null, (_, __) => { if (snapshot != null) QueueUi(() => OpenWorkspaceAsync(snapshot)); });
            restore.Enabled = snapshot != null && CanOpenWorkspace(snapshot);
            restore.ToolTipText = "将保存的会话快照追加为新标签，不替换当前标签。";
            var reopen = menu.Items.Add("重新打开关闭的标签", null, (_, __) => QueueUi(ReopenTabAsync));
            reopen.Enabled = MenuIdle && closedTabs.Count > 0 && controller.Sessions.Count < 100;
            reopen.ToolTipText = "按关闭顺序逐个恢复最近的标签目录与名称；最多保留 20 项可恢复记录。";
            ShowShortcut(reopen, ShortcutChord(HostCommand.ReopenTab));
            menu.Items.Add(new ToolStripSeparator());
            var clear = menu.Items.Add("清除上次会话记录", null, (_, __) => QueueMenuAction(ClearSavedSession));
            clear.Enabled = MenuIdle && library.LastSession != null && libraryStore != null;
            clear.ToolTipText = "只清除恢复快照，不关闭标签、不删除收藏或标签组；开启自动保存后，后续标签变化可能再次保存快照。";
            return menu;
        }
        private void ClearSavedSession()
        {
            if (!MenuIdle || libraryStore == null || library.LastSession == null) return;
            RunSafely("清除会话失败", () =>
            {
                var copy = library.Copy(); copy.LastSession = null; CommitLibrary(copy);
                workspaceTouched = false; sessionOffer.Visible = false;
            });
        }
        private void ShowSessionMenu(Control anchor) => ShowManagedMenu(CreateSessionMenu(), anchor);
        private ContextMenuStrip CreateLibraryMenu()
        {
            var menu = new ContextMenuStrip { ShowItemToolTips = true };
            var active = controller.Active;
            var add = menu.Items.Add("收藏当前目录…", null, (_, __) => { if (active != null) QueueUi(() => AddFavoriteAsync(active)); });
            add.Enabled = CanUseTab(active) && CanReadDirectory(active!) && libraryStore != null;
            add.ToolTipText = "为当前标签的目录命名并保存到收藏。";
            var browse = menu.Items.Add("收藏其他目录…", null, (_, __) => QueueUi(AddFolderBookmarkAsync));
            browse.Enabled = MenuIdle && libraryStore != null;
            browse.ToolTipText = "选择目录并保存到收藏，不立即打开新标签。";
            var save = menu.Items.Add("保存当前标签组…", null, (_, __) => QueueUi(SaveGroupAsync));
            save.Enabled = CanCaptureWorkspace;
            save.ToolTipText = "为当前标签集合命名，保存目录、顺序与标签名称；不会覆盖恢复快照。";
            menu.Items.Add(new ToolStripSeparator());
            menu.Items.Add(new ToolStripLabel("收藏（" + library.Favorites.Count + "）") { ForeColor = HostTheme.MutedText });
            foreach (var record in library.Favorites)
            {
                var favorite = record.Copy();
                var saved = new SavedWorkspace { Name = favorite.Name, Tabs = new List<WorkspaceTab> { new WorkspaceTab { Directory = favorite.Directory } } };
                var item = new ToolStripMenuItem(MenuText(favorite.Name))
                {
                    ToolTipText = "打开为新标签：" + favorite.Name + "\n" + FolderTarget.Display(favorite.Directory),
                    Enabled = CanOpenWorkspace(saved)
                };
                item.Click += (_, __) => QueueUi(() => OpenWorkspaceAsync(saved));
                menu.Items.Add(item);
            }
            if (library.Favorites.Count == 0) menu.Items.Add(new ToolStripMenuItem("暂无收藏目录") { Enabled = false });
            menu.Items.Add(new ToolStripSeparator());
            menu.Items.Add(new ToolStripLabel("标签组（" + library.Groups.Count + "）") { ForeColor = HostTheme.MutedText });
            foreach (var record in library.Groups)
            {
                var group = record.Copy();
                var item = new ToolStripMenuItem(MenuText(group.Name) + "（" + group.Tabs.Count + " 个标签）")
                {
                    ToolTipText = "追加打开标签组：" + group.Name + "\n将 " + group.Tabs.Count + " 个保存的目录追加为新标签，不替换当前标签。",
                    Enabled = CanOpenWorkspace(group)
                };
                item.Click += (_, __) => QueueUi(() => OpenWorkspaceAsync(group));
                menu.Items.Add(item);
            }
            if (library.Groups.Count == 0) menu.Items.Add(new ToolStripMenuItem("暂无标签组") { Enabled = false });
            menu.Items.Add(new ToolStripSeparator());
            var manage = menu.Items.Add("管理收藏与标签组…", null, (_, __) => QueueMenuAction(ShowLibraryManager));
            manage.Enabled = MenuIdle && libraryStore != null;
            manage.ToolTipText = libraryStore == null ? "会话库不可写：已有记录仍可打开；请备份并修复 workspace.json 后重启。" :
                "集中重命名、移除收藏或删除标签组；不会关闭已打开的标签。";
            if (libraryStore == null)
                menu.Items.Add(new ToolStripLabel("会话库不可写：仅可打开已有记录") { ForeColor = HostTheme.MutedText });
            return menu;
        }
        private void ShowLibraryMenu(Control anchor) => ShowManagedMenu(CreateLibraryMenu(), anchor);
        private void ShowLibraryManager()
        {
            if (!MenuIdle || libraryStore == null) return;
            FolderBookmark? openFavorite;
            SavedWorkspace? openGroup;
            using (EnterModal())
            using (var dialog = new LibraryManagerDialog(library))
            {
                if (dialog.ShowDialog(this) != DialogResult.OK) return;
                if (dialog.Dirty) CommitLibrary(dialog.Result);
                openFavorite = dialog.FavoriteToOpen?.Copy();
                openGroup = dialog.GroupToOpen?.Copy();
            }
            if (openFavorite != null)
                QueueUi(() => OpenWorkspaceAsync(new SavedWorkspace
                {
                    Name = openFavorite.Name,
                    Tabs = new List<WorkspaceTab> { new WorkspaceTab { Directory = openFavorite.Directory } }
                }));
            else if (openGroup != null)
                QueueUi(() => OpenWorkspaceAsync(openGroup));
        }
        private string? AskName(string title, string value)
        { using (EnterModal()) using (var dialog = new RenameTabDialog(value, title)) return dialog.ShowDialog(this) == DialogResult.OK ? TabManagementPolicy.NormalizeTitle(dialog.TabTitle) : null; }
        private Task AddFolderBookmarkAsync()
        {
            if (!MenuIdle || libraryStore == null) return Task.CompletedTask;
            string path; using (EnterModal()) using (var picker = new FolderBrowserDialog { Description = "选择要收藏的普通目录", ShowNewFolderButton = false }) { if (picker.ShowDialog(this) != DialogResult.OK) return Task.CompletedTask; path = FolderTarget.Normalize(picker.SelectedPath); }
            AddBookmark(path, path); return Task.CompletedTask;
        }
        private async Task AddFavoriteAsync(WindowSession session)
        {
            if (!CanUseTab(session) || libraryStore == null || !CanReadDirectory(session)) return;
            using (var token = new CancellationTokenSource(3000)) { string path = await ReadTabDirectoryAsync(session, token.Token); if (!CanUseTab(session) || libraryStore == null) return; AddBookmark(path, session.DisplayTitle); }
        }
        private void AddBookmark(string path, string suggestedName)
        {
            if (!MenuIdle || libraryStore == null) return;
            string? name = AskName("收藏目录", suggestedName); if (name == null) return;
            var copy = library.Copy(); var existing = copy.Favorites.FirstOrDefault(f => FolderTarget.Same(f.Directory, path));
            if (existing == null) copy.Favorites.Add(new FolderBookmark { Name = name, Directory = path }); else existing.Name = name; CommitLibrary(copy); status.Text = "已收藏：" + name;
        }
    }
}
