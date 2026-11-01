using System;
using System.Collections;
using System.Collections.Generic;
using System.Drawing;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Threading;
using System.Windows.Forms;
using ExplorerTabs.Core;

namespace ExplorerTabs.Tests
{
    internal static partial class Program
    {
        private static ToolStripMenuItem MenuItem(ContextMenuStrip menu, string text) =>
            menu.Items.OfType<ToolStripMenuItem>().Single(i => i.Text == text);
        private static ContextMenuStrip MakeMenu(Form form, string method) => (ContextMenuStrip)UiCall(form, method)!;
        private static void SetMenuField(Form form, string name, object? value) =>
            MainType.GetField(name, BindingFlags.Instance | BindingFlags.NonPublic)!.SetValue(form, value);
        private static object[] CommandEntries(Form form) => ((IEnumerable)UiCall(form, "CreateCommandEntries")!).Cast<object>().ToArray();
        private static string EntryText(object entry, string name) => (string)entry.GetType().GetProperty(name)!.GetValue(entry)!;
        private static void MenuConsistencyTests(string directory)
        {
            var api = new FakeWindows(); var host = new HostController(api, new FakeStore(), new TestCloser());
            api.Alive.UnionWith(new long[] { 10, 11, 12 });
            var service = new WorkspaceService { NextHandle = 10 }; var store = new WorkspaceStore();
            var favorite = new FolderBookmark { Name = "资料 & 开发", Directory = @"D:\Docs" };
            var group = new SavedWorkspace { Name = "项目 & 工具", Tabs = new List<WorkspaceTab>
                { new WorkspaceTab { Directory = @"C:\One", Alias = "第一" }, new WorkspaceTab { Directory = @"C:\Two" } } };
            store.Value.Favorites.Add(favorite); store.Value.Groups.Add(group); store.Value.LastSession = group.Copy();
            using (var form = WorkspaceForm(host, service, directory, store))
            {
                MainType.GetProperty("ExplorerDiscovery", BindingFlags.Instance | BindingFlags.NonPublic)!.SetValue(form, (Func<IReadOnlyList<WindowIdentity>>)(() => Array.Empty<WindowIdentity>()));
                using (var menu = MakeMenu(form, "CreateDetachMenu"))
                    Check(!MenuItem(menu, "分离当前标签").Enabled && !MenuItem(menu, "分离所有标签").Enabled &&
                        MenuItem(menu, "退出并保留窗口").Enabled && !menu.Items.Cast<ToolStripItem>().Any(i => i.Text.StartsWith("关闭")),
                        "empty detach menu contains no destructive close and disables unavailable detach actions");
                using (var menu = MakeMenu(form, "CreateSessionMenu"))
                    Check(!MenuItem(menu, "保存当前会话").Enabled && !MenuItem(menu, "重新打开关闭的标签").Enabled &&
                        MenuItem(menu, "恢复上次会话（追加）").Enabled && MenuItem(menu, "清除上次会话记录").Enabled,
                        "empty host offers explicit restore/clear but no empty save or unavailable reopen");
                var one = Add(host, 1); var two = Add(host, 2); host.Activate(one, Bounds); UiCall(form, "RefreshTabs");
                using (var modal = (IDisposable)UiCall(form, "EnterModal")!)
                using (var menu = MakeMenu(form, "CreateDetachMenu"))
                    Check(UiField<int>(form, "modalDepth") == 1 && MenuItem(menu, "分离当前标签").Enabled &&
                        MenuItem(menu, "分离所有标签").Enabled, "menu's own modal scope does not disable ready actions");
                using (var menu = MakeMenu(form, "CreateLibraryMenu"))
                {
                    Check(menu.ShowItemToolTips && menu.Items.OfType<ToolStripLabel>().Select(i => i.Text).SequenceEqual(
                        new[] { "收藏（1）", "标签组（1）" }), "library lists use distinct counted section headings");
                    Check(MenuItem(menu, "资料 && 开发").DropDownItems.Count == 0 && MenuItem(menu, "项目 && 工具（2 个标签）").DropDownItems.Count == 0,
                        "favorites and groups open directly and escape mnemonic ampersands");
                    Check(MenuItem(menu, "资料 && 开发").ToolTipText.Contains("资料 & 开发") &&
                        MenuItem(menu, "项目 && 工具（2 个标签）").ToolTipText.Contains("追加"), "hover preserves full user names and append semantics");
                    Check(menu.Items.OfType<ToolStripMenuItem>().All(i => !i.Text.Contains("重命名") && !i.Text.Contains("删除") && !i.Text.Contains("移除")),
                        "destructive library edits are centralized in manager instead of nested open menus");
                }
                using (var menu = MakeMenu(form, "CreateLibraryMenu"))
                {
                    UiField<WorkspaceLibrary>(form, "library").Favorites[0].Directory = @"D:\Changed";
                    MenuItem(menu, "资料 && 开发").PerformClick();
                    Check(PumpUntil(() => service.CreatedPaths.Count == 1, 2000) && service.CreatedPaths[0] == @"D:\Docs",
                        "one-click favorite stays anchored to the captured record after live library changes");
                }
                using (var menu = MakeMenu(form, "CreateLibraryMenu"))
                {
                    UiField<WorkspaceLibrary>(form, "library").Groups[0].Tabs[0].Directory = @"D:\Changed";
                    int before = host.Sessions.Count; MenuItem(menu, "项目 && 工具（2 个标签）").PerformClick();
                    Check(PumpUntil(() => host.Sessions.Count == before + 2 && UiField<CancellationTokenSource?>(form, "workspaceOperation") == null, 2000) &&
                        service.CreatedPaths.Skip(1).SequenceEqual(new[] { @"C:\One", @"C:\Two" }) && host.Sessions.Any(s => s.CustomTitle == "第一"),
                        "one-click group appends all captured directories in order and preserves aliases");
                }
                host.Activate(one, Bounds);
                MainType.GetMethod("ConfigureLibrary")!.Invoke(form, new object?[] { null, store.Value });
                using (var menu = MakeMenu(form, "CreateLibraryMenu"))
                using (var tab = (ContextMenuStrip)UiCall(form, "CreateTabMenu", one)!)
                {
                    Check(!MenuItem(menu, "收藏当前目录…").Enabled && !MenuItem(menu, "收藏其他目录…").Enabled &&
                        !MenuItem(menu, "保存当前标签组…").Enabled && !MenuItem(menu, "管理收藏与标签组…").Enabled &&
                        !MenuItem(tab, "收藏此目录…").Enabled, "unwritable library disables all persistent edits, including tab context menu");
                    Check(MenuItem(menu, "资料 && 开发").Enabled && MenuItem(menu, "项目 && 工具（2 个标签）").Enabled &&
                        menu.Items.OfType<ToolStripLabel>().Any(i => i.Text.Contains("不可写")), "unwritable library keeps existing records readable/openable and explains restriction");
                }
                using (var menu = MakeMenu(form, "CreateSessionMenu"))
                    Check(!MenuItem(menu, "保存当前会话").Enabled && !MenuItem(menu, "清除上次会话记录").Enabled &&
                        MenuItem(menu, "恢复上次会话（追加）").Enabled, "unwritable library allows restore but never save or clear");
                Check(CommandEntries(form).Any(e => EntryText(e, "Title").StartsWith("打开收藏目录")) &&
                    !CommandEntries(form).Any(e => EntryText(e, "Title") == "管理收藏与标签组…" || EntryText(e, "Title") == "保存当前会话"),
                    "command palette follows library read-only state and shared terminology");
                MainType.GetMethod("ConfigureLibrary")!.Invoke(form, new object[] { store, store.Value });
                var settings = UiField<AppSettings>(form, "settings");
                settings.KeyBindings.Single(b => b.Command == HostCommand.SaveSession).Chord = "Ctrl+Alt+S";
                settings.KeyBindings.Single(b => b.Command == HostCommand.ReopenTab).Chord = "Ctrl+Alt+R";
                using (var menu = MakeMenu(form, "CreateSessionMenu"))
                    Check(MenuItem(menu, "保存当前会话").ShortcutKeyDisplayString == "Ctrl+Alt+S" &&
                        MenuItem(menu, "重新打开关闭的标签").ShortcutKeyDisplayString == "Ctrl+Alt+R" &&
                        MenuItem(menu, "清除上次会话记录").ToolTipText.Contains("后续标签变化"), "session shortcuts show actual bindings and clear explains automatic re-save");
                Check(CommandEntries(form).Any(e => EntryText(e, "Title") == "保存当前会话" && EntryText(e, "Detail") == "Ctrl+Alt+S"),
                    "command palette uses actual custom shortcut instead of hardcoded default");
                settings.ShortcutsEnabled = false;
                using (var menu = MakeMenu(form, "CreateSessionMenu"))
                    Check(string.IsNullOrEmpty(MenuItem(menu, "保存当前会话").ShortcutKeyDisplayString) &&
                        string.IsNullOrEmpty(MenuItem(menu, "重新打开关闭的标签").ShortcutKeyDisplayString), "disabled shortcuts are absent from session menu");
                Check(CommandEntries(form).Where(e => e.GetType().GetProperty("Value")!.GetValue(e) is HostCommand).All(e => EntryText(e, "Detail") == ""),
                    "disabled shortcuts are absent from command palette");
                foreach (string busyField in new[] { "workspaceOperation", "creation" })
                using (var busy = new CancellationTokenSource())
                {
                    SetMenuField(form, busyField, busy);
                    CheckBusyMenus(form, one, busyField);
                    SetMenuField(form, busyField, null);
                }
                typeof(WorkspaceService).GetField("busy", BindingFlags.Instance | BindingFlags.NonPublic)!.SetValue(service, true);
                CheckBusyMenus(form, one, "Shell busy");
                typeof(WorkspaceService).GetField("busy", BindingFlags.Instance | BindingFlags.NonPublic)!.SetValue(service, false);
                service.Reading = true;
                using (var menu = MakeMenu(form, "CreateLibraryMenu"))
                using (var sessionMenu = MakeMenu(form, "CreateSessionMenu"))
                    Check(!MenuItem(menu, "收藏当前目录…").Enabled && !MenuItem(menu, "保存当前标签组…").Enabled &&
                        !MenuItem(sessionMenu, "保存当前会话").Enabled && MenuItem(menu, "资料 && 开发").Enabled,
                        "directory reader busy disables captures but does not disable independent saved opens");
                service.Reading = false;
                foreach (string busyField in new[] { "closing", "exitPreparing" })
                {
                    SetMenuField(form, busyField, true);
                    CheckBusyMenus(form, one, busyField);
                    using (var menu = MakeMenu(form, "CreateAppMenu"))
                    using (var tray = MakeMenu(form, "CreateTrayMenu"))
                        Check(menu.Items.OfType<ToolStripMenuItem>().All(i => !i.Enabled) && tray.Items.OfType<ToolStripMenuItem>().All(i => !i.Enabled),
                            "app/tray actions disable during " + busyField);
                    SetMenuField(form, busyField, false);
                }
                using (var read = new CancellationTokenSource())
                {
                    UiField<Dictionary<WindowSession, CancellationTokenSource>>(form, "closeReading")[one] = read;
                    using (var menu = MakeMenu(form, "CreateLibraryMenu"))
                    using (var tab = (ContextMenuStrip)UiCall(form, "CreateTabMenu", one)!)
                    using (var detach = MakeMenu(form, "CreateDetachMenu"))
                        Check(!MenuItem(menu, "收藏当前目录…").Enabled && !MenuItem(menu, "保存当前标签组…").Enabled &&
                            !MenuItem(tab, "关闭标签").Enabled && !MenuItem(detach, "分离所有标签").Enabled,
                            "close directory capture disables duplicate close, capture and detach conflicts");
                    UiField<Dictionary<WindowSession, CancellationTokenSource>>(form, "closeReading").Remove(one);
                }
                host.RequestClose(one);
                using (var menu = MakeMenu(form, "CreateSessionMenu"))
                using (var tab = (ContextMenuStrip)UiCall(form, "CreateTabMenu", one)!)
                using (var detach = MakeMenu(form, "CreateDetachMenu"))
                    Check(!MenuItem(menu, "保存当前会话").Enabled && tab.Items.OfType<ToolStripMenuItem>().All(i => !i.Enabled) &&
                        !MenuItem(detach, "分离当前标签").Enabled && !MenuItem(detach, "分离所有标签").Enabled,
                        "pending close disables capture and mutations of closing session without detaching its window");
                host.RetryClose(one);
                typeof(WindowSession).GetProperty("State")!.SetValue(two, SessionState.RecoveryRequired);
                using (var menu = MakeMenu(form, "CreateAppMenu"))
                using (var detach = MakeMenu(form, "CreateDetachMenu"))
                    Check(!MenuItem(menu, "关闭所有标签并退出").Enabled && MenuItem(menu, "退出并保留窗口").Enabled &&
                        MenuItem(detach, "分离所有标签").Enabled, "recovery state offers restore/detach rather than a close-all action that cannot complete");
                typeof(WindowSession).GetProperty("State")!.SetValue(two, SessionState.Attached);
                using (var app = MakeMenu(form, "CreateAppMenu"))
                using (var tray = MakeMenu(form, "CreateTrayMenu"))
                {
                    foreach (var text in new[] { "退出并保留窗口", "关闭所有标签并退出" })
                        Check(MenuItem(app, text).ToolTipText == MenuItem(tray, text).ToolTipText && MenuItem(app, text).Enabled == MenuItem(tray, text).Enabled,
                            "app and tray share exact exit consequence and readiness: " + text);
                    SetMenuField(form, "exitPreparing", true); UiCall(form, "PopulateTrayMenu", tray);
                    Check(tray.Items.OfType<ToolStripMenuItem>().All(i => !i.Enabled), "persistent tray refreshes stale enabled state on each opening");
                    SetMenuField(form, "exitPreparing", false);
                }
                UiField<Stack<WorkspaceTab>>(form, "closedTabs").Push(new WorkspaceTab { Directory = @"C:\Closed" });
                for (int i = host.Sessions.Count; i < 100; i++) { api.Alive.Add(1000 + i); Add(host, 1000 + i); }
                using (var menu = MakeMenu(form, "CreateLibraryMenu"))
                using (var session = MakeMenu(form, "CreateSessionMenu"))
                using (var tab = (ContextMenuStrip)UiCall(form, "CreateTabMenu", one)!)
                    Check(!MenuItem(menu, "资料 && 开发").Enabled && !MenuItem(menu, "项目 && 工具（2 个标签）").Enabled &&
                        !MenuItem(session, "恢复上次会话（追加）").Enabled && !MenuItem(session, "重新打开关闭的标签").Enabled &&
                        !MenuItem(tab, "复制标签").Enabled, "100-tab capacity disables opens/reopen/duplicate before an error or no-op");
            }
            host.DetachAll();
            var restoreApi = new FakeWindows(); var restoreCloser = new TestCloser();
            var restoreHost = new HostController(restoreApi, new FakeStore(), restoreCloser);
            var restoreService = new WorkspaceService(); var restoreStore = new WorkspaceStore { Value = store.Value.Copy() };
            using (var form = WorkspaceForm(restoreHost, restoreService, directory, restoreStore))
            {
                using (var menu = MakeMenu(form, "CreateSessionMenu"))
                {
                    UiField<WorkspaceLibrary>(form, "library").LastSession!.Tabs[0].Directory = @"D:\Changed";
                    MenuItem(menu, "恢复上次会话（追加）").PerformClick();
                    Check(PumpUntil(() => restoreHost.Sessions.Count == 2 && UiField<CancellationTokenSource?>(form, "workspaceOperation") == null, 2000) &&
                        restoreService.CreatedPaths.SequenceEqual(new[] { @"C:\One", @"C:\Two" }), "session restore stays anchored to captured snapshot instead of changed live record");
                }
                using (var menu = MakeMenu(form, "CreateSessionMenu"))
                {
                    MenuItem(menu, "清除上次会话记录").PerformClick();
                    Check(PumpUntil(() => restoreStore.Saves == 1, 2000) && restoreStore.Value.LastSession == null &&
                        restoreStore.Value.Favorites.Count == 1 && restoreStore.Value.Groups.Count == 1 && restoreHost.Sessions.Count == 2 && restoreCloser.Calls.Count == 0,
                        "clear removes only snapshot, persists once and never closes current windows or deletes library entries");
                }
            }
            restoreHost.DetachAll();
            var textHost = new HostController(new FakeWindows(), new FakeStore());
            using (var form = WorkspaceForm(textHost, new WorkspaceService(), directory, new WorkspaceStore()))
            {
                string longTitle = "窗口 & " + string.Concat(Enumerable.Repeat("资料😀", 40));
                var identity = Identity(88); identity.Title = longTitle;
                MainType.GetProperty("ExplorerDiscovery", BindingFlags.Instance | BindingFlags.NonPublic)!.SetValue(form, (Func<IReadOnlyList<WindowIdentity>>)(() => new[] { identity }));
                using (var menu = MakeMenu(form, "CreateAddWindowsMenu"))
                {
                    var candidate = menu.Items.OfType<ToolStripMenuItem>().First();
                    Check(candidate.Text.Contains("&&") && candidate.Text.EndsWith("…") && candidate.Text.Length < 85 &&
                        candidate.ToolTipText.Contains(longTitle) && !candidate.ToolTipText.Contains("PID") && !candidate.ToolTipText.Contains("HWND"),
                        "long candidate titles are bounded without losing full name in hover or introducing identity jargon");
                    Check(IsValidMenuUnicode(candidate.Text), "title shortening does not split surrogate pairs");
                }
                using (var menu = MakeMenu(form, "CreateSessionMenu"))
                    Check(!MenuItem(menu, "恢复上次会话（追加）").Enabled && !MenuItem(menu, "清除上次会话记录").Enabled,
                        "missing snapshot disables both restore and clear");
                Check(HostShortcutCatalog.Name(HostCommand.CloseTab) == "关闭标签" && HostShortcutCatalog.Name(HostCommand.ReopenTab) == "重新打开关闭的标签",
                    "shortcut settings use the same close/reopen names as menus");
                MainType.GetMethod("ConfigureLibrary")!.Invoke(form, new object[] { store, store.Value });
                RenderMenuSamples(form);
            }
        }
        private static void CheckBusyMenus(Form form, WindowSession session, string reason)
        {
            foreach (var factory in new[] { "CreateLibraryMenu", "CreateSessionMenu", "CreateAddWindowsMenu" })
            using (var menu = MakeMenu(form, factory))
                Check(menu.Items.OfType<ToolStripMenuItem>().All(i => !i.Enabled), factory + " disables conflicting actions during " + reason);
            using (var tab = (ContextMenuStrip)UiCall(form, "CreateTabMenu", session)!)
                Check(tab.Items.OfType<ToolStripMenuItem>().All(i => !i.Enabled), "tab context actions disable during " + reason);
            using (var menu = MakeMenu(form, "CreateDetachMenu"))
                Check(!MenuItem(menu, "分离当前标签").Enabled && !MenuItem(menu, "分离所有标签").Enabled, "detach actions disable during " + reason);
            Check(CommandEntries(form).Length == 0, "command palette excludes unavailable actions during " + reason);
        }
        private static bool IsValidMenuUnicode(string value)
        {
            for (int i = 0; i < value.Length; i++)
            {
                if (char.IsLowSurrogate(value[i])) return false;
                if (char.IsHighSurrogate(value[i]) && (++i >= value.Length || !char.IsLowSurrogate(value[i]))) return false;
            }
            return true;
        }
        private static void RenderMenuSamples(Form form)
        {
            string? output = Environment.GetEnvironmentVariable("EXPLORERTABS_TEST_MENU_RENDER_DIRECTORY");
            if (string.IsNullOrEmpty(output)) return;
            Directory.CreateDirectory(output);
            foreach (var factory in new[] { "CreateAppMenu", "CreateDetachMenu", "CreateLibraryMenu", "CreateSessionMenu", "CreateAddWindowsMenu" })
            foreach (float scale in new[] { 1F, 1.25F, 1.5F })
            using (var menu = MakeMenu(form, factory))
            using (var font = new Font("Microsoft YaHei UI", 9F * scale))
            {
                MainType.Assembly.GetType("ExplorerTabs.App.HostTheme", true)!.GetMethod("StyleMenu", BindingFlags.Static | BindingFlags.NonPublic)!.Invoke(null, new object[] { menu });
                menu.Font = font; menu.Size = menu.GetPreferredSize(Size.Empty); menu.CreateControl(); menu.PerformLayout();
                Check(menu.Items.Cast<ToolStripItem>().Where(i => i.Available).All(i => i.Bounds.Bottom <= menu.Height && i.Bounds.Right <= menu.Width),
                    "offscreen menu items fit bounds at scale " + scale + ": " + factory);
                using (var bitmap = new Bitmap(menu.Width, menu.Height))
                {
                    menu.DrawToBitmap(bitmap, new Rectangle(Point.Empty, menu.Size));
                    bitmap.Save(Path.Combine(output, factory + "-" + (int)(scale * 100) + ".png"), System.Drawing.Imaging.ImageFormat.Png);
                }
            }
        }
    }
}
