using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Forms;
using ExplorerTabs.Core;
namespace ExplorerTabs.Tests
{
    internal static partial class Program
    {
        private static void ResponsivenessUnitTests()
        {
            Check(WorkspaceCapturePolicy.TimeoutMilliseconds(8, true) >= 8 * WorkspaceCapturePolicy.ItemTimeoutMilliseconds + 5000,
                "automatic capture budgets every serial tab, rather than fixed seven seconds");
            Check(WorkspaceCapturePolicy.TimeoutMilliseconds(100, true) == 265000 && WorkspaceCapturePolicy.TimeoutMilliseconds(int.MaxValue, false) == 265000,
                "session capture budget is capped to the supported 100-tab library");
            Check(WorkspaceCapturePolicy.TimeoutMilliseconds(100, true, true) == 30000 && WorkspaceCapturePolicy.TimeoutMilliseconds(0, true, true) == 7000,
                "exit capture has a separate bounded budget");
            Throws(() => WorkspaceCapturePolicy.TimeoutMilliseconds(-1, true));
            var api = new FakeWindows(); var journal = new FakeStore(); var host = new HostController(api, journal);
            var first = Add(host, 1, HostingMode.Grouped); var second = Add(host, 2, HostingMode.Grouped); var other = Add(host, 3, HostingMode.Grouped);
            host.Activate(first, Bounds); api.Events.Clear();
            host.Activate(second, Bounds, false, false);
            Check(host.Active == second && api.Visible.Count == 0 && !api.Events.Any(e => e.StartsWith("show:") || e.StartsWith("layout:")),
                "hidden activation updates active model without showing/layout of external windows");
            api.FailHideHandle = 1; api.Events.Clear(); Throws(() => host.Activate(first, Bounds, true, false));
            Check(host.Active == second && api.Visible.Count == 0 && !api.Events.Any(e => e.StartsWith("show:")),
                "hidden activation rollback also stays hidden");
            api.Alive.Remove(1); api.Alive.Remove(3);
            host.PruneTargets(new[] { first });
            Check(!host.Sessions.Contains(first) && host.Sessions.Contains(other) && journal.Snapshots.Count == 2,
                "targeted close prune does not sweep unrelated lost windows");
            api.ThrowOnMatch = true; Throws(() => host.PruneTargets(new[] { second }));
            Check(host.Sessions.Contains(second) && journal.Snapshots.Count == 2,
                "unknown target identity retains session and durable journal");
        }

        private static void ResponsivenessUiTests(string directory)
        {
            var api = new FakeWindows(); var journal = new FakeStore(); var host = new HostController(api, journal);
            var old = Add(host, 1); host.Activate(old, Bounds); var service = new WorkspaceService { HoldCreate = true, NextHandle = 2 };
            using (var form = WorkspaceForm(host, service, directory, new WorkspaceStore()))
            {
                UiCall(form, "RefreshTabs"); var strip = UiField<FlowLayoutPanel>(form, "tabs"); var oldButton = TabControls(strip).Single();
                var create = WorkspaceTask(form, "CreateTabAsync", new object?[] { null });
                var pending = TabControls(strip).Single(c => c != oldButton);
                Check(!pending.Enabled && pending.ContextMenuStrip == null && pending.Text.Contains("正在打开") && host.Sessions.Count == 1 && journal.Snapshots.Count == 1,
                    "new tab immediately has a disabled placeholder with no HWND identity or journal entry");
                UiCall(form, "RefreshTabs"); Check(TabControls(strip).Contains(pending) && TabControls(strip).Contains(oldButton),
                    "monitor/render retains pending placeholder and existing button");
                service.FinishCreate(); PumpTask(create);
                Check(TabControls(strip).Contains(pending) && pending.Enabled && !oldButton.IsDisposed && host.Sessions.Count == 2 && journal.Snapshots.Count == 2,
                    "successful creation binds the original placeholder, without rebuilding existing tab HWNDs");
                Check(TabControls(strip).All(c => !c.Text.Contains("正在打开")), "completed creation clears pending presentation");
                service.HoldCreate = true; service.NextHandle = 3;
                var cancel = WorkspaceTask(form, "CreateTabAsync", new object?[] { null });
                UiField<CancellationTokenSource>(form, "creation").Cancel(); service.FinishCreate(); PumpTask(cancel);
                Check(host.Sessions.Count == 2 && TabControls(strip).Length == 2 && journal.Snapshots.Count == 2,
                    "cancelled late creation removes placeholder and never journals/attaches late success");
            }
            var hiddenApi = new FakeWindows(); var hiddenHost = new HostController(hiddenApi, new FakeStore());
            var a = Add(hiddenHost, 1, HostingMode.Grouped); var b = Add(hiddenHost, 2, HostingMode.Grouped); hiddenHost.Activate(a, Bounds);
            var hiddenService = new WorkspaceService { HoldCreate = true, NextHandle = 3 };
            using (var form = WorkspaceForm(hiddenHost, hiddenService, directory, new WorkspaceStore(), new AppSettings { Mode = HostingMode.Grouped }))
            {
                var create = WorkspaceTask(form, "CreateTabAsync", new object?[] { null });
                hiddenHost.SetActiveVisible(false); form.Hide(); hiddenApi.Events.Clear(); hiddenApi.Alive.Remove(1);
                UiCall(form, "PollWindows");
                Check(hiddenHost.Active == b && hiddenApi.Visible.Count == 0 && !hiddenApi.Events.Any(e => e.StartsWith("show:")),
                    "hidden host polling/pruning never resurrects grouped Explorer");
                UiAnyCall(form, "OnResize", EventArgs.Empty); UiAnyCall(form, "OnActivated", EventArgs.Empty);
                Check(hiddenApi.Visible.Count == 0 && !hiddenApi.Events.Any(e => e.StartsWith("show:")),
                    "hidden host resize/activation callbacks do not show external windows");
                hiddenService.FinishCreate(); PumpTask(create);
                Check(hiddenHost.Active!.Identity.Handle == 3 && hiddenApi.Visible.Count == 0,
                    "creation finishing while host is hidden does not show external Explorer");
                using ((IDisposable)UiCall(form, "EnterModal")!) { }
                Check(hiddenApi.Visible.Count == 0, "modal cleanup also honors hidden host visibility");
            }
            FastCloseUiTests(directory);
            UncancellableCloseReadTest(directory);
            PersistentTabMenuTests(directory);
        }

        private static void FastCloseUiTests(string directory)
        {
            var api = new FakeWindows(); var journal = new FakeStore(); var closer = new TestCloser(); var host = new HostController(api, journal, closer);
            var first = Add(host, 1); var target = Add(host, 2); var other = Add(host, 3); host.Activate(target, Bounds);
            var service = new WorkspaceService { HoldRead = true };
            using (var form = WorkspaceForm(host, service, directory, new WorkspaceStore()))
            {
                UiCall(form, "RefreshTabs"); var strip = UiField<FlowLayoutPanel>(form, "tabs"); var before = TabControls(strip);
                UiField<Dictionary<WindowSession, string>>(form, "directoryCache")[target] = @"C:\Stale";
                var watch = Stopwatch.StartNew(); var close = WorkspaceTask(form, "CloseTabAsync", target);
                Check(TabControls(strip).Any(c => c.Text.Contains("关闭中")) && closer.Calls.Count == 0,
                    "close gives immediate closing feedback before asynchronous directory read");
                PumpTask(close);
                Check(watch.ElapsedMilliseconds < 1000 && closer.Calls.SequenceEqual(new long[] { 2 }),
                    "slow directory capture no longer holds close for 1.2 seconds");
                Check(host.Active == first && host.Sessions.Count == 3 && journal.Snapshots.Count == 3 && TabControls(strip).SequenceEqual(before),
                    "close request switches to left neighbor immediately while retaining closing tab/journal/buttons");
                PumpTask(WorkspaceTask(form, "CloseTabAsync", target));
                Check(closer.Calls.Count == 1, "repeated close never sends a second message while pending");
                api.Alive.Remove(2); api.Alive.Remove(3);
                UiCall(form, "PollClosingWindows");
                Check(!host.Sessions.Contains(target) && host.Sessions.Contains(other) && journal.Snapshots.Count == 2 && before[1].IsDisposed && !before[0].IsDisposed,
                    "fast target confirmation removes only disappeared close target and its button");
                Check(UiField<Stack<WorkspaceTab>>(form, "closedTabs").Count == 0,
                    "timed-out read does not add a stale cache entry to reopen stack");
            }
            api = new FakeWindows(); journal = new FakeStore(); closer = new TestCloser(); host = new HostController(api, journal, closer);
            target = Add(host, 1); host.Activate(target, Bounds);
            using (var form = WorkspaceForm(host, new WorkspaceService(), directory, new WorkspaceStore()))
            {
                PumpTask(WorkspaceTask(form, "CloseTabAsync", target)); api.Alive.Remove(1); journal.Fail = true;
                UiCall(form, "PollClosingWindows");
                Check(host.Sessions.Contains(target) && journal.Snapshots.Count == 1, "fast close journal failure retains recovery model and snapshot");
                journal.Fail = false; UiCall(form, "PollClosingWindows");
                Check(host.Sessions.Count == 0 && journal.Snapshots.Count == 0 && UiField<Stack<WorkspaceTab>>(form, "closedTabs").Count == 1,
                    "fast close retries journal cleanup and pushes fresh reopen directory only after confirmation");
            }
            api = new FakeWindows(); host = new HostController(api, new FakeStore(), new TestCloser()); target = Add(host, 1); host.Activate(target, Bounds);
            using (var form = WorkspaceForm(host, new WorkspaceService(), directory, new WorkspaceStore()))
            {
                PumpTask(WorkspaceTask(form, "CloseTabAsync", target));
                typeof(WindowSession).GetProperty("CloseRequestedUtc")!.SetValue(target, DateTime.UtcNow.AddSeconds(-7));
                UiCall(form, "PollClosingWindows");
                Check(host.Sessions.Contains(target) && target.CloseRequestedUtc == null && !TabControls(UiField<FlowLayoutPanel>(form, "tabs")).Single().Text.Contains("关闭中"),
                    "refused/blocked close keeps live tab and clears pending state for retry");
            }
        }

        private static void UncancellableCloseReadTest(string directory)
        {
            var api = new FakeWindows(); var journal = new FakeStore(); var closer = new TestCloser(); var host = new HostController(api, journal, closer);
            var target = Add(host, 1); host.Activate(target, Bounds); var service = new UncancellableDirectoryService();
            using (var form = (Form)Activator.CreateInstance(MainType, host, new AppLog(System.IO.Path.Combine(directory, "ui.log")), new AppSettings(), null, service)!)
            {
                MakeHidden(form); UiCall(form, "RefreshTabs");
                var watch = Stopwatch.StartNew(); var close = WorkspaceTask(form, "CloseTabAsync", target); PumpTask(close);
                Check(watch.ElapsedMilliseconds < 1000 && closer.Calls.Count == 1 && !service.Read.Task.IsCompleted,
                    "uncancellable Shell read cannot hold the close request beyond bounded grace period");
                Check(UiField<System.Windows.Forms.Timer>(form, "closeMonitor").Enabled, "pending close starts dedicated fast confirmation timer");
                service.Read.SetResult(@"C:\Late"); PumpFor(40);
                Check(!UiField<Dictionary<WindowSession, string>>(form, "directoryCache").ContainsKey(target),
                    "late uncancellable read is observed/discarded and cannot update directory cache");
                api.Alive.Remove(1);
                Check(PumpUntil(() => host.Sessions.Count == 0, 750) && journal.Snapshots.Count == 0,
                    "fast timer confirms disappearance without waiting for 1.5-second full monitor");
                Check(!UiField<System.Windows.Forms.Timer>(form, "closeMonitor").Enabled && UiField<Stack<WorkspaceTab>>(form, "closedTabs").Count == 0,
                    "confirmation stops timer and never reopens a late directory result");
            }
        }
        private sealed class UncancellableDirectoryService : IExplorerTabService, IExplorerDirectoryService
        {
            public readonly TaskCompletionSource<string> Read = new TaskCompletionSource<string>(TaskCreationOptions.RunContinuationsAsynchronously);
            private bool started;
            public bool IsBusy => false; public bool IsReading => started && !Read.Task.IsCompleted;
            public Task<string> ReadDirectoryAsync(WindowIdentity identity, CancellationToken token) { started = true; return Read.Task; }
            public Task<CreatedExplorer> CreateAsync(string directory, WindowIdentity? source, IReadOnlyCollection<long> attached, CancellationToken token) => Task.FromException<CreatedExplorer>(new InvalidOperationException("unused fake creation"));
        }

        private static void PersistentTabMenuTests(string directory)
        {
            var api = new FakeWindows(); var host = new HostController(api, new FakeStore()); var first = Add(host, 1, HostingMode.Grouped); Add(host, 2, HostingMode.Grouped); host.Activate(first, Bounds);
            using (var form = WorkspaceForm(host, new WorkspaceService(), directory, new WorkspaceStore(), new AppSettings { Mode = HostingMode.Grouped }))
            {
                UiCall(form, "RefreshTabs"); var button = TabControls(UiField<FlowLayoutPanel>(form, "tabs"))[0]; var menu = button.ContextMenuStrip!;
                for (int i = 0; i < 3; i++)
                {
                    UiAnyCall(menu, "OnOpening", new System.ComponentModel.CancelEventArgs()); api.Events.Clear();
                    UiAnyCall(menu, "OnClosed", new ToolStripDropDownClosedEventArgs(ToolStripDropDownCloseReason.AppClicked));
                    Check(!menu.IsDisposed && UiField<int>(form, "modalDepth") == 1 && api.Events.Count == 0,
                        "persistent tab menu Closed defers scope cleanup and grouped restoration: " + i);
                    Application.DoEvents();
                    Check(!menu.IsDisposed && button.ContextMenuStrip == menu && UiField<int>(form, "modalDepth") == 0,
                        "next UI turn releases tab menu scope without disposing reusable dropdown: " + i);
                }
                host.MoveTab(first, 1); host.RenameTab(first, "Changed"); UiCall(form, "RefreshTabs");
                UiAnyCall(menu, "OnOpening", new System.ComponentModel.CancelEventArgs());
                Check(!menu.Items.Cast<ToolStripItem>().Single(x => x.Text == "分离右侧标签").Enabled && menu.Items.Cast<ToolStripItem>().Single(x => x.Text == "恢复默认名称").Enabled,
                    "reused tab menu re-evaluates live order and alias on every Opening");
                UiAnyCall(menu, "OnClosed", new ToolStripDropDownClosedEventArgs(ToolStripDropDownCloseReason.AppClicked));
                // Reopening before the queued cleanup must reuse its scope, not restore a popup-covered window.
                api.Events.Clear(); UiAnyCall(menu, "OnOpening", new System.ComponentModel.CancelEventArgs()); Application.DoEvents();
                Check(UiField<int>(form, "modalDepth") == 1 && !api.Events.Any(x => x.StartsWith("show:")),
                    "rapid tab-menu reopen does not double modal depth or restore Explorer underneath popup");
                UiAnyCall(menu, "OnClosed", new ToolStripDropDownClosedEventArgs(ToolStripDropDownCloseReason.CloseCalled)); Application.DoEvents();
            }
        }
    }
}
