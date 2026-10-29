using System.Runtime.InteropServices;
using System;
using System.Collections.Generic;
using System.Drawing;
using System.Diagnostics;
using System.Reflection;
using System.Threading;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Windows.Forms;
using ExplorerTabs.Core;

namespace ExplorerTabs.Tests
{
    internal static partial class Program
    {
        private static int count;
        [STAThread]
        private static int Main(string[] args)
        {
            try
            {
                if (args.Length == 2 && args[0] == "--identity-probe") return ProbeIdentityMutex(args[1]);
                if (args.Length == 3 && args[0] == "--native-helper") { NativeHelper(args[1], args[2]); return 0; }
                if (args.Length == 3 && args[0] == "--native-helper-unaware") { NativeHelper(args[1], args[2], true); return 0; }
                if ((args.Length == 2 || args.Length == 3) && args[0] == "--render-ui")
                { RenderUi(args[1], args.Length == 3 ? int.Parse(args[2]) : (int?)null); return 0; }
                if (args.Length >= 2 && args[0] == "--render-tabs")
                { RenderUi(args[1], args.Length == 3 ? int.Parse(args[2]) : (int?)null, true); return 0; }
                if (args.Length >= 2 && args[0] == "--render-tabs-hover")
                { RenderUi(args[1], args.Length == 3 ? int.Parse(args[2]) : (int?)null, true, true); return 0; }
                if (args.Length == 2 && args[0] == "--render-rename") { RenderRename(args[1]); return 0; }
                if (args.Length == 2 && args[0] == "--render-about") { RenderAbout(args[1]); return 0; }
                if (args.Length == 2 && args[0] == "--render-settings") { RenderSettings(args[1]); return 0; }
                if (args.Length >= 2 && args[0] == "--render-workspace") { RenderWorkspace(args[1], args.Length == 3 ? int.Parse(args[2]) : 1100); return 0; }
                if (args.Length == 2 && args[0] == "--render-picker") { RenderPicker(args[1]); return 0; }
                if (args.Length == 2 && args[0] == "--render-settings-keys") { RenderSettingsKeys(args[1]); return 0; }
                if (args.Contains("--workspace-ui-smoke")) { WorkspaceUiSmoke(); ChromeUiSmoke(); }
                else if (args.Contains("--creation-ui-smoke")) CreationUiSmoke();
                else if (args.Contains("--chrome-ui-smoke")) ChromeUiSmoke();
                else if (args.Contains("--tab-ui-smoke")) TabUiSmoke();
                else if (args.Contains("--cross-process-smoke")) { CrossProcessSmoke(); CrossProcessUnawareSmoke(); }
                else if (args.Contains("--native-smoke")) NativeSmoke();
                else { UnitTests(); DpiPolicyTests(); FeatureUnitTests(); DeferredCreationTests(); ShellInteropTests(); WorkspaceUnitTests(); BrandingUnitTests(); }
                Console.WriteLine($"PASS: {count} checks. No existing Explorer window was modified.");
                return 0;
            }
            catch (Exception error) { Console.Error.WriteLine(error); return 1; }
        }

        private static void Check(bool condition, string label)
        {
            if (!condition) throw new Exception("FAIL: " + label);
            count++; Console.WriteLine("PASS " + label);
        }
        private static void PumpFor(int milliseconds)
        {
            var watch = Stopwatch.StartNew();
            while (watch.ElapsedMilliseconds < milliseconds) { Application.DoEvents(); Thread.Sleep(10); }
        }
        private static bool PumpUntil(Func<bool> condition, int timeoutMs)
        {
            var watch = Stopwatch.StartNew();
            while (!condition() && watch.ElapsedMilliseconds < timeoutMs) { Application.DoEvents(); Thread.Sleep(10); }
            return condition();
        }
        private static Exception Throws(Action action)
        {
            try { action(); }
            catch (Exception error) { return error; }
            throw new Exception("Expected an exception.");
        }
        private static WindowIdentity Identity(long handle) => new WindowIdentity
        {
            Handle = handle, ProcessId = 123, ThreadId = 456, ProcessStartedUtcTicks = 789,
            ClassName = "CabinetWClass", Title = "Test " + handle,
            ImagePath = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.Windows), "explorer.exe")
        };
        private static readonly Rectangle Bounds = new Rectangle(0, 0, 640, 480);
        private static WindowSession Add(HostController host, long handle, HostingMode mode = HostingMode.Embedded) =>
            host.Attach(Identity(handle), new IntPtr(100), mode, Bounds);

        private static void DpiPolicyTests()
        {
            var allow = typeof(NativeWindowApi).GetMethod("AllowDpiMismatch", BindingFlags.NonPublic | BindingFlags.Static)
                ?? throw new Exception("AllowDpiMismatch missing.");
            bool Allow(int target, int host, uint targetDpi, uint hostDpi) =>
                (bool)allow.Invoke(null, new object[] { new IntPtr(target), new IntPtr(host), targetDpi, hostDpi })!;
            Check(Allow(-1, -4, 96, 96), "Unaware -> PerMonitorV2 allowed at equal effective DPI");
            Check(!Allow(-1, -4, 96, 144), "Unaware -> PerMonitorV2 rejected at different effective DPI");
            Check(!Allow(-4, -1, 96, 96), "PerMonitorV2 -> Unaware never allowed");
            Check(Allow(-2, -4, 120, 120), "System -> PerMonitorV2 allowed at equal effective DPI");
            Check(Allow(-5, -3, 96, 96), "UnawareGdiScaled -> PerMonitor allowed at equal effective DPI");
            Check(Allow(-1, -1, 0, 0), "identical contexts are accepted before DPI comparison");
            Check(!Allow(0, -4, 96, 96), "unreadable target context is rejected");
            Check(!Allow(-1, -4, 0, 0), "zero effective DPI is rejected");

            var preferred = typeof(NativeWindowApi).GetMethod("PreferredThreadDpiContext", BindingFlags.NonPublic | BindingFlags.Static)
                ?? throw new Exception("PreferredThreadDpiContext missing.");
            IntPtr Preferred(params int[] values) =>
                (IntPtr)preferred.Invoke(null, new object[] { values.Select(v => new IntPtr(v)).ToArray() })!;
            Check(Preferred(-1, -2) == IntPtr.Zero, "no per-monitor candidate keeps the manifest context");
            Check(Preferred(-1, -3) == new IntPtr(-3), "PerMonitor V1 is used only when no V2 candidate exists");
            Check(Preferred(-1, -3, -4) == new IntPtr(-4), "PerMonitor V2 is preferred over V1");
            Check(Preferred(-4, -3) == new IntPtr(-4), "PerMonitor V2 wins regardless of enumeration order");
        }

        private static void UnitTests()
        {
            var api = new FakeWindows(); var store = new FakeStore();
            var host = new HostController(api, store);
            var first = Add(host, 1); var second = Add(host, 2);
            Check(host.Sessions.Count == 2 && store.Snapshots.Count == 2, "attach persists both snapshots");
            Check(api.Events.Contains("attach:1") && host.Active == null, "attach does not silently change active tab");
            api.Events.Clear();
            host.Activate(first, Bounds); api.Events.Clear();
            host.Activate(second, Bounds);
            Check(api.Events.SequenceEqual(new[] { "layout:2", "show:2", "hide:1" }), "switch shows new before hiding old; no reparent/navigation");
            Check(host.Active == second, "switch commits active tab");
            Check(Throws(() => Add(host, 1)).Message.Contains("已经"), "duplicate handle rejected");
            Check(Throws(() => Add(host, 3, HostingMode.Grouped)).Message.Contains("模式"), "mixed hosting modes rejected");
            host.Detach(first);
            Check(host.Sessions.Count == 1 && store.Snapshots.Single().Identity.Handle == 2, "detach cleans recovery snapshot");
            host.Detach(second);
            Check(host.Active == null && host.Sessions.Count == 0, "active detach clears active state");

            api = new FakeWindows(); store = new FakeStore { Fail = true }; host = new HostController(api, store);
            Throws(() => Add(host, 1));
            Check(!api.Events.Any(e => e.StartsWith("attach")) && host.Sessions.Count == 0, "journal failure BEFORE attach causes zero mutations");

            api = new FakeWindows { FailValidate = true }; store = new FakeStore(); host = new HostController(api, store);
            Throws(() => Add(host, 1));
            Check(store.Writes == 0 && !api.Events.Any(e => e.StartsWith("attach")), "validation failure prevents persistence and mutations");

            api = new FakeWindows { FailAttach = true }; store = new FakeStore(); host = new HostController(api, store);
            Throws(() => Add(host, 1));
            Check(api.Events.Contains("restore:1") && store.Snapshots.Count == 0 && host.Sessions.Count == 0, "partial attach failure rolls back and cleans journal");

            api = new FakeWindows { FailAttach = true, FailRestore = true }; store = new FakeStore(); host = new HostController(api, store);
            Check(Throws(() => Add(host, 1)) is AggregateException, "attach AND rollback failure reported together");
            Check(host.Sessions.Single().State == SessionState.RecoveryRequired && store.Snapshots.Count == 1, "failed rollback remains recoverable in memory and on disk");
            Check(Throws(() => Add(host, 2)).Message.Contains("恢复"), "unresolved recovery blocks new attach");
            api.FailRestore = false;
            Check(host.DetachAll().Count == 0 && host.Sessions.Count == 0, "rollback can be retried successfully");

            api = new FakeWindows(); store = new FakeStore(); host = new HostController(api, store);
            first = Add(host, 1); second = Add(host, 2); host.Activate(first, Bounds);
            api.FailHideHandle = 1;
            Throws(() => host.Activate(second, Bounds));
            Check(host.Active == first && api.Visible.Contains(1) && !api.Visible.Contains(2), "failed switch restores prior visible tab");
            api.FailHideHandle = 0;

            store.Fail = true; api.Events.Clear();
            Throws(() => host.Detach(first));
            Check(first.State == SessionState.RestoredPendingJournal && host.Sessions.Contains(first), "successful restore + journal failure retains cleanup-only state");
            store.Fail = false; api.Events.Clear(); host.Detach(first);
            Check(!api.Events.Contains("restore:1") && !host.Sessions.Contains(first), "retry journal cleanup does NOT move the restored window again");

            api.Alive.Remove(2); api.Events.Clear();
            Check(host.PruneLost().Count == 0 && host.Sessions.Count == 0, "disappeared window removed from model and journal");
            Check(!api.Events.Any(e => e.StartsWith("restore:")), "lost / recycled HWND is never restored");

            api = new FakeWindows(); store = new FakeStore(); host = new HostController(api, store);
            first = Add(host, 1); second = Add(host, 2); host.Activate(first, Bounds);
            api.FailRestore = true;
            Check(host.DetachAll().Count == 2 && host.Sessions.Count == 2, "detach-all errors retain all recovery entries");
            api.Events.Clear(); host.LayoutActive(Bounds, true); host.SetActiveVisible(false);
            Check(api.Events.Count == 0, "partially restored active window is NOT laid out or hidden by monitoring");
            Check(Throws(() => host.Activate(first, Bounds)).Message.Contains("恢复"), "faulted tab cannot be activated");
            api.FailRestore = false; host.DetachAll();
            Check(store.Snapshots.Count == 0, "normal exit cleanup leaves empty journal");

            api = new FakeWindows(); store = new FakeStore(); host = new HostController(api, store);
            first = Add(host, 1); host.Activate(first, Bounds); api.ThrowOnMatch = true;
            Throws(() => host.PruneLost());
            Check(host.Sessions.Count == 1 && store.Snapshots.Count == 1, "UNKNOWN identity query never discards recovery state");
            Check(host.DetachAll().Count == 1, "UNKNOWN identity error returned by detach-all");
            Check(host.Sessions.Count == 1, "UNKNOWN identity query does not restore or remove window");
            api.ThrowOnMatch = false; host.DetachAll();

            api = new FakeWindows(); store = new FakeStore(); host = new HostController(api, store);
            first = Add(host, 1); second = Add(host, 2); host.Activate(first, Bounds);
            api.FailShowHandle = 2;
            Throws(() => host.Activate(second, Bounds));
            Check(host.Active == first && api.Visible.Contains(1) && !api.Visible.Contains(2), "failure showing next tab preserves prior tab");
            api.FailShowHandle = 0; host.DetachAll();

            var cabinet = Identity(1);
            Check(NativeWindowApi.IsExplorer(cabinet), "explorer.exe-hosted cabinet window is a genuine Shell browser");
            cabinet.ImagePath = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.System), "rundll32.exe");
            Check(NativeWindowApi.IsExplorer(cabinet), "registered rundll32 ShellBrowserWindow host is accepted");
            cabinet.ImagePath = Path.Combine(Path.GetTempPath(), "rundll32.exe");
            Check(!NativeWindowApi.IsExplorer(cabinet), "rundll32 outside the Windows directory is rejected");
            cabinet.ImagePath = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.System), "rundll32.exe");
            cabinet.ClassName = "NotExplorer";
            Check(!NativeWindowApi.IsExplorer(cabinet), "non-cabinet window is never treated as a Shell browser");

            ShortcutTests();
            JournalTests();
            RestorePresentationTests();
            DiagnosticArchiveTests();
            PresentationSnapshotTests();
            TabManagementTests();
        }

        private static void TabManagementTests()
        {
            var api = new FakeWindows(); var store = new FakeStore(); var host = new HostController(api, store);
            var first = Add(host, 1); var second = Add(host, 2); var third = Add(host, 3); var fourth = Add(host, 4);
            host.Activate(second, Bounds);
            var snapshots = store.Snapshots.ToArray(); int writes = store.Writes; api.Events.Clear(); store.Fail = true;
            host.MoveTab(second, 3);
            Check(host.Sessions.SequenceEqual(new[] { first, third, fourth, second }), "moving a tab right uses final index after removal");
            Check(host.Active == second && api.Events.Count == 0 && store.Writes == writes,
                "reordering active tab leaves all native windows, focus and recovery writes untouched");
            Check(host.Adjacent(1) == first && host.Adjacent(-1) == fourth, "Ctrl+Tab neighbours follow reordered tab list");
            host.MoveTab(second, 0);
            Check(host.Sessions.SequenceEqual(new[] { second, first, third, fourth }), "moving a tab left uses final index");
            host.MoveTab(second, 0);
            Check(api.Events.Count == 0 && store.Writes == writes, "same-position reorder is a no-op even if journal is unavailable");
            Check(Throws(() => host.MoveTab(second, -1)) is ArgumentOutOfRangeException &&
                Throws(() => host.MoveTab(second, 4)) is ArgumentOutOfRangeException,
                "out-of-range tab moves are rejected before modifying list");
            var foreignHost = new HostController(new FakeWindows(), new FakeStore()); var foreign = Add(foreignHost, 1);
            Check(Throws(() => host.MoveTab(foreign, 0)) is InvalidOperationException, "foreign-host tab cannot be reordered");
            host.RenameTab(second, "  常用目录 & 项目资料  ");
            Check(second.DisplayTitle == "常用目录 & 项目资料" && second.CustomTitle == second.DisplayTitle,
                "host-only tab alias trims whitespace and preserves Unicode and ampersands");
            second.Title = "Explorer 导航后的目录";
            Check(second.DisplayTitle == "常用目录 & 项目资料", "navigation title updates do not overwrite custom tab alias");
            Check(second.Identity.Title == "Test 2" && snapshots[1].Identity.Title == "Test 2" &&
                store.Snapshots.SequenceEqual(snapshots) && store.Writes == writes && api.Events.Count == 0,
                "renaming never modifies Explorer title, recovery identity, snapshots or native windows");
            Check(Throws(() => host.RenameTab(second, " ")) is ArgumentException &&
                Throws(() => host.RenameTab(second, "bad\nname")) is ArgumentException &&
                Throws(() => host.RenameTab(second, new string('x', 121))) is ArgumentException,
                "blank, control-character and overlong names are rejected");
            Check(second.DisplayTitle == "常用目录 & 项目资料", "failed rename preserves existing custom alias");
            host.RenameTab(second, new string('界', 120));
            Check(second.DisplayTitle.Length == 120, "maximum 120-character custom name is accepted");
            host.ResetTabTitle(second);
            Check(second.CustomTitle == null && second.DisplayTitle == second.Title, "reset alias immediately returns to latest native directory title");
            Check(Throws(() => host.RenameTab(foreign, "wrong")) is InvalidOperationException &&
                Throws(() => host.ResetTabTitle(foreign)) is InvalidOperationException,
                "rename and reset refuse foreign-host tabs");
            Check(host.Active == second && api.Events.Count == 0 && store.Writes == writes, "metadata operations preserve active tab with no native or journal mutations");
            Check(TabManagementPolicy.Select(host.Sessions, first, TabDetachScope.Current).SequenceEqual(new[] { first }), "current detach scope selects anchor only");
            Check(TabManagementPolicy.Select(host.Sessions, first, TabDetachScope.Others).SequenceEqual(new[] { second, third, fourth }), "other-tabs scope excludes right-clicked anchor, not active tab");
            Check(TabManagementPolicy.Select(host.Sessions, first, TabDetachScope.ToRight).SequenceEqual(new[] { third, fourth }), "right-side scope follows visible reordered tab order");
            Check(TabManagementPolicy.Select(host.Sessions, fourth, TabDetachScope.ToRight).Count == 0, "rightmost tab has no right-side detach targets");
            Check(TabManagementPolicy.Select(host.Sessions, first, TabDetachScope.All).SequenceEqual(host.Sessions), "all scope snapshots the complete ordered list");
            Check(Throws(() => TabManagementPolicy.Select(host.Sessions, foreign, TabDetachScope.Others)) is InvalidOperationException &&
                Throws(() => TabManagementPolicy.Select(host.Sessions, first, (TabDetachScope)99)) is ArgumentOutOfRangeException,
                "invalid anchor and unknown detach scopes are rejected");
            Check(TabManagementPolicy.Successor(host.Sessions, first, new[] { first }) == third,
                "detaching active tab prefers next neighbour, not first tab");
            Check(TabManagementPolicy.Successor(host.Sessions, fourth, new[] { fourth }) == third,
                "detaching last active tab prefers nearest left neighbour");
            Check(TabManagementPolicy.Successor(host.Sessions, second, new[] { first, third }) == second,
                "detaching background tabs preserves active tab");
            Check(TabManagementPolicy.Successor(host.Sessions, first, host.Sessions) == null,
                "detaching all leaves no preferred active tab");
            Check(TabManagementPolicy.CloseSuccessor(host.Sessions, first, first) == second,
                "closing an active tab focuses its left neighbour");
            Check(TabManagementPolicy.CloseSuccessor(host.Sessions, second, second) == first,
                "closing the leftmost active tab falls back to the right neighbour");
            Check(TabManagementPolicy.CloseSuccessor(host.Sessions, third, first) == third,
                "closing a background tab keeps the current active tab");
            Check(TabManagementPolicy.CloseSuccessor(new[] { first }, first, first) == null,
                "closing the only tab leaves no successor");
            Check(TabManagementPolicy.CloseSuccessor(host.Sessions, first, first, new[] { third }) == third,
                "close successor skips windows that disappeared in the same poll");
            store.Fail = false;
            Check(host.DetachTabs(first, TabDetachScope.ToRight).Count == 0 && host.Sessions.SequenceEqual(new[] { second, first }),
                "batch right-side detach restores only selected windows");
            Check(api.Events.SequenceEqual(new[] { "restore:3", "restore:4" }) && host.Active == second,
                "batch detach uses existing restore semantics and preserves unrelated active tab");
            api.Events.Clear(); writes = store.Writes;
            Check(host.DetachTabs(first, TabDetachScope.ToRight).Count == 0 && store.Writes == writes && api.Events.Count == 0,
                "empty batch detach does not touch windows or recovery journal");
            host.DetachAll(); foreignHost.DetachAll();

            api = new FakeWindows { FailRestoreHandle = 2 }; store = new FakeStore(); host = new HostController(api, store);
            first = Add(host, 1); second = Add(host, 2); third = Add(host, 3); fourth = Add(host, 4);
            host.Activate(first, Bounds); api.Events.Clear();
            var errors = host.DetachTabs(first, TabDetachScope.Others);
            Check(errors.Count == 1 && host.Sessions.SequenceEqual(new[] { first, second }) &&
                second.State == SessionState.RecoveryRequired && store.Snapshots.Count == 2,
                "batch partial restore failure retains failed record and continues other targets");
            Check(api.Events.SequenceEqual(new[] { "restore:2", "restore:3", "restore:4" }) && host.Active == first,
                "batch failure never restores or removes protected anchor");
            api.FailRestoreHandle = 0;
            Check(host.DetachTabs(first, TabDetachScope.Others).Count == 0 && host.Sessions.Single() == first,
                "failed batch restoration can be retried safely");
            Check(Throws(() => host.MoveTab(second, 0)) is InvalidOperationException, "detached session cannot be reordered into live host");
            host.DetachAll();

            api = new FakeWindows(); store = new FakeStore(); host = new HostController(api, store);
            first = Add(host, 1); second = Add(host, 2); third = Add(host, 3);
            store.Fail = true; api.Events.Clear();
            Check(host.DetachTabs(first, TabDetachScope.Others).Count == 2 && second.State == SessionState.RestoredPendingJournal &&
                third.State == SessionState.RestoredPendingJournal, "batch journal cleanup failure retains restored targets for cleanup-only retry");
            store.Fail = false; api.Events.Clear();
            Check(host.DetachTabs(first, TabDetachScope.Others).Count == 0 && api.Events.Count == 0 && host.Sessions.Single() == first,
                "batch cleanup-only retry never moves already restored windows again");
            second = Add(host, 2); api.Alive.Remove(2); api.Events.Clear();
            Check(host.DetachTabs(first, TabDetachScope.Others).Count == 0 && api.Events.Count == 0,
                "lost target during batch is removed without restoring recycled HWND");
            third = Add(host, 3); api.FailRestoreHandle = 3; Throws(() => host.Detach(third));
            Check(TabManagementPolicy.Successor(host.Sessions, first, new[] { first }) == null,
                "adjacent successor skips unresolved recovery tabs");
            Check(TabManagementPolicy.CloseSuccessor(host.Sessions, first, first) == null,
                "close successor skips unresolved recovery tabs");
            host.MoveTab(third, 0);
            Check(third.State == SessionState.RecoveryRequired && store.Snapshots.Contains(third.Snapshot),
                "sorting unresolved tab preserves its state and recovery snapshot");
            api.FailRestoreHandle = 0; host.DetachAll();

            Check(!TabManagementPolicy.StartsDrag(new Point(10, 10), new Point(11, 11), new Size(8, 8)), "small click movement does not initiate drag");
            Check(TabManagementPolicy.StartsDrag(new Point(10, 10), new Point(30, 10), new Size(8, 8)), "horizontal threshold movement starts sorting gesture");
            Check(TabManagementPolicy.StartsDrag(new Point(int.MinValue, 0), new Point(int.MaxValue, 0), new Size(8, 8)), "drag threshold arithmetic handles large signed coordinates");
            Check(TabManagementPolicy.InsertionIndex(new[] { 100, 300, 500 }, 10) == 0 &&
                TabManagementPolicy.InsertionIndex(new[] { 100, 300, 500 }, 301) == 2 &&
                TabManagementPolicy.InsertionIndex(new[] { 100, 300, 500 }, 900) == 3,
                "drop slots exclude dragged tab and cover both edges and middle");
            Check(TabManagementPolicy.InsertionIndex(Array.Empty<int>(), 100) == 0, "single-tab sorting has exactly one slot");
        }

        private static void PresentationSnapshotTests()
        {
            string directory = Path.Combine(Path.GetTempPath(), "ExplorerTabs-monitor-" + Guid.NewGuid().ToString("N"));
            var log = new AppLog(Path.Combine(directory, "host.log"));
            int discoveries = 0, queries = 0;
            bool failDiscovery = false, failQuery = false;
            var candidates = new List<WindowIdentity> { Identity(1), Identity(2) };
            Func<IReadOnlyList<WindowIdentity>> discover = () =>
            {
                discoveries++;
                if (failDiscovery) throw new IOException("Injected discovery failure");
                return candidates;
            };
            Func<WindowIdentity, bool, string> capture = (identity, children) =>
            {
                queries++;
                if (failQuery) throw new IOException("Injected query failure");
                return "fake-state=" + identity.Handle + " children=" + children;
            };
            var type = Assembly.Load("ExplorerTabs").GetType("ExplorerTabs.App.PresentationMonitor", true)!;
            var monitor = (IDisposable)Activator.CreateInstance(type, new object[] { log, discover, capture })!;
            string Snapshot(bool standalone) => (string)type.GetMethod("Snapshot")!.Invoke(monitor, new object[] { "test", standalone })!;
            try
            {
                string text = Snapshot(false);
                Check(discoveries == 0 && queries == 0, "empty closing snapshot does not discover or query unrelated windows");
                type.GetMethod("Track")!.Invoke(monitor, new object[] { Identity(1), "detach" });
                text = Snapshot(false);
                Check(discoveries == 0 && queries == 1 && text.Contains("operation=detach") && text.Contains("fake-state=1 children=True"),
                    "tracked diagnostic snapshot captures child state without standalone discovery");
                queries = 0;
                text = Snapshot(true);
                Check(discoveries == 1 && queries == 2 && text.Contains("operation=standalone-read-only title=Test 2"),
                    "explicit export includes standalone windows without querying tracked HWND twice");
                Check(text.Contains("actual rendered pixels are not verified"), "snapshot does not assert taskbar or pixel-level success");
                failDiscovery = true;
                text = Snapshot(true);
                Check(text.Contains("standalone-discovery=unknown") && text.Contains("fake-state=1"),
                    "discovery failure preserves tracked diagnostics and does not throw");
                failDiscovery = false; failQuery = true;
                text = Snapshot(false);
                Check(text.Contains("identity/query=unknown") && text.Contains("Injected query failure"),
                    "failed per-window query remains unknown rather than claiming lost window");
                failQuery = false; candidates = Enumerable.Range(2, 120).Select(i => Identity(i)).ToList(); queries = 0;
                text = Snapshot(true);
                Check(queries == 101, "explicit standalone export bounds child-surface collection to 100 additional roots");
                Check(File.ReadAllText(log.FilePath).Contains("Presentation snapshot"), "snapshot is retained in local log for post-exit diagnosis");
            }
            finally
            {
                monitor.Dispose();
                if (File.Exists(log.FilePath)) File.Delete(log.FilePath);
                if (Directory.Exists(directory)) Directory.Delete(directory);
            }
        }

        private static void RestorePresentationTests()
        {
            Check(RestorePresentation.HiddenStyle(0x96CF0000L) == 0x86CF0000L, "saved WS_VISIBLE is cleared during structural restoration");
            Check(RestorePresentation.HiddenStyle(0x86CF0000L) == 0x86CF0000L, "hidden style remains unchanged");
            var saved = new WindowPlacement
            {
                Length = 44, Flags = 2, ShowCommand = 1,
                NormalPosition = new NativeRect { Left = -1000, Top = 20, Right = -200, Bottom = 620 },
                MinimumPosition = new NativePoint { X = -1, Y = -1 }, MaximumPosition = new NativePoint { X = 3, Y = 4 }
            };
            var restored = RestorePresentation.Placement(saved, true);
            Check(restored.ShowCommand == 4, "normal placement uses SW_SHOWNOACTIVATE");
            Check(restored.Flags == saved.Flags && restored.Length == saved.Length && restored.NormalPosition.Left == -1000 &&
                restored.NormalPosition.Right == -200 && restored.MaximumPosition.X == 3 && restored.MinimumPosition.Y == -1,
                "restore presentation preserves placement metadata and negative workspace coordinates");
            Check(saved.ShowCommand == 1, "presentation does not mutate saved recovery snapshot");
            Check(RestorePresentation.Placement(saved, false).ShowCommand == 0, "originally hidden window uses SW_HIDE placement");
            saved.ShowCommand = 3;
            Check(RestorePresentation.Placement(saved, true).ShowCommand == 3, "maximised placement is not flattened to normal");
            saved.ShowCommand = 2;
            Check(RestorePresentation.Placement(saved, true).ShowCommand == 7, "minimised placement uses SW_SHOWMINNOACTIVE");
            saved.ShowCommand = 7;
            Check(RestorePresentation.Placement(saved, true).ShowCommand == 7, "already non-activating minimised placement stays minimised");
        }

        private static void DiagnosticArchiveTests()
        {
            string directory = Path.Combine(Path.GetTempPath(), "ExplorerTabs-diagnostics-tests-" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(directory);
            try
            {
                File.WriteAllText(Path.Combine(directory, "host.log"), "test log 中文");
                File.WriteAllText(Path.Combine(directory, "recovery.json"), "recovery sentinel");
                File.WriteAllText(Path.Combine(directory, "do-not-copy.txt"), "private content");
                string output = Path.Combine(directory, "diagnostics.zip");
                DiagnosticArchive.Create(output, directory, "window state 中文");
                using (var input = File.OpenRead(output))
                using (var zip = new ZipArchive(input, ZipArchiveMode.Read))
                {
                    Check(zip.Entries.Count == 4 && zip.GetEntry("do-not-copy.txt") == null,
                        "diagnostic ZIP includes only allowlisted files and generated state");
                    using (var reader = new StreamReader(zip.GetEntry("window-state.txt")!.Open()))
                        Check(reader.ReadToEnd() == "window state 中文", "diagnostic state preserves Unicode");
                    using (var reader = new StreamReader(zip.GetEntry("host.log")!.Open()))
                        Check(reader.ReadToEnd() == "test log 中文", "diagnostic ZIP preserves original log content");
                    using (var reader = new StreamReader(zip.GetEntry("collection-info.txt")!.Open()))
                        Check(reader.ReadToEnd().Contains("No automatic upload"), "diagnostic ZIP explains privacy and collection scope");
                }
                Check(File.ReadAllText(Path.Combine(directory, "recovery.json")) == "recovery sentinel", "export never rewrites recovery journal");
                byte[] before = File.ReadAllBytes(output);
                Check(Throws(() => DiagnosticArchive.Create(output, directory, "replacement")) is IOException,
                    "export refuses to overwrite existing destination");
                Check(File.ReadAllBytes(output).SequenceEqual(before), "rejected export preserves existing destination bytes");
                Check(Directory.GetFiles(directory, "*.tmp").Length == 0, "failed diagnostic export cleans only its temporary file");
                DiagnosticArchive.Create(Path.Combine(directory, "empty-state.zip"), Path.Combine(directory, "missing-state"), "empty");
                Check(File.Exists(Path.Combine(directory, "empty-state.zip")), "export tolerates missing optional log and journal files");
            }
            finally
            {
                foreach (string file in Directory.GetFiles(directory)) File.Delete(file);
                Directory.Delete(directory);
            }
        }

        private static void ShortcutTests()
        {
            var policy = new TabShortcutPolicy();
            Check(!policy.Handle(65, true, false, true, false, false, true).Suppress, "ordinary Explorer keys pass through");
            Check(!policy.Handle(9, true, false, false, false, false, true).Suppress, "plain Tab is untouched");
            var decision = policy.Handle(9, true, false, true, false, false, true);
            Check(!decision.Suppress && decision.Direction == 0, "pressing Ctrl while plain Tab is held cannot start a shortcut");
            Check(!policy.Handle(9, false, false, true, false, false, true).Suppress, "plain Tab key-up is untouched");
            Check(!policy.Handle(9, true, false, true, false, true, true).Suppress, "Ctrl+Alt+Tab is untouched");
            policy.Handle(9, false, false, true, false, true, true);
            Check(!policy.Handle(9, true, false, true, false, false, false).Suppress, "Ctrl+Tab outside active host is untouched");
            decision = policy.Handle(9, true, false, true, false, false, true);
            Check(!decision.Suppress && decision.Direction == 0, "foreground change while outside Tab is held cannot start a shortcut");
            Check(!policy.Handle(9, false, false, true, false, false, true).Suppress, "outside chord key-up remains untouched");
            Check(!policy.Handle(9, true, true, true, false, false, true).Suppress, "injected input is untouched");
            decision = policy.Handle(9, true, false, true, false, false, true);
            Check(decision.Suppress && decision.Direction == 1, "Ctrl+Tab queues forward switch");
            decision = policy.Handle(9, true, false, true, false, false, true);
            Check(decision.Suppress && decision.Direction == 0, "held Tab repeat is consumed without queue flooding");
            decision = policy.Handle(9, false, false, false, false, false, false);
            Check(decision.Suppress && decision.Direction == 0, "matching key-up consumed even after Ctrl released or foreground changed");
            Check(!policy.Handle(9, false, false, true, false, false, true).Suppress, "unpaired key-up is untouched");
            decision = policy.Handle(9, true, false, true, true, false, true);
            Check(decision.Suppress && decision.Direction == -1, "Ctrl+Shift+Tab queues backward switch");
            policy.Handle(9, false, false, true, true, false, true);
            Check(policy.Handle(9, true, false, true, false, false, true).Direction == 1, "new physical chord works after key-up");
            Check(!policy.Handle(9, false, true, false, false, false, false).Suppress, "injected key-up does not consume a real held chord");
            Check(policy.Handle(9, true, false, true, false, false, true).Direction == 0, "injected key-up cannot reset real Tab repeat latch");
            Check(policy.Handle(9, false, false, false, false, false, false).Suppress, "physical key-up still completes chord after injected input");
            policy = new TabShortcutPolicy(tabInitiallyDown: true);
            decision = policy.Handle(9, true, false, true, false, false, true);
            Check(!decision.Suppress && decision.Direction == 0, "Tab already held at hook startup remains untouched");
            Check(!policy.Handle(9, false, false, true, false, false, true).Suppress, "startup-held Tab key-up remains untouched");
            Check(policy.Handle(9, true, false, true, false, false, true).Direction == 1, "fresh Tab after startup-held release switches normally");

            var api = new FakeWindows();
            var host = new HostController(api, new FakeStore());
            Check(host.Adjacent(1) == null, "no tabs yields no shortcut target");
            var first = Add(host, 1);
            Check(host.Adjacent(1) == null, "one tab leaves Explorer Ctrl+Tab behavior available");
            var second = Add(host, 2); var third = Add(host, 3);
            host.Activate(first, Bounds);
            Check(host.Adjacent(1) == second && host.Adjacent(-1) == third, "adjacent selection wraps both directions");
            host.Activate(third, Bounds);
            Check(host.Adjacent(1) == first, "last tab wraps to first");
            api.FailRestore = true;
            Throws(() => host.Detach(second));
            Check(host.Adjacent(-1) == first, "shortcut skips faulted tabs awaiting recovery");
            Check(Throws(() => host.Adjacent(0)) is ArgumentOutOfRangeException, "invalid shortcut direction rejected");
            api.FailRestore = false; host.DetachAll();
        }

        private static void JournalTests()
        {
            string directory = Path.Combine(Path.GetTempPath(), "ExplorerTabs-tests-" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(directory);
            try
            {
                var journal = new RecoveryJournal(Path.Combine(directory, "recovery.json"));
                Check(journal.Read() == null, "missing recovery journal is harmless");
                var snapshot = new WindowSnapshot
                {
                    Identity = Identity(1), HostHandle = 100, Mode = HostingMode.Embedded, Style = 0x10CF0000L,
                    ExtendedStyle = 0x40000, WasVisible = true,
                    Placement = new WindowPlacement
                    {
                        Length = 44, ShowCommand = 3,
                        NormalPosition = new NativeRect { Left = -1200, Top = 50, Right = -200, Bottom = 800 }
                    }
                };
                snapshot.Identity.Handle = 0x123456789L;
                journal.Save(new[] { snapshot });
                var read = journal.Read()!;
                Check(read.Windows.Single().Identity.Handle == 0x123456789L && read.Windows.Single().Placement.NormalPosition.Left == -1200 &&
                    read.Windows[0].Style == snapshot.Style && read.Windows[0].Identity.ImagePath == snapshot.Identity.ImagePath,
                    "JSON roundtrip preserves 64-bit handles, style and negative coordinates");
                Check(RecoveryJournal.OwnerIsRunning(read), "live owner blocks recovery");
                Check(Throws(() => journal.Recover(read, new FakeWindows())) is InvalidOperationException, "cannot restore from a live host's journal");
                snapshot.Identity.Handle = 1;
                read.Windows[0].Identity.Handle = 1;
                read.HostProcessId = int.MaxValue; read.HostStartedUtcTicks = 1;
                var api = new FakeWindows { FailRestore = true };
                Check(journal.Recover(read, api).Errors.Count == 1 && journal.Read()!.Windows.Count == 1, "failed recovery remains on disk");
                Check(!RecoveryJournal.OwnerIsRunning(journal.Read()!), "failed recovery retains DEAD original owner");
                api.FailRestore = false;
                Check(journal.Recover(journal.Read()!, api).Errors.Count == 0 && journal.Read()!.Windows.Count == 0, "stale recovery can be retried");
                journal.Save(new[] { snapshot });
                read = journal.Read()!; read.HostProcessId = int.MaxValue;
                api.Alive.Clear(); api.Events.Clear();
                var missing = journal.Recover(read, api);
                Check(missing.Restored.Count == 0 && missing.Missing.Count == 1 && missing.Errors.Count == 0,
                    "destroyed-window recovery reports zero restored rather than silent success");
                Check(missing.Summary.Contains("0 个原窗口已恢复") && missing.Summary.Contains("1 个原窗口已消失"),
                    "recovery result explicitly distinguishes missing windows");
                Check(!api.Events.Any(e => e.StartsWith("restore:")) && journal.Read()!.Windows.Count == 0, "stale journal never mutates lost window");
                journal.Save(new[] { snapshot });
                read = journal.Read()!; read.HostProcessId = int.MaxValue;
                api.Alive.Add(1); api.ThrowOnMatch = true;
                var unknown = journal.Recover(read, api);
                Check(unknown.Errors.Count == 1 && unknown.Missing.Count == 0 && journal.Read()!.Windows.Count == 1,
                    "recovery UNKNOWN query retains pending entry and reports error, not missing");
                api.ThrowOnMatch = false;
                var successful = journal.Recover(journal.Read()!, api);
                Check(successful.Restored.Count == 1 && successful.Missing.Count == 0 && successful.Errors.Count == 0,
                    "recovery reports actual restored count");
                File.WriteAllText(journal.FilePath, "not json");
                Check(Throws(() => journal.Read()) != null, "corrupt journal is rejected instead of overwritten");
                Check(Directory.GetFiles(directory, "*.tmp").Length == 0, "atomic writes leave no temporary files");
            }
            finally
            {
                // Only delete known files in this exact, newly created test directory; no recursive deletion.
                foreach (string file in Directory.GetFiles(directory)) File.Delete(file);
                Directory.Delete(directory);
            }
        }

        private static Control[] TabControls(FlowLayoutPanel strip) =>
            strip.Controls.Cast<Control>().Where(c => c.GetType().GetProperty("Session") != null).ToArray();

        private static void TabUiSmoke()
        {
            Console.WriteLine("Tab UI smoke creates only hidden managed controls with fake window APIs. No Show, global capture, hooks or Explorer manipulation.");
            var api = new FakeWindows(); var store = new FakeStore(); var host = new HostController(api, store);
            var first = Add(host, 1); var second = Add(host, 2); var third = Add(host, 3); var fourth = Add(host, 4);
            host.Activate(second, Bounds);
            host.RenameTab(first, "常用 & 项目目录");
            var type = Assembly.Load("ExplorerTabs").GetType("ExplorerTabs.App.MainForm", true)!;
            string directory = Path.Combine(Path.GetTempPath(), "ExplorerTabs-tab-ui-" + Guid.NewGuid().ToString("N"));
            var log = new AppLog(Path.Combine(directory, "host.log"));
            using (var form = (Form)Activator.CreateInstance(type, host, log)!)
            {
                var strip = (FlowLayoutPanel)type.GetField("tabs", BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(form)!;
                var stripType = strip.GetType();
                object? Call(string method, params object[] arguments) => stripType.GetMethod(method,
                    BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic)!.Invoke(strip, arguments);
                void Refresh() => type.GetMethod("RefreshTabs", BindingFlags.Instance | BindingFlags.NonPublic)!.Invoke(form, null);
                Button ButtonFor(WindowSession session) => TabControls(strip).Cast<Button>().Single(b =>
                    (WindowSession)b.GetType().GetProperty("Session")!.GetValue(b)! == session);
                int TabCount() => TabControls(strip).Length;
                bool Interacting() => (bool)stripType.GetProperty("IsInteracting")!.GetValue(strip)!;
                Refresh(); form.Width = form.MinimumSize.Width; MakeHidden(form);
                var tipMethod = stripType.GetMethod("TooltipFor", BindingFlags.Static | BindingFlags.NonPublic)!;
                var plainTip = (string)tipMethod.Invoke(null, new object?[] { first, null })!;
                Check(plainTip.Contains("目录：未缓存") && !plainTip.Contains("拖动排序"),
                    "tab tooltip shows the cached-directory line and no drag-sort instructions");
                var cache = (Dictionary<WindowSession, string>)type.GetField("directoryCache", BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(form)!;
                cache[first] = @"D:\Cached";
                Refresh();
                var tips = (ToolTip)stripType.GetField("tips", BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(strip)!;
                Check(tips.GetToolTip(ButtonFor(first)).Contains(@"目录：D:\Cached"),
                    "tab tooltip reads the cached Explorer directory");
                var closeButton = ButtonFor(second);
                closeButton.GetType().GetMethod("OnMouseEnter", BindingFlags.Instance | BindingFlags.NonPublic)!.Invoke(closeButton, new object[] { EventArgs.Empty });
                var hitClose = closeButton.GetType().GetMethod("HitClose", BindingFlags.Instance | BindingFlags.NonPublic)!;
                Check((bool)hitClose.Invoke(closeButton, new object[] { new Point(closeButton.Width - 10, closeButton.Height / 2) })!,
                    "hovering a tab exposes a close hit target at its right edge");
                Check(!(bool)hitClose.Invoke(closeButton, new object[] { new Point(10, closeButton.Height / 2) })!,
                    "the tab body itself is not the close hit target");
                closeButton.GetType().GetMethod("OnMouseDown", BindingFlags.Instance | BindingFlags.NonPublic)!
                    .Invoke(closeButton, new object[] { new MouseEventArgs(MouseButtons.Left, 1, closeButton.Width - 10, closeButton.Height / 2, 0) });
                Check(!Interacting(), "pressing the close glyph does not arm a tab drag/selection gesture");
                closeButton.GetType().GetMethod("OnClick", BindingFlags.Instance | BindingFlags.NonPublic)!
                    .Invoke(closeButton, new object[] { EventArgs.Empty });
                Check(host.Active == second, "pressing the close glyph never selects the tab");
                Check(!(bool)Call("HasHorizontalScrollbar")! && !(bool)Call("HasVerticalScrollbar")! &&
                    strip.Controls.Cast<Control>().All(c => c.Top >= 0 && c.Bottom <= strip.ClientSize.Height),
                    "short adaptive titles fit the minimum-width host without an unnecessary scrollbar");
                foreach (var session in host.Sessions) host.RenameTab(session, "用于检查完整标题溢出与滚动的很长的项目目录名称 · " + session.Identity.Handle);
                Refresh(); form.PerformLayout();
                Check((bool)Call("HasHorizontalScrollbar")! && !(bool)Call("HasVerticalScrollbar")! &&
                    strip.Controls.Cast<Control>().All(c => c.Bottom <= strip.ClientSize.Height),
                    "long-title overflow keeps the entire tab row visible with horizontal-only scrolling");
                Call("Reveal", fourth);
                Check(ButtonFor(fourth).Left >= 0 && ButtonFor(fourth).Right <= strip.ClientSize.Width,
                    "revealing the last tab scrolls it fully into the visible strip");
                int offset = ((Point)Call("ScrollPosition")!).X; Refresh();
                Check(((Point)Call("ScrollPosition")!).X == offset, "metadata redraw preserves horizontal scroll position");
                foreach (var session in host.Sessions)
                { if (session == first) host.RenameTab(session, "常用 & 项目目录"); else host.ResetTabTitle(session); }
                Refresh(); Call("ResetScrollPosition");
                var clickButton = ButtonFor(third);
                clickButton.GetType().GetMethod("OnClick", BindingFlags.Instance | BindingFlags.NonPublic)!.Invoke(clickButton, new object[] { EventArgs.Empty });
                Check(host.Active == second && !clickButton.IsDisposed, "tab selection is queued until the mouse message has completed");
                Application.DoEvents();
                Check(host.Active == third && clickButton.IsDisposed, "queued tab selection executes and safely rebuilds its controls");
                host.Activate(second, Bounds); Refresh(); Call("ResetScrollPosition");
                api.Events.Clear(); int writes = store.Writes;
                var button = ButtonFor(first);
                Check(TabCount() == 4 && button.Text == first.DisplayTitle && button.AccessibleName == first.DisplayTitle,
                    "actual tab controls display custom aliases and accessible names");
                Check(strip.Controls.Cast<Control>().Last().AccessibleName == "新建标签" &&
                    TabControls(strip).All(c => strip.Controls.GetChildIndex(c) < strip.Controls.GetChildIndex(strip.Controls.Cast<Control>().Last())),
                    "new-tab action stays directly after the last file tab");
                Check(!button.UseMnemonic && button.AutoEllipsis, "long titles are ellipsized and ampersands are not accelerator markers");
                Check(ButtonFor(second).BackColor != button.BackColor, "actual active tab uses selected background");
                Check(ButtonFor(first).ContextMenuStrip!.Items.Cast<ToolStripItem>().Single(i => i.Text == "分离右侧标签").Enabled &&
                    !ButtonFor(fourth).ContextMenuStrip!.Items.Cast<ToolStripItem>().Single(i => i.Text == "分离右侧标签").Enabled,
                    "last tab menu disables impossible right-detach action");
                Check(button.ContextMenuStrip.Items.Cast<ToolStripItem>().Single(i => i.Text == "恢复默认名称").Enabled &&
                    !ButtonFor(second).ContextMenuStrip!.Items.Cast<ToolStripItem>().Single(i => i.Text == "恢复默认名称").Enabled,
                    "reset-name menu is enabled only for custom aliases");
                Check(button.ContextMenuStrip.Items.Cast<ToolStripItem>().OfType<ToolStripMenuItem>().Any(i => i.Text.StartsWith("关闭") && i.ShortcutKeyDisplayString == "Ctrl+W") &&
                    !button.ContextMenuStrip.Items.Cast<ToolStripItem>().Any(i => i.Text.Contains("移动")),
                    "tab menu shows the close shortcut and no longer exposes move-left/right entries");
                Call("BeginPointer", button, MouseButtons.Right, new Point(20, 15));
                Check(!Interacting(), "right-click does not arm a sorting gesture");
                Call("BeginPointer", button, MouseButtons.Left, new Point(20, 15));
                Call("MovePointer", button, new Point(21, 15));
                Check(Interacting() && !(bool)Call("IsDragging", button)!, "pointer-down with small movement remains a click");
                Check(Call("EndPointer", button, MouseButtons.Left, new Point(21, 15)) == null && !Interacting(),
                    "ordinary click release never produces a reorder request");
                Call("BeginPointer", button, MouseButtons.Left, new Point(20, 15));
                var reorderPoint = new Point(ButtonFor(second).Left + ButtonFor(second).Width / 2 + 1, 15);
                Call("MovePointer", button, reorderPoint);
                Check((bool)Call("IsDragging", button)! && api.Events.Count == 0 && store.Writes == writes,
                    "sorting preview does not activate, reparent or restore any window");
                button.GetType().GetMethod("OnClick", BindingFlags.Instance | BindingFlags.NonPublic)!.Invoke(button, new object[] { EventArgs.Empty });
                Check(host.Active == second && api.Events.Count == 0, "dragging button suppresses Click even when WinForms fires it before MouseUp");
                var move = Call("EndPointer", button, MouseButtons.Left, reorderPoint);
                Check(move != null && !Interacting() && host.Sessions[0] == first, "drop finishes gesture without changing model before request is published");
                var oldMenu = button.ContextMenuStrip;
                Call("PublishMove", move!);
                Check(host.Sessions.SequenceEqual(new[] { second, first, third, fourth }) && host.Active == second,
                    "actual host receives drag request and reorders without switching active Explorer");
                Check(api.Events.Count == 0 && store.Writes == writes && button.IsDisposed && oldMenu!.IsDisposed,
                    "rebuilding reordered tab UI disposes old controls/menus without native mutations");
                button = ButtonFor(first);
                Call("BeginPointer", button, MouseButtons.Left, new Point(button.Left + 10, 15));
                Call("MovePointer", button, new Point(-100, 15));
                Check(Call("EndPointer", button, MouseButtons.Left, new Point(-100, 15)) == null && host.Sessions[1] == first,
                    "drop outside tab strip cancels without detaching or reordering");
                Call("BeginPointer", button, MouseButtons.Left, new Point(button.Left + 10, 15));
                Call("MovePointer", button, new Point(50, 15)); Call("CancelPointer");
                Check(!Interacting() && Call("EndPointer", button, MouseButtons.Left, new Point(50, 15)) == null,
                    "cancelled sorting cannot publish a later drop");
                button.ContextMenuStrip!.Items.Cast<ToolStripItem>().Single(i => i.Text == "恢复默认名称").PerformClick();
                Application.DoEvents(); // Menu commands run after the click/close stack, not inline.
                Check(first.CustomTitle == null && ButtonFor(first).Text == first.Title && api.Events.Count == 0,
                    "actual reset-name menu restores automatic directory label without touching Explorer");
                // Non-active right-click anchor must govern the batch; active tab is removed here.
                ButtonFor(first).ContextMenuStrip!.Items.Cast<ToolStripItem>().Single(i => i.Text == "分离其他标签").PerformClick();
                Application.DoEvents(); // Menu commands run after the click/close stack, not inline.
                Check(host.Sessions.Single() == first && host.Active == first && TabCount() == 1,
                    "actual other-tabs menu keeps its right-click anchor and selects it after active tab is detached");
                Check(api.Events.Contains("restore:2") && api.Events.Contains("restore:3") && api.Events.Contains("restore:4") &&
                    !api.Events.Contains("restore:1"), "batch UI routes selected targets through existing safe restoration, not close commands");
                api.Events.Clear();
                ButtonFor(first).ContextMenuStrip!.Items.Cast<ToolStripItem>().Single(i => i.Text == "分离全部标签").PerformClick();
                Application.DoEvents(); // Menu commands run after the click/close stack, not inline.
                Check(host.Sessions.Count == 0 && TabCount() == 0 && strip.Controls.Cast<Control>().Single().AccessibleName == "新建标签" &&
                    api.Events.SequenceEqual(new[] { "restore:1" }),
                    "actual all-tabs menu restores final window and leaves only the new-tab action");
            }
            var renameType = Assembly.Load("ExplorerTabs").GetType("ExplorerTabs.App.RenameTabDialog", true)!;
            using (var dialog = (Form)Activator.CreateInstance(renameType, "Original")!)
            {
                MakeHidden(dialog);
                Check(!dialog.ShowIcon && HasWindowRegion(dialog), "rename dialog removes the inherited title-bar icon and applies a small rounded region");
                var input = (TextBox)renameType.GetField("name", BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(dialog)!;
                var error = (Label)renameType.GetField("error", BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(dialog)!;
                Check(input.MaxLength == 120 && input.Text == "Original", "rename dialog pre-fills its alias and limits input length");
                input.Text = " "; ((Button)dialog.AcceptButton!).PerformClick();
                Check(dialog.DialogResult == DialogResult.None && error.Text.Length > 0,
                    "empty alias validation keeps the dialog open with an inline error");
                input.Text = "  常用 & 第二目录  "; ((Button)dialog.AcceptButton!).PerformClick();
                Check(dialog.DialogResult == DialogResult.OK && input.Text == "常用 & 第二目录",
                    "rename confirmation normalizes Unicode text without changing native windows");
            }
            using (var dialog = (Form)Activator.CreateInstance(renameType, "Unchanged")!)
            {
                MakeHidden(dialog); ((Button)dialog.CancelButton!).PerformClick();
                Check(dialog.DialogResult == DialogResult.Cancel && (string)renameType.GetProperty("TabTitle")!.GetValue(dialog)! == "Unchanged",
                    "rename cancellation returns without applying a new title");
            }
            // Dispose hidden controls before deleting only this test's own log directory.
            foreach (string file in Directory.Exists(directory) ? Directory.GetFiles(directory) : Array.Empty<string>()) File.Delete(file);
            if (Directory.Exists(directory)) Directory.Delete(directory);
        }

        private static void RenderRename(string output)
        {
            Application.EnableVisualStyles();
            var type = Assembly.Load("ExplorerTabs").GetType("ExplorerTabs.App.RenameTabDialog", true)!;
            using (var form = (Form)Activator.CreateInstance(type, "常用目录 · 项目资料")!) RenderHiddenForm(form, output);
            Console.WriteLine("Rendered rename dialog without showing it: " + output);
        }
        private static void RenderAbout(string output)
        {
            Application.EnableVisualStyles();
            var type = Assembly.Load("ExplorerTabs").GetType("ExplorerTabs.App.AboutDialog", true)!;
            using (var form = (Form)Activator.CreateInstance(type)!) RenderHiddenForm(form, output);
            Console.WriteLine("Rendered about dialog without showing it: " + output);
        }

        private static void RenderHiddenForm(Form form, string output)
        {
            MakeHidden(form);
            using (var bitmap = new Bitmap(form.Width, form.Height))
            {
                form.DrawToBitmap(bitmap, new Rectangle(0, 0, bitmap.Width, bitmap.Height));
                bitmap.Save(output, System.Drawing.Imaging.ImageFormat.Png);
            }
        }

        private static void NativeSmoke()
        {
            Console.WriteLine("Native smoke uses ONLY transparent windows created by this test process.");
            EmbeddedChromeNativeSmoke();
            var taskbar = new FakeTaskbar();
            var warnings = new List<string>();
            var trace = new List<string>();
            var api = new NativeWindowApi(taskbar, warnings.Add, trace.Add);
            using (var container = Synthetic("Host"))
            using (var firstWindow = Synthetic("First"))
            using (var secondWindow = Synthetic("Second"))
            {
                var hostHwnd = container.Handle;
                api.Show(api.Capture(NativeWindowApi.Inspect(hostHwnd)), true, false);
                var one = NativeWindowApi.Inspect(firstWindow.Handle);
                var two = NativeWindowApi.Inspect(secondWindow.Handle);
                Check(!NativeWindowApi.IsExplorer(one), "synthetic window cannot be offered by Explorer discovery");
                using (var process = Process.GetCurrentProcess())
                    Check(one.ProcessStartedUtcTicks == process.StartTime.ToUniversalTime().Ticks &&
                        string.Equals(one.ImagePath, process.MainModule!.FileName, StringComparison.OrdinalIgnoreCase),
                        "limited-query identity matches original Process.StartTime and MainModule snapshot format");
                var original = api.Capture(one);
                var host = new HostController(api, new FakeStore());
                try
                {
                    var first = host.Attach(one, hostHwnd, HostingMode.Embedded, Bounds);
                    var second = host.Attach(two, hostHwnd, HostingMode.Embedded, Bounds);
                    Check(api.Capture(one).Parent == hostHwnd.ToInt64(), "native SetParent mounts synthetic window");
                    Check(taskbar.Events.SequenceEqual(new[] { "remove:" + one.Handle, "remove:" + two.Handle }),
                        "embedded attach requests taskbar removal through fake adapter only");
                    host.Activate(first, Bounds); host.Activate(second, Bounds);
                    Check(!api.Capture(one).WasVisible && api.Capture(two).WasVisible, "native switch toggles child visibility");
                    host.LayoutActive(new Rectangle(0, 0, 800, 500));
                    var rect = NativeWindowApi.ReadRect(two.Hwnd);
                    Check(rect.Right - rect.Left == 800 && rect.Bottom - rect.Top == 500, "native embedded resize updates client window size");
                    host.DetachAll();
                    var restored = api.Capture(one);
                    Check(restored.Parent == original.Parent && restored.Style == original.Style &&
                        restored.ExtendedStyle == original.ExtendedStyle && restored.WasVisible == original.WasVisible,
                        "native detach restores parent, styles and visibility");
                    Check(host.Sessions.Count == 0, "native embedded cleanup completes");
                    Check(!taskbar.Events.Any(e => e.StartsWith("add:")), "originally hidden window is not spuriously added to taskbar");
                }
                finally
                {
                    var errors = host.DetachAll();
                    if (errors.Count > 0) throw new Exception(string.Join("\n", errors));
                }
                taskbar.Events.Clear();
                host = new HostController(api, new FakeStore());
                try
                {
                    var first = host.Attach(one, hostHwnd, HostingMode.Grouped, new Rectangle(-30000, -30000, 640, 480));
                    host.Activate(first, new Rectangle(-30000, -30000, 700, 450));
                    Check(api.Capture(one).Parent == 0, "grouped mode leaves window top-level");
                    Check(NativeWindowApi.ReadRect(one.Hwnd).Left == -30000, "grouped layout uses screen coordinates");
                    Check(host.DetachAll().Count == 0, "grouped mode cleanup succeeds");
                    Check(taskbar.Events.Count == 0, "grouped mode does not edit taskbar membership");
                }
                finally { host.DetachAll(); }

                // Exercise visible-window restoration with a recording taskbar service, never the real taskbar.
                api.Show(api.Capture(one), true, false);
                var visibleOriginal = api.Capture(one);
                Check(visibleOriginal.WasVisible, "visible synthetic window used for taskbar restoration regression");
                taskbar.Events.Clear();
                host = new HostController(api, new FakeStore());
                try
                {
                    var first = host.Attach(one, hostHwnd, HostingMode.Embedded, Bounds);
                    host.Activate(first, Bounds);
                    taskbar.FailAdd = true;
                    Check(host.DetachAll().Count == 0 && host.Sessions.Count == 0,
                        "taskbar failure does not undo structural restoration or block exit");
                    var restored = api.Capture(one);
                    Check(taskbar.Events.Contains("add:" + one.Handle) && restored.Parent == 0 && restored.WasVisible,
                        "visible detach explicitly re-adds taskbar item AFTER restoring top-level window");
                    Check(warnings.Count == 1, "taskbar restoration failure surfaces as warning");
                    string hiddenStage = trace.Last(line => line.StartsWith("restore.styles-hidden "));
                    Check(hiddenStage.Contains(" visible=False ") && hiddenStage.Contains("parentOrOwner=0x0 owner=0x0 root=0x" + one.Handle.ToString("X")),
                        "styles and parent restoration keep originally visible window hidden");
                    Check(restored.ExtendedStyle == visibleOriginal.ExtendedStyle && restored.Style == visibleOriginal.Style,
                        "visible detach restores complete style and extended style");
                    Check(trace.Any(line => line.StartsWith("restore.repaint-request") && line.Contains("accepted=true paint-complete=unknown")),
                        "embedded detach requests asynchronous repaint without claiming completion");
                    Check(trace.Any(line => line.StartsWith("taskbar.add") && line.Contains("call-returned=false HRESULT=")),
                        "taskbar failure trace contains operation and HRESULT");
                    Check(trace.Any(line => line.StartsWith("restore.complete") && line.Contains("visual-result=not-verified")),
                        "structural completion log does not claim visual Shell success");
                    Check(new[] { "hide", "parent", "styles-hidden", "placement", "show", "verify", "presentation" }
                        .All(stage => trace.Any(line => line.StartsWith("restore.phase ") && line.Contains("stage=" + stage + " phase-ms="))),
                        "restore phase timings separate native steps from diagnostic state queries");
                    int repaintIndex = trace.FindLastIndex(line => line.StartsWith("restore.repaint-request"));
                    int taskbarIndex = trace.FindLastIndex(line => line.StartsWith("taskbar.add"));
                    Check(repaintIndex < taskbarIndex, "repaint is requested before final taskbar registration");
                    taskbar.FailAdd = false;
                    taskbar.FailRemove = true;
                    first = host.Attach(one, hostHwnd, HostingMode.Embedded, Bounds);
                    Check(warnings.Count == 2 && host.Sessions.Count == 1, "taskbar removal failure does not corrupt attach transaction");
                    taskbar.FailRemove = false;
                    Check(host.DetachAll().Count == 0 && warnings.Count == 2, "successful taskbar restore finishes recovery without warning");
                }
                finally { host.DetachAll(); }

                // Minimise only our transparent synthetic window, using a non-activating command.
                // Do not maximise a real/synthetic window here: that could activate it on the user's desktop.
                foreach (var restoreMode in new[] { HostingMode.Embedded, HostingMode.Grouped })
                using (var minimisedWindow = Synthetic("Minimised " + restoreMode))
                {
                    var identity = NativeWindowApi.Inspect(minimisedWindow.Handle);
                    var placement = api.Capture(identity).Placement;
                    placement.ShowCommand = 7; // SW_SHOWMINNOACTIVE
                    Check(SetWindowPlacement(identity.Hwnd, ref placement), "native synthetic minimised setup succeeds: " + restoreMode);
                    var saved = api.Capture(identity);
                    Check(saved.WasVisible && (saved.Style & 0x20000000L) != 0,
                        "native minimised snapshot is visible and iconic: " + restoreMode);
                    var priorForeground = GetForegroundWindow();
                    var minHost = new HostController(api, new FakeStore());
                    try
                    {
                        minHost.Attach(identity, hostHwnd, restoreMode, new Rectangle(-30000, -30000, 640, 480));
                        Check(minHost.DetachAll().Count == 0, "native minimised detach completes: " + restoreMode);
                        var actual = api.Capture(identity);
                        Check(actual.Style == saved.Style && actual.ExtendedStyle == saved.ExtendedStyle &&
                            actual.WasVisible && (actual.Placement.ShowCommand == 2 || actual.Placement.ShowCommand == 7),
                            "native detach preserves minimised state and styles: " + restoreMode);
                        Check(GetForegroundWindow() == priorForeground,
                            "native minimised detach does not change foreground: " + restoreMode);
                    }
                    finally { minHost.DetachAll(); }
                }

                using (var child = new Panel { Dock = DockStyle.Left, Width = 100 })
                {
                    firstWindow.Controls.Add(child);
                    _ = child.Handle;
                    var diagnostic = WindowDiagnostics.Capture(one, true);
                    Check(diagnostic.Contains("DWM_HR=") && diagnostic.Contains("child#1") && diagnostic.Contains("SysSetRedraw=") &&
                        diagnostic.Contains("updatePending="), "read-only diagnostics contain DWM, child surfaces and paint state");
                    var changedIdentity = new WindowIdentity { Handle = one.Handle, ProcessId = one.ProcessId + 1, ThreadId = one.ThreadId };
                    Check(WindowDiagnostics.Capture(changedIdentity, true).Contains("identity=missing-or-changed"),
                        "diagnostic queries reject changed identities before reading child state");
                    string before = api.Capture(one).Style.ToString();
                    new WindowRepaintService().Request(one);
                    Check(WindowDiagnostics.Capture(one, true).Contains("updatePending=True"), "nonblocking redraw marks a window or child surface dirty");
                    Check(api.Capture(one).Style.ToString() == before, "read-only diagnostics and repaint do not alter window styles");
                }
                var fakeRepaint = new FakeRepaint { Fail = true };
                var repaintWarnings = new List<string>();
                var repaintApi = new NativeWindowApi(taskbar, repaintWarnings.Add, trace.Add, fakeRepaint);
                host = new HostController(repaintApi, new FakeStore());
                try
                {
                    host.Attach(one, hostHwnd, HostingMode.Embedded, Bounds);
                    Check(host.DetachAll().Count == 0 && host.Sessions.Count == 0, "repaint failure does not block structural detach or normal exit");
                    Check(fakeRepaint.Requests == 1 && repaintWarnings.Count == 1 && repaintWarnings[0].Contains("重绘"),
                        "failed repaint generates an explicit nonfatal warning");
                    Check(repaintApi.Capture(one).Parent == 0 && repaintApi.Capture(one).WasVisible,
                        "repaint failure leaves restored top-level window visible");
                }
                finally { host.DetachAll(); }
                taskbar.FailAdd = true;
                repaintApi = new NativeWindowApi(taskbar, _ => throw new Exception("warning sink failure"),
                    _ => throw new Exception("trace sink failure"), fakeRepaint);
                host = new HostController(repaintApi, new FakeStore());
                try
                {
                    host.Attach(one, hostHwnd, HostingMode.Embedded, Bounds);
                    Check(host.DetachAll().Count == 0 && host.Sessions.Count == 0,
                        "throwing trace or warning sinks cannot change ownership or block successful structural detach");
                }
                finally { taskbar.FailAdd = false; host.DetachAll(); }
            }
        }

        private static void RenderUi(string output, int? width, bool populated = false, bool hoverTab = false)
        {
            Application.EnableVisualStyles();
            Application.SetCompatibleTextRenderingDefault(false);
            var type = Assembly.Load("ExplorerTabs").GetType("ExplorerTabs.App.MainForm", true)!;
            var controller = new HostController(new FakeWindows(), new FakeStore());
            using (var form = (Form)Activator.CreateInstance(type, controller, new AppLog(Path.Combine(Path.GetTempPath(), "ExplorerTabs-ui-render.log")))!)
            {
                if (width.HasValue) form.Width = width.Value;
                if (populated)
                {
                    var first = Add(controller, 1); var second = Add(controller, 2); var third = Add(controller, 3); Add(controller, 4);
                    first.Title = "项目资料与美术资源";
                    second.Title = "下载";
                    third.Title = "很长的文件夹名称 · Tab 支持中文与 & 符号";
                    controller.RenameTab(first, "常用项目 & 资料");
                    controller.Activate(second, Bounds);
                    type.GetMethod("RefreshTabs", BindingFlags.Instance | BindingFlags.NonPublic)!.Invoke(form, null);
                    if (hoverTab)
                    {
                        var strip = (Control)type.GetField("tabs", BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(form)!;
                        var button = strip.Controls.Cast<Control>().FirstOrDefault(c => c.GetType().Name == "TabButton");
                        if (button != null)
                        {
                            button.GetType().GetField("hovered", BindingFlags.Instance | BindingFlags.NonPublic)!.SetValue(button, true);
                            button.GetType().GetField("closeHovered", BindingFlags.Instance | BindingFlags.NonPublic)!.SetValue(button, true);
                            button.Invalidate();
                        }
                    }
                }
                // Test-only managed visibility; never Show or trigger Shown/global keyboard hook.
                RenderHiddenForm(form, output);
            }
            Console.WriteLine("Rendered actual WinForms UI without showing or attaching any Explorer: " + output);
        }
        private static void CrossProcessSmoke()
        {
            Console.WriteLine("Cross-process smoke creates its OWN helper process and transparent window only.");
            string directory = Path.Combine(Path.GetTempPath(), "ExplorerTabs-cross-process-" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(directory);
            string ready = Path.Combine(directory, "ready.txt"), stop = Path.Combine(directory, "stop.txt");
            Process? helper = null;
            try
            {
                helper = Process.Start(new ProcessStartInfo
                {
                    FileName = Assembly.GetExecutingAssembly().Location,
                    Arguments = $"--native-helper \"{ready}\" \"{stop}\"",
                    UseShellExecute = false, CreateNoWindow = true, WindowStyle = ProcessWindowStyle.Hidden
                }) ?? throw new Exception("Could not start helper.");
                var watch = Stopwatch.StartNew();
                while (!File.Exists(ready))
                {
                    if (helper.HasExited || watch.Elapsed.TotalSeconds > 10) throw new Exception("Helper not ready.");
                    Thread.Sleep(50);
                }
                var foreign = NativeWindowApi.Inspect(new IntPtr(long.Parse(File.ReadAllText(ready))));
                Check(foreign.ProcessId == helper.Id && !NativeWindowApi.IsExplorer(foreign), "foreign test HWND belongs to owned helper, NOT Explorer");
                var api = new NativeWindowApi();
                var original = api.Capture(foreign);
                using (var container = Synthetic("Cross-process host"))
                {
                    IntPtr hostHwnd = container.Handle;
                    api.Show(api.Capture(NativeWindowApi.Inspect(hostHwnd)), true, false);
                    var controller = new HostController(api, new FakeStore());
                    try
                    {
                        var session = controller.Attach(foreign, hostHwnd, HostingMode.Embedded, Bounds);
                        controller.Activate(session, Bounds);
                        Check(api.Capture(foreign).Parent == hostHwnd.ToInt64(), "cross-process SetParent mounts helper window");
                        controller.LayoutActive(new Rectangle(0, 0, 750, 500));
                        var rect = NativeWindowApi.ReadRect(foreign.Hwnd);
                        Check(rect.Right - rect.Left == 750, "cross-process layout resizes helper window");
                        Check(controller.DetachAll().Count == 0, "cross-process detach completes before host destruction");
                        var restored = api.Capture(foreign);
                        Check(restored.Parent == original.Parent && restored.Style == original.Style &&
                            restored.ExtendedStyle == original.ExtendedStyle, "cross-process detach restores original parent and styles");
                    }
                    finally
                    {
                        var errors = controller.DetachAll();
                        if (errors.Count > 0) throw new Exception(string.Join("\n", errors));
                    }
                }
                Check(!helper.HasExited, "helper survives normal host container destruction AFTER detach");
                File.WriteAllText(stop, "stop");
                Check(helper.WaitForExit(5000) && helper.ExitCode == 0, "helper exits normally");
            }
            finally
            {
                if (helper != null)
                {
                    if (!helper.HasExited)
                    {
                        File.WriteAllText(stop, "stop");
                        if (!helper.WaitForExit(5000)) helper.Kill(); // This exact owned test process only.
                    }
                    helper.Dispose();
                }
                foreach (string file in Directory.GetFiles(directory)) File.Delete(file);
                Directory.Delete(directory);
            }
        }

        private static void CrossProcessUnawareSmoke()
        {
            Console.WriteLine("Cross-process unaware smoke creates its OWN helper process and hidden windows only.");
            string directory = Path.Combine(Path.GetTempPath(), "ExplorerTabs-cross-process-unaware-" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(directory);
            string ready = Path.Combine(directory, "ready.txt"), stop = Path.Combine(directory, "stop.txt");
            Process? helper = null;
            HostController? controller = null;
            try
            {
                helper = Process.Start(new ProcessStartInfo
                {
                    FileName = Assembly.GetExecutingAssembly().Location,
                    Arguments = $"--native-helper-unaware \"{ready}\" \"{stop}\"",
                    UseShellExecute = false, CreateNoWindow = true, WindowStyle = ProcessWindowStyle.Hidden
                }) ?? throw new Exception("Could not start unaware helper.");
                var watch = Stopwatch.StartNew();
                while (!File.Exists(ready))
                {
                    if (helper.HasExited || watch.Elapsed.TotalSeconds > 10) throw new Exception("Unaware helper not ready.");
                    Thread.Sleep(50);
                }
                var foreign = NativeWindowApi.Inspect(new IntPtr(long.Parse(File.ReadAllText(ready))));
                Check(foreign.ProcessId == helper.Id && NativeWindowApi.DescribeDpi(foreign.Hwnd) == "Unaware",
                    "owned helper window is a DPI-unaware top-level window");
                var trace = new List<string>();
                var api = new NativeWindowApi(trace: trace.Add);
                var original = api.Capture(foreign);
                using (var container = Synthetic("Cross-process unaware host"))
                {
                    IntPtr hostHwnd = container.Handle;
                    Check(NativeWindowApi.DescribeDpi(hostHwnd) == "PerMonitorV2", "test host remains PerMonitorV2");
                    api.Show(api.Capture(NativeWindowApi.Inspect(hostHwnd)), true, false);
                    uint hostDpi = GetDpiForWindow(hostHwnd), targetDpi = GetDpiForWindow(foreign.Hwnd);
                    controller = new HostController(api, new FakeStore());
                    if (hostDpi != 0 && hostDpi == targetDpi)
                    {
                        var session = controller.Attach(foreign, hostHwnd, HostingMode.Embedded, Bounds);
                        controller.Activate(session, Bounds);
                        Check(api.Capture(foreign).Parent == hostHwnd.ToInt64(),
                            "unaware helper embeds through cross-DPI SetParent at equal effective DPI");
                        Check(NativeWindowApi.DescribeDpi(foreign.Hwnd) == "PerMonitorV2",
                            "Windows reset the embedded helper to the host DPI context");
                        controller.LayoutActive(new Rectangle(0, 0, 750, 500));
                        var rect = NativeWindowApi.ReadRect(foreign.Hwnd);
                        Check(rect.Right - rect.Left == 750, "cross-DPI-context layout resizes the unaware helper");
                        Check(controller.DetachAll().Count == 0, "cross-DPI-context detach completes");
                        var restored = api.Capture(foreign);
                        Check(restored.Parent == original.Parent && restored.Style == original.Style &&
                            restored.ExtendedStyle == original.ExtendedStyle,
                            "cross-DPI-context detach restores original parent and styles");
                        Check(trace.Any(line => line.Contains("attach.dpi-mismatch") && line.Contains("allowed=True")),
                            "allowed DPI mismatch is traced with host/target contexts and DPIs");
                    }
                    else
                    {
                        var error = Throws(() => controller!.Attach(foreign, hostHwnd, HostingMode.Embedded, Bounds));
                        Check(error.Message.Contains("DPI") && error.Message.Contains(hostDpi.ToString()) && error.Message.Contains(targetDpi.ToString()),
                            "unequal effective DPI still rejects embedding with host/target DPI details");
                        Check(controller!.Sessions.Count == 0, "rejected DPI mismatch creates no session");
                        Check(trace.Any(line => line.Contains("attach.dpi-mismatch") && line.Contains("allowed=False")),
                            "rejected DPI mismatch is traced");
                    }
                }
                Check(!helper.HasExited, "unaware helper survives host container destruction");
                File.WriteAllText(stop, "stop");
                Check(helper.WaitForExit(5000) && helper.ExitCode == 0, "unaware helper exits normally");
            }
            finally
            {
                try { controller?.DetachAll(); } catch { /* The owned helper is stopped below. */ }
                if (helper != null)
                {
                    if (!helper.HasExited)
                    {
                        File.WriteAllText(stop, "stop");
                        if (!helper.WaitForExit(5000)) helper.Kill(); // This exact owned test process only.
                    }
                    helper.Dispose();
                }
                foreach (string file in Directory.GetFiles(directory)) File.Delete(file);
                Directory.Delete(directory);
            }
        }

        private static void NativeHelper(string ready, string stop, bool unaware = false)
        {
            // Must run before any WinForms HWND is created on this thread.
            if (unaware && SetThreadDpiAwarenessContext(new IntPtr(-1)) == IntPtr.Zero)
                throw new InvalidOperationException("SetThreadDpiAwarenessContext(UNAWARE) failed.");
            using (var window = Synthetic("Foreign helper"))
            using (var timer = new System.Windows.Forms.Timer { Interval = 50 })
            {
                string temp = ready + ".tmp";
                File.WriteAllText(temp, window.Handle.ToInt64().ToString());
                File.Move(temp, ready);
                timer.Tick += (_, __) => { if (File.Exists(stop)) Application.ExitThread(); };
                timer.Start();
                Application.Run(new ApplicationContext());
            }
        }
        [DllImport("user32.dll", SetLastError = true)]
        private static extern IntPtr SetThreadDpiAwarenessContext(IntPtr context);
        [DllImport("user32.dll")]
        private static extern uint GetDpiForWindow(IntPtr hwnd);
        [DllImport("user32.dll", SetLastError = true)]
        [return: MarshalAs(UnmanagedType.Bool)]
        private static extern bool SetWindowPlacement(IntPtr hwnd, ref WindowPlacement placement);
        [DllImport("user32.dll")]
        private static extern IntPtr GetForegroundWindow();
        [DllImport("gdi32.dll")] private static extern IntPtr CreateRectRgn(int left, int top, int right, int bottom);
        [DllImport("user32.dll")] private static extern int GetWindowRgn(IntPtr hwnd, IntPtr region);
        [DllImport("gdi32.dll")] private static extern bool DeleteObject(IntPtr handle);
        private static bool HasWindowRegion(Form form)
        {
            IntPtr region = CreateRectRgn(0, 0, 0, 0);
            try { return GetWindowRgn(form.Handle, region) != 0; }
            finally { DeleteObject(region); }
        }

        private static Form Synthetic(string title) => new SyntheticForm
        {
            Text = "ExplorerTabs test " + title, ShowInTaskbar = true, Opacity = 0,
            StartPosition = FormStartPosition.Manual, Location = new Point(-30000, -30000), Size = new Size(640, 480)
        };
        /// <summary>Ownerless top-level window kept out of the taskbar/Alt+Tab without
        /// WinForms' ShowInTaskbar=false parking owner (attach validation requires no owner).</summary>
        private sealed class SyntheticForm : Form
        {
            protected override CreateParams CreateParams
            {
                get
                {
                    var parameters = base.CreateParams;
                    parameters.ExStyle = (parameters.ExStyle | 0x80) & ~0x40000; // WS_EX_TOOLWINDOW, no WS_EX_APPWINDOW
                    return parameters;
                }
            }
        }

        private sealed class FakeRepaint : IWindowRepaintService
        {
            public bool Fail;
            public int Requests;
            public void Request(WindowIdentity identity)
            {
                Requests++;
                if (Fail) throw new Exception("Injected repaint request failure");
            }
        }

        private sealed class FakeTaskbar : ITaskbarService
        {
            public readonly List<string> Events = new List<string>();
            public bool FailAdd, FailRemove;
            public void Add(WindowIdentity identity)
            {
                Events.Add("add:" + identity.Handle);
                if (FailAdd) throw new Exception("Injected taskbar AddTab failure");
            }
            public void Remove(WindowIdentity identity)
            {
                Events.Add("remove:" + identity.Handle);
                if (FailRemove) throw new Exception("Injected taskbar DeleteTab failure");
            }
        }

        private sealed class FakeStore : ISnapshotStore
        {
            public bool Fail;
            public int Writes;
            public List<WindowSnapshot> Snapshots = new List<WindowSnapshot>();
            public void Save(IReadOnlyCollection<WindowSnapshot> snapshots)
            {
                Writes++;
                if (Fail) throw new IOException("Injected journal failure");
                Snapshots = snapshots.ToList();
            }
        }
        private sealed class FakeWindows : IWindowApi
        {
            public readonly HashSet<long> Alive = new HashSet<long> { 1, 2, 3, 4 };
            public readonly HashSet<long> Visible = new HashSet<long>();
            public readonly List<string> Events = new List<string>();
            public bool FailAttach, FailRestore, FailValidate, ThrowOnMatch;
            public long FailShowHandle;
            public long FailHideHandle, FailRestoreHandle;
            public bool Matches(WindowIdentity identity)
            {
                if (ThrowOnMatch) throw new System.ComponentModel.Win32Exception(5, "Injected access denied");
                return Alive.Contains(identity.Handle);
            }
            public WindowSnapshot Capture(WindowIdentity identity) => new WindowSnapshot { Identity = identity };
            public void ValidateAttach(WindowSnapshot snapshot, IntPtr host, HostingMode mode)
            { if (FailValidate) throw new InvalidOperationException("Injected validation failure"); }
            public void Attach(WindowSnapshot snapshot, IntPtr host, Rectangle bounds)
            {
                Events.Add("attach:" + snapshot.Identity.Handle);
                if (FailAttach) throw new InvalidOperationException("Injected partial attach failure");
            }
            public void Layout(WindowSnapshot snapshot, Rectangle bounds) => Events.Add("layout:" + snapshot.Identity.Handle);
            public void Show(WindowSnapshot snapshot, bool visible, bool raise)
            {
                long handle = snapshot.Identity.Handle;
                Events.Add((visible ? "show:" : "hide:") + handle);
                if (!visible && handle == FailHideHandle)
                { FailHideHandle = 0; throw new InvalidOperationException("Injected hide failure"); }
                if (visible && handle == FailShowHandle) throw new InvalidOperationException("Injected show failure");
                if (visible) Visible.Add(handle); else Visible.Remove(handle);
            }
            public void Restore(WindowSnapshot snapshot)
            {
                Events.Add("restore:" + snapshot.Identity.Handle);
                if (FailRestore || snapshot.Identity.Handle == FailRestoreHandle) throw new InvalidOperationException("Injected restore failure");
            }
        }
    }
}
