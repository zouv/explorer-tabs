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
        private const BindingFlags PrivateInstance = BindingFlags.Instance | BindingFlags.NonPublic;
        private static object? UiCall(object target, string method, params object?[] arguments) =>
            target.GetType().GetMethod(method, PrivateInstance)!.Invoke(target, arguments);
        private static T UiField<T>(object target, string name) => (T)target.GetType().GetField(name, PrivateInstance)!.GetValue(target)!;
        private static void MakeHidden(Control control)
        {
            // Forcing the internal Visible state also makes CreateControl create a WS_VISIBLE
            // handle. Keep verification invisible: fully transparent and out of the taskbar,
            // so test runs never flash windows or taskbar entries over the user's desktop.
            if (control is Form form) { form.Opacity = 0; form.ShowInTaskbar = false; }
            typeof(Control).GetMethod("SetState", PrivateInstance)!.Invoke(control, new object[] { 2, true });
            control.CreateControl(); control.PerformLayout();
        }
        private static void PumpTask(Task task)
        {
            var watch = System.Diagnostics.Stopwatch.StartNew();
            while (!task.IsCompleted && watch.ElapsedMilliseconds < 3000) { Application.DoEvents(); Thread.Sleep(5); }
            Check(task.IsCompleted, "hidden UI async request completes within bounded test time");
            task.GetAwaiter().GetResult();
        }
        private static void CreationUiSmoke()
        {
            Console.WriteLine("Creation UI smoke: only hidden forms, fake Shell service and fake native windows. No Show, hooks or Explorer activation.");
            string directory = Path.Combine(Path.GetTempPath(), "ExplorerTabs-creation-ui-" + Guid.NewGuid().ToString("N"));
            var type = Assembly.Load("ExplorerTabs").GetType("ExplorerTabs.App.MainForm", true)!;
            var dialogType = Assembly.Load("ExplorerTabs").GetType("ExplorerTabs.App.SettingsDialog", true)!;
            var settings = new AppSettings { Mode = HostingMode.Grouped, NewTabDirectory = @"C:\New" };
            var context = SynchronizationContext.Current;
            SynchronizationContext.SetSynchronizationContext(new WindowsFormsSynchronizationContext());
            try
            {
                var api = new FakeWindows(); var journal = new FakeStore(); var host = new HostController(api, journal);
                var service = new UiCreationService(); var store = new UiSettingsStore();
                using (var form = (Form)Activator.CreateInstance(type, host, new AppLog(Path.Combine(directory, "host.log")), settings, store, service)!)
                {
                    form.Width = 720; MakeHidden(form);
                    Check(UiField<ComboBox>(form, "mode").SelectedIndex == 1 && UiField<CheckBox>(form, "shortcuts").Checked,
                        "main form initializes from saved mode and shortcut settings without showing");
                    var toolbar = UiField<FlowLayoutPanel>(form, "commandToolbar");
                    var toolbarRight = UiField<FlowLayoutPanel>(form, "commandToolbarRight");
                    Check(toolbar.Controls.Cast<Control>().Where(c => c.Visible).All(c => c.Left >= 0 && c.Right <= toolbar.ClientSize.Width && c.Bottom <= toolbar.ClientSize.Height) &&
                        toolbarRight.Controls.Cast<Control>().Where(c => c.Visible).All(c => c.Left >= 0 && c.Right <= toolbarRight.ClientSize.Width && c.Bottom <= toolbarRight.ClientSize.Height),
                        "minimum-width toolbar keeps every visible action control inside its group");
                    var discovery = type.GetProperty("ExplorerDiscovery", BindingFlags.Instance | BindingFlags.NonPublic)!;
                    discovery.SetValue(form, (Func<IReadOnlyList<WindowIdentity>>)(() => new[] { Identity(7), Identity(8) }));
                    using (var menu = (ContextMenuStrip)UiCall(form, "CreateAddWindowsMenu")!)
                    {
                        var items = menu.Items.OfType<ToolStripMenuItem>().Select(i => i.Text).ToArray();
                        Check(items.SequenceEqual(new[] { "加入【Test 7】窗口", "加入【Test 8】窗口", "打开“加入”管理界面" }),
                            "attach menu lists discovered windows with a join prefix then the join manager entry");
                        var windowItem = menu.Items.OfType<ToolStripMenuItem>().First();
                        Check(windowItem.ToolTipText.StartsWith("把“Test 7”加入为标签"), "attach window entry explains the click and keeps identity details on hover");
                    }
                    discovery.SetValue(form, (Func<IReadOnlyList<WindowIdentity>>)(() => Array.Empty<WindowIdentity>()));
                    using (var menu = (ContextMenuStrip)UiCall(form, "CreateAddWindowsMenu")!)
                    {
                        var placeholders = menu.Items.OfType<ToolStripMenuItem>().ToArray();
                        Check(!placeholders[0].Enabled && placeholders[0].Text == "暂无可加入的 Explorer 窗口" &&
                            placeholders.Last().Text == "打开“加入”管理界面",
                            "attach menu keeps an explicit empty state and join manager entry");
                    }
                    var task = (Task)UiCall(form, "CreateTabAsync", new object?[] { null })!; PumpTask(task);
                    Check(host.Sessions.Count == 1 && host.Active!.Identity.Handle == 1 && service.Calls == 1 && service.Source == null,
                        "new tab on empty host attaches only returned fake identity and activates it");
                    var first = host.Active!; host.RenameTab(first, "source alias");
                    service.NextHandle = 2;
                    task = (Task)UiCall(form, "CreateTabAsync", first)!; PumpTask(task);
                    Check(host.Active!.Identity.Handle == 2 && service.Source!.Handle == 1 && host.Sessions[1].CustomTitle == null && first.CustomTitle == "source alias",
                        "duplicate uses explicit source HWND, creates second tab and does not inherit custom alias");
                    Check(service.Excluded.SequenceEqual(new long[] { 1 }) && service.Directory == @"C:\New",
                        "UI passes existing attached handles and saved default directory to creation service");
                    service.Hold = true; service.NextHandle = 3;
                    task = (Task)UiCall(form, "CreateTabAsync", new object?[] { null })!;
                    Check(!task.IsCompleted && !UiField<Button>(form, "newTab").Enabled &&
                        !UiField<Button>(form, "addWindows").Enabled,
                        "pending creation immediately disables conflicting operations");
                    Check(PumpUntil(() => UiField<Button>(form, "cancelCreation").Visible, 1200),
                        "pending creation exposes cancellation after the reveal delay");
                    using (var menu = (ContextMenuStrip)UiCall(form, "CreateAppMenu")!)
                        Check(!menu.Items.OfType<ToolStripMenuItem>().First(i => i.Text == "设置…").Enabled,
                            "pending creation disables the app-menu settings entry");
                    string progress = UiField<Label>(form, "status").Text; UiCall(form, "UpdateStatus");
                    Check(UiField<Label>(form, "status").Text == progress, "monitor status update does not overwrite creation progress");
                    int calls = service.Calls;
                    PumpTask((Task)UiCall(form, "CreateTabAsync", new object?[] { null })!);
                    Check(service.Calls == calls, "UI ignores parallel new-tab requests");
                    UiField<Button>(form, "cancelCreation").PerformClick(); PumpTask(task);
                    Check(host.Sessions.Count == 2 && host.Active!.Identity.Handle == 2 && !UiField<Button>(form, "newTab").Enabled &&
                        !UiField<Button>(form, "cancelCreation").Visible,
                        "cancelled request keeps original tabs, hides cancel and respects worker still-busy gate");
                    service.Finish(); Application.DoEvents(); UiCall(form, "SetCreationControls", false);
                    Check(host.Sessions.Count == 2 && UiField<Button>(form, "newTab").Enabled,
                        "late service completion never attaches cancelled window and unlocks only after worker exit");
                    var candidate = settings.Copy(); candidate.ShortcutsEnabled = false; candidate.NewTabDirectory = @"C:\Changed";
                    UiCall(form, "ApplySettings", candidate);
                    Check(store.Saves == 1 && !UiField<CheckBox>(form, "shortcuts").Checked && UiField<AppSettings>(form, "settings").NewTabDirectory == @"C:\Changed",
                        "settings save commits storage and then live UI");
                    store.Fail = true; candidate.ShortcutsEnabled = true; candidate.NewTabDirectory = @"C:\Failed";
                    Check(Throws(() => UiCall(form, "ApplySettings", candidate)).InnerException is IOException &&
                        !UiField<CheckBox>(form, "shortcuts").Checked && UiField<AppSettings>(form, "settings").NewTabDirectory == @"C:\Changed",
                        "failed settings save leaves active preferences unchanged");
                    store.Fail = false; candidate.Mode = HostingMode.Embedded; int saves = store.Saves;
                    Check(Throws(() => UiCall(form, "ApplySettings", candidate)).InnerException is InvalidOperationException && store.Saves == saves,
                        "settings cannot change hosting mode while real-session models are attached");
                    api.FailShowHandle = 3; api.Events.Clear();
                    Check(Throws(() => UiCall(form, "AttachCreated", new CreatedExplorer(Identity(3), @"C:\New"), HostingMode.Grouped)).InnerException is InvalidOperationException &&
                        host.Sessions.Count == 2 && host.Active!.Identity.Handle == 2 && api.Events.Contains("restore:3") && journal.Snapshots.Count == 2,
                        "new-tab activation failure rolls back new window through safe detach and preserves prior active tab");
                    api.FailShowHandle = 0; api.FailValidate = true; api.Events.Clear();
                    Check(Throws(() => UiCall(form, "AttachCreated", new CreatedExplorer(Identity(3), @"C:\New"), HostingMode.Grouped)).InnerException is InvalidOperationException &&
                        host.Sessions.Count == 2 && !api.Events.Contains("show:3"), "attachment validation failure does not activate or claim the new window");
                    api.FailValidate = false; api.FailAttach = true; api.FailRestoreHandle = 3;
                    Check(Throws(() => UiCall(form, "AttachCreated", new CreatedExplorer(Identity(3), @"C:\New"), HostingMode.Grouped)).InnerException is AggregateException &&
                        host.Sessions.Count == 3 && host.Sessions[2].State == SessionState.RecoveryRequired &&
                        TabControls(UiField<FlowLayoutPanel>(form, "tabs")).Length == 3 && journal.Snapshots.Count == 3,
                        "partial attach and rollback failure retains recovery journal entry and displays its recovery tab immediately");
                    api.FailAttach = false; api.FailRestoreHandle = 0; host.DetachAll();
                    UiCall(form, "SaveSettingsOnExit");
                    Check(store.Last!.Bounds.Right > store.Last.Bounds.Left && !store.Last.ShortcutsEnabled,
                        "normal exit settings capture host bounds and live shortcut state after detach");
                }
                var embeddedSettings = new AppSettings { Mode = HostingMode.Embedded, NewTabDirectory = @"C:\New" };
                var embeddedHost = new HostController(new FakeWindows(), new FakeStore());
                var embeddedService = new UiCreationService();
                using (var form = (Form)Activator.CreateInstance(type, embeddedHost, new AppLog(Path.Combine(directory, "embedded.log")), embeddedSettings, null, embeddedService)!)
                {
                    MakeHidden(form);
                    var task = (Task)UiCall(form, "CreateTabAsync", new object?[] { null })!; PumpTask(task);
                    Check(embeddedHost.Sessions.Count == 1 && embeddedHost.Active!.Snapshot.Mode == HostingMode.Embedded && embeddedService.Calls == 1,
                        "first tab in empty embedded host completes without experimental-confirmation dialog");
                    embeddedHost.DetachAll();
                    UiCall(form, "RefreshTabs");
                    UiCall(form, "AttachSelectedWindows", new object[] { new[] { Identity(3), Identity(4) } });
                    Check(embeddedHost.Sessions.Count == 2 && embeddedHost.Active!.Identity.Handle == 4 && embeddedService.Calls == 1,
                        "explicitly selected windows join empty embedded host directly without confirmation or Shell creation");
                    Check(embeddedHost.Sessions.All(s => s.Snapshot.Mode == HostingMode.Embedded), "direct join retains selected embedded hosting mode");
                    embeddedHost.DetachAll();
                }
                var modalService = new UiCreationService { Hold = true };
                var modalHost = new HostController(new FakeWindows(), new FakeStore());
                using (var form = (Form)Activator.CreateInstance(type, modalHost, new AppLog(Path.Combine(directory, "modal.log")), settings, null, modalService)!)
                {
                    MakeHidden(form);
                    var task = (Task)UiCall(form, "CreateTabAsync", new object?[] { null })!;
                    var modal = (IDisposable)UiCall(form, "EnterModal")!;
                    try
                    {
                        modalService.Finish(); Application.DoEvents();
                        Check(modalHost.Sessions.Count == 0 && !task.IsCompleted, "successful async creation waits until modal interaction ends before attaching");
                    }
                    finally { modal.Dispose(); }
                    PumpTask(task);
                    Check(modalHost.Sessions.Count == 1 && modalHost.Active != null, "deferred new tab attaches after modal scope is safely released");
                    modalHost.DetachAll();
                }
                // Service intentionally ignores cancellation to verify the UI's independent late-result guard.
                var delayed = new UiCreationService { Hold = true, IgnoreCancellation = true };
                var emptyHost = new HostController(new FakeWindows(), new FakeStore());
                var disposed = (Form)Activator.CreateInstance(type, emptyHost, new AppLog(Path.Combine(directory, "dispose.log")), settings, null, delayed)!;
                MakeHidden(disposed);
                var pending = (Task)UiCall(disposed, "CreateTabAsync", new object?[] { null })!;
                disposed.Dispose(); delayed.Finish(); PumpTask(pending);
                Check(emptyHost.Sessions.Count == 0 && delayed.Token.IsCancellationRequested, "disposed host cancels request and rejects even an uncancellable late success");
                using (var dialog = (Form)Activator.CreateInstance(dialogType, settings, true, true)!)
                {
                    MakeHidden(dialog);
                    Check(!UiField<ComboBox>(dialog, "mode").Enabled && UiField<TextBox>(dialog, "directory").Text == @"C:\New",
                        "settings dialog locks mode for existing tabs and pre-fills directory");
                    UiField<TextBox>(dialog, "directory").Text = "shell:RecycleBinFolder"; ((Button)dialog.AcceptButton!).PerformClick();
                    Check(dialog.DialogResult == DialogResult.None && UiField<Label>(dialog, "error").Text.Length > 0,
                        "invalid setting is reported inline without closing or invoking Shell");
                    UiField<TextBox>(dialog, "directory").Text = @" C:\资料\ "; ((Button)dialog.AcceptButton!).PerformClick();
                    var result = (AppSettings)dialogType.GetProperty("Result")!.GetValue(dialog)!;
                    Check(dialog.DialogResult == DialogResult.OK && result.NewTabDirectory == @"C:\资料", "settings confirmation returns normalized folder");
                }
                using (var dialog = (Form)Activator.CreateInstance(dialogType, settings, true, true)!)
                {
                    MakeHidden(dialog);
                    UiField<TextBox>(dialog, "directory").Text = FolderTarget.ThisPc; ((Button)dialog.AcceptButton!).PerformClick();
                    var result = (AppSettings)dialogType.GetProperty("Result")!.GetValue(dialog)!;
                    Check(dialog.DialogResult == DialogResult.OK && result.NewTabDirectory == FolderTarget.ThisPc,
                        "settings accepts the This PC virtual target");
                }
                using (var dialog = (Form)Activator.CreateInstance(dialogType, settings, false, false)!)
                {
                    MakeHidden(dialog); ((Button)dialog.CancelButton!).PerformClick();
                    Check(dialog.DialogResult == DialogResult.Cancel && ((AppSettings)dialogType.GetProperty("Result")!.GetValue(dialog)!).NewTabDirectory == settings.NewTabDirectory,
                        "settings cancel preserves original values when persistence is unavailable");
                }
            }
            finally
            {
                SynchronizationContext.SetSynchronizationContext(context);
                if (Directory.Exists(directory)) { foreach (string own in Directory.GetFiles(directory)) File.Delete(own); Directory.Delete(directory); }
            }
        }
        private static void RenderSettings(string output)
        {
            Application.EnableVisualStyles(); Application.SetCompatibleTextRenderingDefault(false);
            var type = Assembly.Load("ExplorerTabs").GetType("ExplorerTabs.App.SettingsDialog", true)!;
            using (var form = (Form)Activator.CreateInstance(type, new AppSettings { NewTabDirectory = @"F:\工作资料与常用目录" }, true, true)!)
                RenderHiddenForm(form, output);
            Console.WriteLine("Rendered actual settings form without Show or Shell invocation: " + output);
        }
        private sealed class UiCreationService : IExplorerTabService
        {
            public bool Hold, IgnoreCancellation; public long NextHandle = 1; public int Calls;
            public bool IsBusy { get; private set; }
            public WindowIdentity? Source; public long[] Excluded = Array.Empty<long>(); public string Directory = ""; public CancellationToken Token;
            private TaskCompletionSource<CreatedExplorer>? pending; private CancellationTokenRegistration registration;
            public Task<CreatedExplorer> CreateAsync(string directory, WindowIdentity? source, System.Collections.Generic.IReadOnlyCollection<long> attached, CancellationToken token)
            {
                Calls++; Directory = directory; Source = source; Excluded = attached.ToArray(); Token = token;
                if (!Hold) return Task.FromResult(new CreatedExplorer(Identity(NextHandle), directory));
                IsBusy = true; pending = new TaskCompletionSource<CreatedExplorer>(TaskCreationOptions.RunContinuationsAsynchronously);
                if (!IgnoreCancellation) registration = token.Register(() => pending.TrySetCanceled());
                return pending.Task;
            }
            public void Finish() { IsBusy = false; registration.Dispose(); pending!.TrySetResult(new CreatedExplorer(Identity(NextHandle), Directory)); }
        }
        private sealed class UiSettingsStore : IAppSettingsStore
        {
            public bool Fail; public int Saves; public AppSettings? Last;
            public AppSettings? Read() => Last;
            public void Save(AppSettings settings) { Saves++; if (Fail) throw new IOException("fake settings save failure"); Last = settings.Copy(); }
        }
    }
}
