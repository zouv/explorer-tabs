using System;
using System.Collections.Generic;
using System.Drawing;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Forms;
using ExplorerTabs.Core;
namespace ExplorerTabs.Tests
{
    internal static partial class Program
    {
        private static readonly Type MainType = Assembly.Load("ExplorerTabs").GetType("ExplorerTabs.App.MainForm", true)!;
        private static Form WorkspaceForm(HostController host, WorkspaceService service, string directory, WorkspaceStore store, AppSettings? settings = null)
        {
            var form = (Form)Activator.CreateInstance(MainType, host, new AppLog(Path.Combine(directory, "ui.log")), settings ?? new AppSettings(), null, service)!;
            MakeHidden(form); MainType.GetMethod("ConfigureLibrary")!.Invoke(form, new object[] { store, store.Value }); return form;
        }
        private static Task WorkspaceTask(Form form, string method, params object?[] arguments)
        {
            // Hidden forms do not run Application.Run; DoEvents can uninstall its context between calls.
            SynchronizationContext.SetSynchronizationContext(new WindowsFormsSynchronizationContext());
            return (Task)UiCall(form, method, arguments)!;
        }
        private static void WorkspaceUiSmoke()
        {
            Console.WriteLine("Workspace UI smoke: hidden forms and fake windows/Shell only. No Show, hooks or user window operations.");
            var directory = Path.Combine(Path.GetTempPath(), "ExplorerTabs-workspace-ui-" + Guid.NewGuid().ToString("N")); Directory.CreateDirectory(directory);
            var context = SynchronizationContext.Current; SynchronizationContext.SetSynchronizationContext(new WindowsFormsSynchronizationContext());
            try
            {
                var api = new FakeWindows(); var closer = new TestCloser(); var host = new HostController(api, new FakeStore(), closer); var service = new WorkspaceService(); var store = new WorkspaceStore();
                var one = Add(host, 1); var two = Add(host, 2); host.RenameTab(one, "资料 & 开发"); host.Activate(two, Bounds);
                using (var form = WorkspaceForm(host, service, directory, store))
                {
                    PumpTask(WorkspaceTask(form, "SaveSessionAsync", false));
                    Check(store.Saves == 1 && store.Value.LastSession!.ActiveIndex == 1 && store.Value.LastSession.Tabs[0].Alias == "资料 & 开发" && store.Value.LastSession.Tabs[1].Directory == @"C:\Folder2", "hidden host saves current paths, order, alias and active index");
                    PumpTask(WorkspaceTask(form, "SaveSessionAsync", true)); Check(store.Saves == 1, "unchanged automatic session does not rewrite workspace file");
                    service.FailHandle = 2; PumpTask(WorkspaceTask(form, "SaveSessionAsync", true));
                    Check(store.Saves == 1 && store.Value.LastSession!.Tabs.Count == 2, "partial directory failure preserves complete previous session"); service.FailHandle = 0;
                    service.Paths[1] = @"D:\NewPath"; store.Fail = true; Throws(() => PumpTask(WorkspaceTask(form, "SaveSessionAsync", false)));
                    Check(UiField<WorkspaceLibrary>(form, "library").LastSession!.Tabs[0].Directory == @"C:\Folder1", "failed persistence leaves live workspace library unchanged"); store.Fail = false;
                    service.HoldRead = true; var stale = WorkspaceTask(form, "SaveSessionAsync", true); host.MoveTab(two, 0); service.FinishRead(); PumpTask(stale);
                    Check(store.Saves == 1, "tab order changed during async read rejects stale session instead of committing"); host.MoveTab(one, 0); service.HoldRead = false;
                    var beforeClose = closer.Calls.Count; service.HoldRead = true; var close = WorkspaceTask(form, "CloseTabAsync", one);
                    PumpTask(WorkspaceTask(form, "CloseTabAsync", one)); host.Activate(two, Bounds); service.FinishRead(); PumpTask(close);
                    Check(closer.Calls.Count == beforeClose + 1 && closer.Calls.Last() == 1 && host.Sessions.Count == 2 && !api.Events.Any(e => e.StartsWith("restore")), "rapid duplicate close is coalesced and stays anchored after active tab changes");
                    var before = host.Sessions.ToArray(); api.Alive.Remove(1); host.PruneLost(); UiCall(form, "CompleteClosedTabs", new object[] { before });
                    Check(UiField<Stack<WorkspaceTab>>(form, "closedTabs").Count == 1 && host.Sessions.Single() == two, "confirmed target disappearance populates reopen stack without detach");
                    service.HoldRead = false; service.NextHandle = 3; PumpTask(WorkspaceTask(form, "ReopenTabAsync"));
                    Check(host.Sessions.Count == 2 && host.Active!.Identity.Handle == 3 && host.Active.CustomTitle == "资料 & 开发" && service.CreatedPaths.Last() == @"D:\NewPath" && UiField<Stack<WorkspaceTab>>(form, "closedTabs").Count == 0, "reopen uses captured directory/alias and consumes only successful entry");
                    Check(service.Exclusions.Last().Contains(2), "workspace creation excludes already-managed HWND identities");
                }
                var restoreApi = new FakeWindows(); var restoreHost = new HostController(restoreApi, new FakeStore()); var restoreService = new WorkspaceService(); var library = new WorkspaceStore { Value = new WorkspaceLibrary { LastSession = SampleWorkspace() } };
                using (var form = WorkspaceForm(restoreHost, restoreService, directory, library))
                {
                    Check(UiField<FlowLayoutPanel>(form, "sessionOffer").Visible && restoreHost.Sessions.Count == 0 && restoreService.Creates == 0, "startup offers saved session without auto-opening any Explorer");
                    var saved = SampleWorkspace(); saved.ActiveIndex = 0; PumpTask(WorkspaceTask(form, "OpenWorkspaceAsync", saved));
                    Check(restoreService.CreatedPaths.SequenceEqual(saved.Tabs.Select(t => t.Directory)) && restoreHost.Sessions.Select(s => s.CustomTitle).SequenceEqual(saved.Tabs.Select(t => t.Alias)), "explicit restore serializes creation order and preserves aliases");
                    Check(restoreHost.Active == restoreHost.Sessions[0] && !UiField<FlowLayoutPanel>(form, "sessionOffer").Visible, "restore selects saved active index only after completed creation");
                }
                var cancelHost = new HostController(new FakeWindows(), new FakeStore()); var cancelService = new WorkspaceService { HoldCreate = true };
                using (var form = WorkspaceForm(cancelHost, cancelService, directory, new WorkspaceStore()))
                {
                    var pending = WorkspaceTask(form, "OpenWorkspaceAsync", SampleWorkspace()); UiField<CancellationTokenSource>(form, "workspaceOperation").Cancel(); cancelService.FinishCreate(); PumpTask(pending);
                    Check(cancelHost.Sessions.Count == 0 && cancelService.Creates == 1, "cancelled restore never attaches late success or starts remaining tabs");
                }
                var disposedHost = new HostController(new FakeWindows(), new FakeStore()); var disposeService = new WorkspaceService { HoldCreate = true };
                var disposed = WorkspaceForm(disposedHost, disposeService, directory, new WorkspaceStore());
                var lateRestore = WorkspaceTask(disposed, "OpenWorkspaceAsync", SampleWorkspace()); disposed.Dispose(); disposeService.FinishCreate(); PumpTask(lateRestore);
                Check(disposedHost.Sessions.Count == 0 && disposeService.Creates == 1, "disposed host cannot attach late restore result or continue the batch");
                var disposeCloser = new TestCloser(); var closeHost = new HostController(new FakeWindows(), new FakeStore(), disposeCloser); var closeTarget = Add(closeHost, 1); var closeService = new WorkspaceService { HoldRead = true };
                var closingForm = WorkspaceForm(closeHost, closeService, directory, new WorkspaceStore());
                var lateClose = WorkspaceTask(closingForm, "CloseTabAsync", closeTarget); closingForm.Dispose(); closeService.FinishRead(); PumpTask(lateClose);
                Check(disposeCloser.Calls.Count == 0 && closeHost.Sessions.Count == 1, "dispose cancels directory capture and never sends late close");
                MenuLifetimeTests(directory);
                LayoutAndDialogTests(directory);
                HoverCloseTest(directory);
                CloseFocusTests(directory);
                ExitBehaviorTests(directory);
            }
            finally
            {
                SynchronizationContext.SetSynchronizationContext(context);
                foreach (var own in Directory.GetFiles(directory)) File.Delete(own); Directory.Delete(directory);
            }
        }
        private static void MenuLifetimeTests(string directory)
        {
            using (var form = WorkspaceForm(new HostController(new FakeWindows(), new FakeStore()), new WorkspaceService(), directory, new WorkspaceStore()))
            {
                var reasons = new[] { ToolStripDropDownCloseReason.AppClicked, ToolStripDropDownCloseReason.AppFocusChange, ToolStripDropDownCloseReason.Keyboard, ToolStripDropDownCloseReason.ItemClicked, ToolStripDropDownCloseReason.CloseCalled };
                foreach (var reason in reasons)
                using (var menu = new MenuLifecycleProbe())
                {
                    var parent = new ToolStripMenuItem("Nested menu"); parent.DropDownItems.Add("Child"); menu.Items.Add(parent);
                    var child = parent.DropDown; int disposalCount = 0; menu.Disposed += (_, __) => disposalCount++;
                    UiCall(form, "ShowManagedMenu", menu, UiField<Button>(form, "newTab"));
                    Check(UiField<int>(form, "modalDepth") == 1, "menu opening blocks shortcuts without showing an interactive popup: " + reason);
                    menu.FinishClose(reason); menu.FinishClose(reason);
                    Check(!menu.IsDisposed && !child.IsDisposed && UiField<int>(form, "modalDepth") == 1, "Closed keeps root/submenu and scope alive until close returns: " + reason);
                    Application.DoEvents();
                    Check(menu.IsDisposed && child.IsDisposed && disposalCount == 1 && UiField<int>(form, "modalDepth") == 0, "next UI turn disposes menu tree exactly once and releases scope: " + reason);
                }
                Check(UiField<Dictionary<ContextMenuStrip, Action>>(form, "managedMenuCleanups").Count == 0 && UiField<HashSet<ContextMenuStrip>>(form, "closedManagedMenus").Count == 0, "repeated menu open/close leaves no owned menu or queued cleanup leak");
                using (var screenMenu = new MenuLifecycleProbe())
                {
                    UiCall(form, "ShowManagedMenuAtScreenPoint", screenMenu, new Point(200, 200));
                    Check(UiField<int>(form, "modalDepth") == 1, "screen-point app menu opens with modal scope");
                    screenMenu.FinishClose(ToolStripDropDownCloseReason.AppClicked);
                    Application.DoEvents();
                    Check(screenMenu.IsDisposed && UiField<int>(form, "modalDepth") == 0, "screen-point app menu disposes after the next UI turn");
                }
                using (var cancelled = new MenuLifecycleProbe())
                {
                    cancelled.Opening += (_, e) => e.Cancel = true;
                    UiCall(form, "ShowManagedMenu", cancelled, UiField<Button>(form, "newTab"));
                    Check(UiField<int>(form, "modalDepth") == 0 && !cancelled.IsDisposed, "cancelled opening before scope never enables modal interaction");
                    Application.DoEvents(); Check(cancelled.IsDisposed, "cancelled Opening without Closed still disposes temporary menu");
                }
                using (var cancelledLate = new MenuLifecycleProbe { CancelAfterOpening = true })
                {
                    UiCall(form, "ShowManagedMenu", cancelledLate, UiField<Button>(form, "newTab"));
                    Check(UiField<int>(form, "modalDepth") == 1, "late Opening cancellation retains scope until show returns");
                    Application.DoEvents(); Check(cancelledLate.IsDisposed && UiField<int>(form, "modalDepth") == 0, "late cancellation releases scope without relying on Closed");
                }
                using (var failed = new MenuLifecycleProbe { ThrowAfterOpening = true })
                {
                    Throws(() => UiCall(form, "ShowManagedMenu", failed, UiField<Button>(form, "newTab")));
                    Application.DoEvents(); Check(failed.IsDisposed && UiField<int>(form, "modalDepth") == 0, "failed menu show releases registered ownership and modal scope");
                }
                using (var clicked = new MenuLifecycleProbe())
                {
                    bool ran = false, cleanupBeforeAction = false;
                    clicked.Items.Add("Queued action", null, (_, __) => UiCall(form, "QueueMenuAction", (Action)(() => { ran = true; cleanupBeforeAction = clicked.IsDisposed && UiField<int>(form, "modalDepth") == 0; })));
                    UiCall(form, "RegisterManagedMenu", clicked); clicked.RaiseOpening();
                    clicked.Items[0].PerformClick(); clicked.FinishClose(ToolStripDropDownCloseReason.ItemClicked);
                    Check(!ran && !clicked.IsDisposed, "Item.Click cannot execute queued action or dispose menu inside framework close stack");
                    Application.DoEvents(); Check(ran && cleanupBeforeAction, "click posted before Closed still drains closed menus before running command");
                }
                var log = File.ReadAllText(Path.Combine(directory, "ui.log"));
                Check(log.Contains("Menu opened:") && log.Contains("Menu close queued:") && log.Contains("Menu disposed:") && log.Contains("Menu show failed:"), "menu lifecycle and show failure emit diagnostics without swallowing the failure");
            }
            var groupedApi = new FakeWindows(); var groupedHost = new HostController(groupedApi, new FakeStore()); Add(groupedHost, 1, HostingMode.Grouped);
            using (var form = WorkspaceForm(groupedHost, new WorkspaceService(), directory, new WorkspaceStore(), new AppSettings { Mode = HostingMode.Grouped }))
            using (var menu = new MenuLifecycleProbe())
            {
                UiCall(form, "RegisterManagedMenu", menu); menu.RaiseOpening();
                Check(!groupedApi.Visible.Contains(1), "grouped Explorer is hidden while menu is open");
                menu.FinishClose(ToolStripDropDownCloseReason.AppClicked);
                Check(!groupedApi.Visible.Contains(1), "Closed does not re-show grouped Explorer inside framework dropdown close stack");
                Application.DoEvents(); Check(groupedApi.Visible.Contains(1) && menu.IsDisposed, "next UI turn restores grouped Explorer after dropdown close completes");
            }
            {
                var form = WorkspaceForm(new HostController(new FakeWindows(), new FakeStore()), new WorkspaceService(), directory, new WorkspaceStore()); var menu = new MenuLifecycleProbe();
                UiCall(form, "RegisterManagedMenu", menu); menu.RaiseOpening();
                UiAnyCall(form, "DestroyHandle"); menu.FinishClose(ToolStripDropDownCloseReason.CloseCalled);
                Check(!menu.IsDisposed && !form.IsHandleCreated, "lost host handle never forces synchronous disposal from Closed");
                form.Dispose(); Check(menu.IsDisposed && UiField<int>(form, "modalDepth") == 0, "host Dispose owns cleanup when no UI handle can accept a callback");
            }
            foreach (bool closeFirst in new[] { false, true })
            {
                var api = new FakeWindows(); var host = new HostController(api, new FakeStore()); Add(host, 1);
                var form = WorkspaceForm(host, new WorkspaceService(), directory, new WorkspaceStore()); var menu = new MenuLifecycleProbe();
                bool ran = false; UiCall(form, "RegisterManagedMenu", menu); menu.RaiseOpening();
                UiCall(form, "QueueMenuAction", (Action)(() => ran = true));
                if (closeFirst) menu.FinishClose(ToolStripDropDownCloseReason.CloseCalled);
                int beforeDispose = api.Events.Count; form.Dispose(); Application.DoEvents();
                Check(menu.IsDisposed && UiField<int>(form, "modalDepth") == 0 && UiField<Dictionary<ContextMenuStrip, Action>>(form, "managedMenuCleanups").Count == 0, "host Dispose cleans open/pending menus even if posted callback never runs: " + closeFirst);
                Check(!ran && api.Events.Count == beforeDispose, "Dispose skips pending menu commands and never re-shows fake Explorer: " + closeFirst);
            }
            foreach (var command in new[] { "恢复默认名称", "分离为独立窗口" })
            {
                var host = new HostController(new FakeWindows(), new FakeStore()); var first = Add(host, 1); Add(host, 2); host.RenameTab(first, "Alias");
                using (var form = WorkspaceForm(host, new WorkspaceService(), directory, new WorkspaceStore()))
                {
                    UiCall(form, "RefreshTabs"); var strip = UiField<FlowLayoutPanel>(form, "tabs"); var menu = strip.Controls[0].ContextMenuStrip!;
                    UiAnyCall(menu, "OnOpening", new System.ComponentModel.CancelEventArgs());
                    menu.Items.Cast<ToolStripItem>().First(item => item.Text == command).PerformClick();
                    Check(!menu.IsDisposed && host.Sessions[0] == first && first.CustomTitle == "Alias", "tab menu command defers render/detach until native click stack returns: " + command);
                    UiAnyCall(menu, "OnClosed", new ToolStripDropDownClosedEventArgs(ToolStripDropDownCloseReason.ItemClicked));
                    Check(menu.Handle != IntPtr.Zero && !menu.IsDisposed, "tab menu handle survives Closed before queued mutation: " + command);
                    Application.DoEvents();
                    bool result = command == "恢复默认名称" ? first.CustomTitle == null : !host.Sessions.Contains(first);
                    Check(result && menu.IsDisposed && UiField<int>(form, "modalDepth") == 0, "queued tab mutation completes and safely replaces old menu: " + command);
                }
            }
        }
        // Exercise real ContextMenuStrip events/handles, but never show a native popup or run a host.
        private sealed class MenuLifecycleProbe : ContextMenuStrip
        {
            public bool CancelAfterOpening, ThrowAfterOpening;
            public void RaiseOpening() => OnOpening(new System.ComponentModel.CancelEventArgs());
            protected override void SetVisibleCore(bool visible)
            {
                if (!visible) return;
                var args = new System.ComponentModel.CancelEventArgs(); OnOpening(args);
                if (ThrowAfterOpening) throw new InvalidOperationException("Fake menu Show failure");
                if (CancelAfterOpening) args.Cancel = true;
                if (!args.Cancel) OnOpened(EventArgs.Empty);
            }
            public void FinishClose(ToolStripDropDownCloseReason reason)
            {
                OnClosed(new ToolStripDropDownClosedEventArgs(reason));
                // WinForms still accesses the dropdown handle after Closed (the user's crashing stack).
                var handle = Handle;
                Check(handle != IntPtr.Zero, "framework can access dropdown handle after Closed");
            }
        }
        private static object? UiAnyCall(object target, string method, params object?[] arguments) =>
            target.GetType().GetMethod(method, BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic)!.Invoke(target, arguments);
        private static void HoverCloseTest(string directory)
        {
            var api = new FakeWindows(); var closer = new TestCloser(); var host = new HostController(api, new FakeStore(), closer);
            var one = Add(host, 1); host.Activate(one, Bounds);
            using (var form = WorkspaceForm(host, new WorkspaceService(), directory, new WorkspaceStore()))
            {
                form.Width = 720; UiCall(form, "RefreshTabs"); form.PerformLayout();
                var tabs = UiField<FlowLayoutPanel>(form, "tabs");
                var button = TabControls(tabs).Single();
                var buttonType = button.GetType();
                buttonType.GetMethod("OnMouseEnter", BindingFlags.Instance | BindingFlags.NonPublic)!.Invoke(button, new object[] { EventArgs.Empty });
                var point = new Point(button.Width - 10, button.Height / 2);
                var mouse = new MouseEventArgs(MouseButtons.Left, 1, point.X, point.Y, 0);
                buttonType.GetMethod("OnMouseDown", BindingFlags.Instance | BindingFlags.NonPublic)!.Invoke(button, new object[] { mouse });
                buttonType.GetMethod("OnMouseUp", BindingFlags.Instance | BindingFlags.NonPublic)!.Invoke(button, new object[] { mouse });
                for (int i = 0; i < 40 && closer.Calls.Count == 0; i++) { Application.DoEvents(); Thread.Sleep(10); }
                Check(closer.Calls.Count == 1 && closer.Calls[0] == 1, "hover close button queues a precise Explorer close");
            }
            host.DetachAll();
        }

        private static void CloseFocusTests(string directory)
        {
            var api = new FakeWindows(); var host = new HostController(api, new FakeStore());
            var one = Add(host, 1); var two = Add(host, 2); var three = Add(host, 3);
            host.Activate(two, Bounds);
            using (var form = WorkspaceForm(host, new WorkspaceService(), directory, new WorkspaceStore()))
            {
                api.Alive.Remove(2); UiCall(form, "PollWindows");
                Check(host.Active == one, "closing the active middle tab focuses its left neighbour");
                host.Activate(one, Bounds); api.Alive.Remove(1); UiCall(form, "PollWindows");
                Check(host.Active == three, "closing the leftmost active tab falls back to the right neighbour");
                api.Alive.Add(4); Add(host, 4); host.Activate(three, Bounds); api.Alive.Remove(4); UiCall(form, "PollWindows");
                Check(host.Active == three, "closing a background tab keeps the active tab");
            }
            host.DetachAll();
        }

        private static void ExitBehaviorTests(string directory)
        {
            var api = new FakeWindows(); var closer = new TestCloser(); var host = new HostController(api, new FakeStore(), closer);
            Add(host, 1); Add(host, 2);
            using (var form = WorkspaceForm(host, new WorkspaceService(), directory, new WorkspaceStore()))
            {
                form.Close();
                Check(!form.IsDisposed && closer.Calls.Count == 2 && closer.Calls.Contains(1) && closer.Calls.Contains(2) &&
                    !api.Events.Any(e => e.StartsWith("restore")),
                    "user close requests every session window instead of restoring them");
                api.Alive.Remove(1); api.Alive.Remove(2);
                UiCall(form, "PollWindows"); Application.DoEvents();
                Check(form.IsDisposed && host.Sessions.Count == 0, "host exits after every session window has closed");
            }
            var restoreApi = new FakeWindows(); var restoreHost = new HostController(restoreApi, new FakeStore());
            Add(restoreHost, 1); Add(restoreHost, 2);
            using (var form = WorkspaceForm(restoreHost, new WorkspaceService(), directory, new WorkspaceStore()))
            {
                UiCall(form, "CloseRestoringWindows"); Application.DoEvents();
                Check(form.IsDisposed && restoreApi.Events.Contains("restore:1") && restoreApi.Events.Contains("restore:2"),
                    "explicit restore-and-exit path detaches windows instead of closing them");
            }
        }

        private static void LayoutAndDialogTests(string directory)
        {
            var api = new FakeWindows(); var host = new HostController(api, new FakeStore());
            for (int i = 1; i <= 100; i++) { api.Alive.Add(i); Add(host, i); } host.Activate(host.Sessions[0], Bounds);
            using (var form = WorkspaceForm(host, new WorkspaceService(), directory, new WorkspaceStore(), new AppSettings { WrapTabs = true, MaximumTabRows = 3 }))
            {
                form.Width = 720; UiCall(form, "RefreshTabs"); form.PerformLayout(); var tabs = UiField<FlowLayoutPanel>(form, "tabs"); tabs.PerformLayout();
                var controls = TabControls(tabs);
                Check(tabs.WrapContents && tabs.Height <= tabs.Padding.Vertical + 3 * 38 && controls.Select(c => c.Top).Distinct().Count() > 3, "100 wrapped tabs cap visible rows while retaining all rows");
                Check(tabs.VerticalScroll.Visible && !tabs.HorizontalScroll.Visible, "wrapped overflow uses vertical scrolling only");
                Check(controls.All(c => c.Left >= 0 && c.Right <= tabs.ClientSize.Width), "all wrapped buttons fit client width including scrollbar");
                UiAnyCall(tabs, "Reveal", host.Sessions.Last()); Check(controls.Last().Bottom <= tabs.ClientSize.Height && tabs.AutoScrollPosition.Y < 0, "last wrapped tab reveals within capped viewport");
                int offset = tabs.AutoScrollPosition.Y; UiCall(form, "RefreshTabs"); Check(tabs.AutoScrollPosition.Y == offset, "wrapped metadata redraw preserves vertical viewport");
                tabs.AutoScrollPosition = Point.Empty; tabs.PerformLayout(); controls = TabControls(tabs); var source = controls[0]; var target = controls[5];
                UiCall(tabs, "BeginPointer", source, MouseButtons.Left, new Point(source.Left + 10, source.Top + 10)); UiCall(tabs, "MovePointer", source, new Point(target.Right - 2, target.Top + 15));
                var move = UiCall(tabs, "EndPointer", source, MouseButtons.Left, new Point(target.Right - 2, target.Top + 15))!;
                Check((int)move.GetType().GetProperty("Index")!.GetValue(move)! == 5 && host.Sessions[0].Identity.Handle == 1, "cross-row drag computes visual insertion without native mutations before drop dispatch");
                UiCall(tabs, "PublishMove", move); Check(host.Sessions[5].Identity.Handle == 1, "cross-row drop reaches host reorder");
                form.Width = 1100; form.PerformLayout(); tabs.PerformLayout(); Check(tabs.Height <= tabs.Padding.Vertical + 3 * 38 && !tabs.HorizontalScroll.Visible, "width change recalculates multirow layout without horizontal overflow");
                UiAnyCall(tabs, "ConfigureLayout", false, 3); tabs.PerformLayout(); Check(!tabs.WrapContents && TabControls(tabs).All(c => c.Width >= 110 && c.Width <= 280) && tabs.HorizontalScroll.Visible, "disabling wrap retains adaptive widths and horizontal overflow");
                Check(!api.Events.Any(e => e.StartsWith("restore")), "multirow layout and reorder never detach Explorer");
            }
            var settingsType = Assembly.Load("ExplorerTabs").GetType("ExplorerTabs.App.SettingsDialog", true)!;
            using (var dialog = (Form)Activator.CreateInstance(settingsType, new AppSettings(), false, true)!)
            {
                MakeHidden(dialog); Check(!dialog.ShowIcon && HasWindowRegion(dialog), "settings dialog removes the inherited title-bar icon and applies a small rounded region");
                Check(dialog.Padding.All == 1, "settings dialog reserves the shared 1px border ring");
                using (var bitmap = new Bitmap(dialog.Width, dialog.Height))
                {
                    dialog.DrawToBitmap(bitmap, dialog.ClientRectangle);
                    Check(bitmap.GetPixel(0, bitmap.Height / 2).ToArgb() == Color.FromArgb(196, 208, 226).ToArgb(),
                        "settings dialog paints the shared border colour at its edge");
                }
                Check(!UiField<CheckBox>(dialog, "wrap").Checked && !UiField<NumericUpDown>(dialog, "maximumRows").Enabled, "settings expose wrap off by default with row limit disabled");
                UiField<CheckBox>(dialog, "wrap").Checked = true; UiField<NumericUpDown>(dialog, "maximumRows").Value = 5;
                var grid = UiField<DataGridView>(dialog, "bindings"); grid.Rows[1].Cells[1].Value = grid.Rows[0].Cells[1].Value; ((Button)dialog.AcceptButton!).PerformClick();
                Check(dialog.DialogResult == DialogResult.None && UiField<Label>(dialog, "error").Text.Length > 0, "duplicate binding shows inline validation and keeps settings open");
                grid.Rows[1].Cells[1].Value = ""; ((Button)dialog.AcceptButton!).PerformClick(); var result = (AppSettings)settingsType.GetProperty("Result")!.GetValue(dialog)!;
                Check(dialog.DialogResult == DialogResult.OK && result.WrapTabs && result.MaximumTabRows == 5 && result.KeyBindings[1].Chord == "", "settings apply wrap/row count and explicitly disabled key");
            }
            using (var picker = CreatePicker())
            {
                MakeHidden(picker); Check(!picker.ShowIcon && HasWindowRegion(picker), "picker dialog removes the inherited title-bar icon and applies a small rounded region");
                Check(picker.Padding.All == 1, "picker dialog reserves the shared 1px border ring");
                using (var bitmap = new Bitmap(picker.Width, picker.Height))
                {
                    picker.DrawToBitmap(bitmap, picker.ClientRectangle);
                    Check(bitmap.GetPixel(0, bitmap.Height / 2).ToArgb() == Color.FromArgb(196, 208, 226).ToArgb(),
                        "picker dialog paints the shared border colour at its edge");
                }
                var query = UiField<TextBox>(picker, "query"); var results = UiField<ListBox>(picker, "results");
                query.Text = "项目 assets"; Check(results.Items.Count == 1 && picker.GetType().GetProperty("SelectedValue")!.GetValue(picker)!.Equals(2), "picker filters across title and path and retains exact session value");
                query.Text = "no match"; UiCall(picker, "AcceptSelection"); Check(results.Items.Count == 0 && picker.DialogResult == DialogResult.None, "no-match picker cannot activate a stale selection");
                query.Text = ""; UiCall(picker, "QueryKey", null, new KeyEventArgs(Keys.Down)); Check(results.SelectedIndex == 1, "picker arrow key selects next result");
                var message = Message.Create(IntPtr.Zero, 0, IntPtr.Zero, IntPtr.Zero); UiCall(picker, "ProcessCmdKey", message, Keys.Escape); Check(picker.DialogResult == DialogResult.Cancel, "picker Esc cancels without switching tabs");
            }
        }
        private static Form CreatePicker()
        {
            var entryType = Assembly.Load("ExplorerTabs").GetType("ExplorerTabs.App.PickerEntry", true)!; var array = Array.CreateInstance(entryType, 3);
            array.SetValue(Activator.CreateInstance(entryType, "1  常用资料", @"C:\Documents", 1), 0); array.SetValue(Activator.CreateInstance(entryType, "2  项目 & 开发", @"D:\Project\Assets", 2), 1);
            array.SetValue(Activator.CreateInstance(entryType, "3  很长的标签名称与路径也可按关键字快速搜索", @"F:\Workspace\Archive", 3), 2);
            return (Form)Activator.CreateInstance(Assembly.Load("ExplorerTabs").GetType("ExplorerTabs.App.QuickPickerDialog", true)!, "搜索标签 · Ctrl+P", array)!;
        }
        private static void RenderWorkspace(string output, int width)
        {
            Application.EnableVisualStyles(); Application.SetCompatibleTextRenderingDefault(false);
            var api = new FakeWindows(); var host = new HostController(api, new FakeStore());
            using (var form = (Form)Activator.CreateInstance(MainType, host, new AppLog(Path.Combine(Path.GetTempPath(), "ExplorerTabs-render-workspace.log")), new AppSettings { WrapTabs = true }, null, new WorkspaceService())!)
            {
                form.Width = width; for (int i = 1; i <= 24; i++) { api.Alive.Add(i); var session = Add(host, i); host.RenameTab(session, i % 3 == 0 ? "美术资源 & Assets " + i : "工作空间 · 项目 " + i); }
                host.Activate(host.Sessions[1], Bounds); UiCall(form, "RefreshTabs"); RenderHiddenForm(form, output);
            }
        }
        private static void RenderPicker(string output) { Application.EnableVisualStyles(); Application.SetCompatibleTextRenderingDefault(false); using (var picker = CreatePicker()) RenderHiddenForm(picker, output); }
        private static void RenderSettingsKeys(string output)
        {
            Application.EnableVisualStyles(); Application.SetCompatibleTextRenderingDefault(false);
            var type = Assembly.Load("ExplorerTabs").GetType("ExplorerTabs.App.SettingsDialog", true)!;
            using (var form = (Form)Activator.CreateInstance(type, new AppSettings(), false, true)!)
            { MakeHidden(form); var outer = form.Controls.OfType<TableLayoutPanel>().Single(); outer.Controls.OfType<TabControl>().Single().SelectedIndex = 1; RenderHiddenForm(form, output); }
        }
        private sealed class WorkspaceStore : IWorkspaceLibraryStore
        { public WorkspaceLibrary Value = new WorkspaceLibrary(); public int Saves; public bool Fail; public WorkspaceLibrary? Read() => Value.Copy(); public void Save(WorkspaceLibrary library) { if (Fail) throw new IOException("fake disk error"); Saves++; Value = library.Copy(); } }
        private sealed class WorkspaceService : IExplorerTabService, IExplorerDirectoryService
        {
            private bool busy; public bool IsBusy => busy; public bool IsReading => false; public bool HoldRead, HoldCreate; public long FailHandle, NextHandle = 1; public int Creates;
            public readonly Dictionary<long, string> Paths = new Dictionary<long, string>(); public readonly List<string> CreatedPaths = new List<string>(); public readonly List<long[]> Exclusions = new List<long[]>();
            private TaskCompletionSource<string>? read; private TaskCompletionSource<CreatedExplorer>? create; private string heldPath = "", heldCreatePath = ""; private CancellationTokenRegistration registration;
            public Task<string> ReadDirectoryAsync(WindowIdentity identity, CancellationToken token)
            {
                token.ThrowIfCancellationRequested(); if (identity.Handle == FailHandle) return Task.FromException<string>(new IOException("virtual/missing directory"));
                string path = Paths.TryGetValue(identity.Handle, out var p) ? p : @"C:\Folder" + identity.Handle;
                if (!HoldRead) return Task.FromResult(path); heldPath = path; read = new TaskCompletionSource<string>(TaskCreationOptions.RunContinuationsAsynchronously); registration = token.Register(() => read.TrySetCanceled()); return read.Task;
            }
            public void FinishRead() { registration.Dispose(); HoldRead = false; read!.TrySetResult(heldPath); }
            public Task<CreatedExplorer> CreateAsync(string directory, WindowIdentity? source, IReadOnlyCollection<long> attached, CancellationToken token)
            {
                Creates++; CreatedPaths.Add(directory); Exclusions.Add(attached.ToArray());
                if (!HoldCreate) return Task.FromResult(new CreatedExplorer(Identity(NextHandle++), directory)); busy = true; heldCreatePath = directory; create = new TaskCompletionSource<CreatedExplorer>(TaskCreationOptions.RunContinuationsAsynchronously); return create.Task;
            }
            public void FinishCreate() { busy = false; HoldCreate = false; create!.TrySetResult(new CreatedExplorer(Identity(NextHandle++), heldCreatePath)); }
        }
    }
}
