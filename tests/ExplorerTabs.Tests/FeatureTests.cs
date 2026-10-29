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
        private static void FeatureUnitTests()
        {
            Check(FolderTarget.Normalize(@" c:\资料\..\新目录\ ") == @"c:\新目录", "folder normalization supports Unicode and canonical dot segments");
            Check(FolderTarget.Normalize(@"C:/") == @"C:\", "drive root keeps its trailing separator");
            Check(FolderTarget.Same(@"C:\Test\", @"c:/test"), "folder comparison is case insensitive and separator independent");
            Check(FolderTarget.Normalize(@"\\server\share\folder\") == @"\\server\share\folder", "UNC folder retains server and share");
            Check(FolderTarget.FromLocationUrl("file:///C:/%E8%B5%84%E6%96%99/a%20b%23x") == @"C:\资料\a b#x", "file URL decodes Unicode spaces and literal hash");
            Check(FolderTarget.FromLocationUrl("file://server/share/path") == @"\\server\share\path", "Shell UNC file URL converts to folder");
            Check(FolderTarget.Normalize("shell:MyComputerFolder") == FolderTarget.ThisPc &&
                FolderTarget.Normalize("::{20D04FE0-3AEA-1069-A2D8-08002B30309D}") == FolderTarget.ThisPc &&
                FolderTarget.Display(FolderTarget.ThisPc) == "此电脑" &&
                FolderTarget.Same(FolderTarget.ThisPc, "shell:mycomputerfolder"),
                "This PC is the only accepted virtual target and compares case-insensitively");
            foreach (string invalid in new[] { "", " ", "relative", "C:relative", @"\rooted", "https://example.com", "shell:RecycleBinFolder",
                "explorer.exe /n", @"C:\a"" /x", "C:\\a\nb", "C:\\a\tb", "C:\\a\0b", @"C:\a:stream", @"C:\a?b", @"C:\a*b",
                @"C:\a|b", @"C:\a<b", @"\\?\C:\folder", @"\\.\C:\folder", @"\\server", @"\\server\", @"\\server\..\x", @"\\\share" })
                Check(Throws(() => FolderTarget.Normalize(invalid)) is ArgumentException, "unsafe/nonabsolute folder rejected: " + invalid.Replace('\0', '_'));
            Check(FolderTarget.FromLocationUrl("shell:MyComputerFolder") == FolderTarget.ThisPc,
                "This PC shell URL maps to the canonical token");
            foreach (string url in new[] { "", "shell:RecycleBinFolder", "search-ms:query=test", "https://example.com/", "file:///C:/Test?q=1", "file:///C:/Test#anchor" })
                Check(Throws(() => FolderTarget.FromLocationUrl(url)) is InvalidOperationException, "virtual or ambiguous Shell URL rejected: " + url);

            var policy = new NewTabShortcutPolicy();
            var decision = policy.Handle(0x54, true, false, true, false, false, true);
            Check(decision.Create && decision.Suppress, "Ctrl+T creates and suppresses eligible first keydown");
            decision = policy.Handle(0x54, true, false, true, false, false, true);
            Check(!decision.Create && decision.Suppress, "held Ctrl+T suppresses repeats without creating more windows");
            decision = policy.Handle(0x54, false, false, false, true, true, false);
            Check(decision.Suppress && !decision.Create, "Ctrl+T keyup pairs even after focus and modifiers change");
            Check(!policy.Handle(0x54, false, false, true, false, false, true).Suppress, "extra T keyup is untouched");
            foreach (var flags in new[] { new[] { false, false, false, true }, new[] { true, true, false, true },
                new[] { true, false, true, true }, new[] { true, false, false, false } })
            {
                policy = new NewTabShortcutPolicy();
                decision = policy.Handle(0x54, true, false, flags[0], flags[1], flags[2], flags[3]);
                Check(!decision.Create && !decision.Suppress && !policy.Handle(0x54, false, false, true, false, false, true).Suppress,
                    "plain/shift/alt/out-of-scope T passes through with paired keyup");
            }
            policy = new NewTabShortcutPolicy(true);
            Check(!policy.Handle(0x54, true, false, true, false, false, true).Suppress, "startup-held T is not reinterpreted");
            policy.Handle(0x54, false, false, false, false, false, false);
            Check(policy.Handle(0x54, true, false, true, false, false, true).Create, "released startup T permits next chord");
            policy = new NewTabShortcutPolicy();
            policy.Handle(0x54, true, false, false, false, false, true);
            Check(!policy.Handle(0x54, true, false, true, false, false, true).Create, "Ctrl pressed after plain held T cannot create");
            policy = new NewTabShortcutPolicy();
            Check(!policy.Handle(0x54, true, true, true, false, false, true).Suppress &&
                policy.Handle(0x54, true, false, true, false, false, true).Create, "injected T does not alter physical key state");
            Check(!policy.Handle(0x54, false, true, false, false, false, false).Suppress &&
                policy.Handle(0x54, false, false, false, false, false, false).Suppress, "injected keyup cannot consume physical T pairing");
            Check(!policy.Handle(9, true, false, true, false, false, true).Suppress &&
                !policy.Handle(0x57, true, false, true, false, false, true).Create, "new-tab policy leaves Tab and Ctrl+W alone");
            Check(GlobalHotkeyDefinition.Normalize(" ctrl + alt + e ") == "Ctrl+Alt+E" &&
                GlobalHotkeyDefinition.Normalize("Shift+Ctrl+F5") == "Ctrl+Shift+F5" &&
                GlobalHotkeyDefinition.Normalize("Alt+1") == "Alt+1" &&
                GlobalHotkeyDefinition.Format(GlobalHotkeyDefinition.Control | GlobalHotkeyDefinition.Alt, 'E') == "Ctrl+Alt+E",
                "global hotkey parsing formats modifiers and keys canonically");
            foreach (string invalid in new[] { "E", "Ctrl+", "Ctrl+Alt", "Win+E", "Ctrl+Win+E", "Ctrl+Alt+F13", "Ctrl+Alt+Arrow", "Alt+F4" })
                Check(!GlobalHotkeyDefinition.IsValid(invalid), "invalid global hotkey rejected: " + invalid);
            Check(GlobalHotkeyDefinition.Conflicts("Ctrl+T", HostShortcutCatalog.Defaults()) &&
                !GlobalHotkeyDefinition.Conflicts("Ctrl+Alt+E", HostShortcutCatalog.Defaults()),
                "global hotkey duplicates host shortcuts are detected");
            SettingsUnitTests(); CreationWorkflowTests(); CreationServiceTests();
        }

        private static void SettingsUnitTests()
        {
            string directory = Path.Combine(Path.GetTempPath(), "ExplorerTabs-settings-" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(directory);
            string file = Path.Combine(directory, "settings.json"); var store = new AppSettingsStore(file);
            try
            {
                Check(store.Read() == null, "missing settings return defaults at caller without creating a file");
                var original = new AppSettings { Mode = HostingMode.Grouped, ShortcutsEnabled = false, NewTabDirectory = @"C:\资料 & 空间",
                    Bounds = new NativeRect { Left = -1100, Top = 10, Right = -100, Bottom = 810 }, Maximized = true };
                store.Save(original); var loaded = store.Read()!;
                Check(loaded.Mode == original.Mode && !loaded.ShortcutsEnabled && loaded.NewTabDirectory == original.NewTabDirectory &&
                    loaded.Bounds.Left == -1100 && loaded.Maximized, "settings roundtrip mode shortcuts Unicode directory bounds and maximized");
                original.NewTabDirectory = @"C:\second"; store.Save(original);
                Check(store.Read()!.NewTabDirectory == @"C:\second" && Directory.GetFiles(directory).Length == 1, "atomic replacement saves updated settings without temp leftovers");
                var copy = original.Copy(); copy.NewTabDirectory = @"C:\third";
                Check(original.NewTabDirectory == @"C:\second", "settings copies do not mutate originals");
                original.GlobalHotkey = " Ctrl+Alt+E "; store.Save(original);
                Check(store.Read()!.GlobalHotkey == " Ctrl+Alt+E ", "global hotkey roundtrips in settings");
                Check(Throws(() => store.Save(new AppSettings { GlobalHotkey = "Ctrl+T" })) is InvalidDataException &&
                    Throws(() => store.Save(new AppSettings { GlobalHotkey = "E" })) is InvalidDataException,
                    "global hotkey conflicts and invalid formats are rejected");
                original.GlobalHotkey = null;
                File.WriteAllText(file, "{}"); loaded = store.Read()!;
                Check(loaded.Version == 1 && loaded.Mode == HostingMode.Embedded && loaded.ShortcutsEnabled &&
                    loaded.NewTabDirectory == Environment.GetFolderPath(Environment.SpecialFolder.UserProfile) && loaded.GlobalHotkey == null,
                    "partial legacy settings receive field defaults");
                foreach (string content in new[] { "null", "broken json", "{\"Version\":2}", "{\"Mode\":99}", "{\"NewTabDirectory\":\"shell:RecycleBinFolder\"}", new string('x', 32769) })
                {
                    File.WriteAllText(file, content);
                    Check(Throws(() => store.Read()) != null && File.ReadAllText(file) == content, "bad/oversize/future settings are rejected and preserved");
                }
                store.Save(original); string prior = File.ReadAllText(file);
                Check(Throws(() => store.Save(new AppSettings { Version = 2 })) is InvalidDataException && File.ReadAllText(file) == prior,
                    "save validation failure preserves old settings");
                using (File.Open(file, FileMode.Open, FileAccess.Read, FileShare.None))
                    Check(Throws(() => store.Save(new AppSettings())) is IOException, "locked destination rejects replacement");
                Check(File.ReadAllText(file) == prior && Directory.GetFiles(directory).Length == 1, "failed atomic save preserves previous bytes and cleans its own temporary file");
            }
            finally { foreach (string own in Directory.GetFiles(directory)) File.Delete(own); Directory.Delete(directory); }
            var fallback = new Rectangle(100, 100, 1000, 700);
            var screens = new[] { new Rectangle(-1920, 0, 1920, 1080), new Rectangle(0, 0, 1920, 1040) };
            var restored = SettingsLayoutPolicy.Restore(SettingsLayoutPolicy.Save(new Rectangle(-1500, 40, 1000, 800)), screens, fallback);
            Check(restored == new Rectangle(-1500, 40, 1000, 800), "negative coordinate monitor restores correctly");
            restored = SettingsLayoutPolicy.Restore(SettingsLayoutPolicy.Save(new Rectangle(-1500, 40, 1000, 800)), new[] { screens[1] }, fallback);
            Check(screens[1].Contains(restored) && restored.Size == new Size(1000, 800), "removed monitor moves the host into an available working area");
            restored = SettingsLayoutPolicy.Restore(new NativeRect { Left = int.MinValue, Right = int.MaxValue, Bottom = int.MaxValue }, screens, fallback);
            Check(restored == fallback, "corrupt or overflowing saved bounds use safe fallback");
            restored = SettingsLayoutPolicy.Restore(SettingsLayoutPolicy.Save(new Rectangle(1800, 1000, 5000, 3000)), screens, fallback);
            Check(screens[1].Contains(restored), "oversized saved bounds clamp fully into monitor");
            Check(SettingsLayoutPolicy.Restore(default, Array.Empty<Rectangle>(), fallback) == fallback, "missing display inventory retains fallback");
            var small = new Rectangle(0, 0, 640, 480);
            Check(SettingsLayoutPolicy.Restore(default, new[] { small }, fallback) == small, "small working area clamps geometry without overflow");
        }

        private static CreatedExplorer RunCreate(CreationBackend fake, CancellationToken token = default, WindowIdentity? source = null,
            IReadOnlyCollection<long>? attached = null) => ExplorerCreationWorkflow.Run(fake, @"C:\New", source, attached ?? Array.Empty<long>(), token);
        private static void CreationWorkflowTests()
        {
            var backend = new CreationBackend(); var result = RunCreate(backend);
            Check(result.Identity.Handle == 10 && result.Directory == @"C:\New" && backend.Browser.Disposals == 1,
                "workflow returns exact dedicated browser and releases only its COM reference");
            Check(backend.Events.SequenceEqual(new[] { "exists", "snapshot", "create", "inspect:10", "navigate:C:\\New", "show" }),
                "workflow verifies explicit new identity before navigation and never guesses another window");
            backend = new CreationBackend(); backend.SourceDirectory = @"\\server\share\folder";
            result = RunCreate(backend, source: Identity(2));
            Check(backend.ReadSource!.Handle == 2 && result.Directory == backend.SourceDirectory && backend.Browser.Navigated == backend.SourceDirectory,
                "duplicate reads only requested source and creates an independent browser at its directory");
            foreach (long excluded in new long[] { 1, 2 })
            {
                backend = new CreationBackend(); backend.Browser.CurrentHandle = excluded;
                Check(Throws(() => RunCreate(backend, attached: new long[] { 2 })) is InvalidOperationException && backend.Browser.Navigated == null && backend.Browser.Shows == 0,
                    "existing or already-attached HWND rejected BEFORE navigation: " + excluded);
            }
            backend = new CreationBackend(); backend.Exists = false;
            Check(Throws(() => RunCreate(backend)) is InvalidOperationException && !backend.Events.Contains("create"), "inaccessible directory fails without creating any browser");
            backend = new CreationBackend(); backend.SourceDirectory = "shell:RecycleBinFolder";
            Check(Throws(() => RunCreate(backend, source: Identity(1))) is ArgumentException && !backend.Events.Contains("create"), "unsupported virtual source fails before new-window activation");
            backend = new CreationBackend(); backend.SourceDirectory = FolderTarget.ThisPc;
            result = RunCreate(backend, source: Identity(1));
            Check(result.Directory == FolderTarget.ThisPc && backend.Browser.Navigated == FolderTarget.ThisPc &&
                backend.Events.Contains("isat:" + FolderTarget.ThisPc) && !backend.Events.Contains("exists"),
                "This PC source skips the filesystem existence check and confirms the PIDL target");
            backend = new CreationBackend(); backend.ReadFailure = true;
            Check(Throws(() => RunCreate(backend, source: Identity(1))) is InvalidOperationException && !backend.Events.Contains("create"), "missing source browser does not fall back to a guessed folder");
            backend = new CreationBackend(); backend.Inspected = Identity(10); backend.Inspected.ClassName = "NotExplorer";
            Check(Throws(() => RunCreate(backend)) is InvalidOperationException && backend.Browser.Navigated == null, "wrong window class rejected before navigation");
            backend = new CreationBackend(); backend.Inspected = Identity(10);
            backend.Inspected.ImagePath = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.System), "rundll32.exe");
            result = RunCreate(backend);
            Check(result.Identity.Handle == 10 && backend.Browser.Navigated == @"C:\New",
                "new ShellBrowserWindow served by the registered rundll32 host is accepted and navigated");
            backend = new CreationBackend(); backend.ValidIdentity = false;
            Check(Throws(() => RunCreate(backend)) is InvalidOperationException && backend.Browser.Navigated == null, "recycled dedicated HWND rejected before navigation");
            backend = new CreationBackend(); backend.Browser.OnNavigate = () => backend.Browser.CurrentHandle = 11;
            Check(Throws(() => RunCreate(backend)) is InvalidOperationException && backend.Browser.Shows == 0, "changing dedicated handle never triggers a window search");
            backend = new CreationBackend(); backend.Browser.CurrentHandle = 0;
            backend.OnPause = () => backend.Browser.CurrentHandle = 10;
            Check(RunCreate(backend).Identity.Handle == 10 && backend.Pauses == 1, "delayed Shell handle is checked before show and attach");
            backend = new CreationBackend(); backend.Browser.OnNavigate = () => backend.Browser.Url = "";
            backend.OnPause = () => backend.Browser.Url = "file:///C:/New";
            Check(RunCreate(backend).Identity.Handle == 10 && backend.Pauses == 1, "initial Busy=false and blank location waits for asynchronous navigation");
            backend = new CreationBackend(); backend.Browser.IsBusy = true;
            backend.OnPause = () => backend.Browser.IsBusy = false;
            Check(RunCreate(backend).Directory == @"C:\New" && backend.Pauses == 1, "busy browser is not attached before navigation finishes");
            using (var cancel = new CancellationTokenSource())
            {
                cancel.Cancel(); backend = new CreationBackend();
                Check(Throws(() => RunCreate(backend, cancel.Token)) is OperationCanceledException && backend.Events.Count == 0, "pre-cancelled workflow touches neither source nor Shell");
            }
            foreach (string stage in new[] { "create", "navigate", "show", "pause" })
            using (var cancel = new CancellationTokenSource())
            {
                backend = new CreationBackend();
                if (stage == "create") backend.OnCreate = cancel.Cancel;
                if (stage == "navigate") backend.Browser.OnNavigate = cancel.Cancel;
                if (stage == "show") backend.Browser.OnShow = cancel.Cancel;
                if (stage == "pause") { backend.Browser.IsBusy = true; backend.OnPause = cancel.Cancel; }
                Check(Throws(() => RunCreate(backend, cancel.Token)) is OperationCanceledException && backend.Browser.Disposals == 1,
                    "cancellation after " + stage + " releases reference without a close or fallback");
                if (stage == "create") Check(backend.Browser.Navigated == null && backend.Browser.Shows == 0, "cancel after activation prevents late navigation and show");
                if (stage == "navigate") Check(backend.Browser.Shows == 0, "cancel after Navigate prevents late Show");
            }
            using (var cancel = new CancellationTokenSource())
            {
                backend = new CreationBackend(); backend.Browser.OnNavigate = () => backend.Browser.Url = "file:///C:/Wrong";
                backend.OnPause = cancel.Cancel;
                Check(Throws(() => RunCreate(backend, cancel.Token)) is OperationCanceledException && backend.Browser.Shows == 1,
                    "wrong final location is never attached and remains independent at timeout");
            }
            backend = new CreationBackend(); backend.Browser.OnShow = () => backend.ValidIdentity = false;
            Check(Throws(() => RunCreate(backend)) is InvalidOperationException && backend.Browser.Disposals == 1, "identity loss after Show prevents attachment without closing browser");
        }
        private static void CreationServiceTests()
        {
            int factories = 0; var service = new StaExplorerTabService(() => { Interlocked.Increment(ref factories); return new CreationBackend(); });
            using (var cancel = new CancellationTokenSource())
            {
                cancel.Cancel(); var task = service.CreateAsync(@"C:\New", null, Array.Empty<long>(), cancel.Token);
                Check(task.IsCanceled && factories == 0 && !service.IsBusy, "pre-cancelled service does not start a COM worker");
            }
            var result = service.CreateAsync(@"C:\New", null, Array.Empty<long>(), default).GetAwaiter().GetResult();
            SpinWait.SpinUntil(() => !service.IsBusy, 2000);
            Check(result.Identity.Handle == 10 && !service.IsBusy && factories == 1, "successful STA service clears gate after backend disposal");
            using (var entered = new ManualResetEventSlim())
            using (var release = new ManualResetEventSlim())
            using (var cancel = new CancellationTokenSource())
            {
                var backend = new CreationBackend(); ApartmentState state = ApartmentState.Unknown;
                backend.OnCreate = () => { state = Thread.CurrentThread.GetApartmentState(); entered.Set(); if (!release.Wait(3000)) throw new TimeoutException("test worker timed out"); };
                service = new StaExplorerTabService(() => backend);
                var task = service.CreateAsync(@"C:\New", null, Array.Empty<long>(), cancel.Token);
                try
                {
                    Check(entered.Wait(2000) && state == ApartmentState.STA && service.IsBusy, "Shell backend runs on a dedicated single-flight STA thread");
                    cancel.Cancel();
                    Check(task.IsCanceled && service.IsBusy && !release.IsSet, "UI cancellation completes immediately while stalled COM worker keeps busy gate");
                    Check(Throws(() => service.CreateAsync(@"C:\New", null, Array.Empty<long>(), default).GetAwaiter().GetResult()) is InvalidOperationException,
                        "cancelled but running COM request refuses unlimited additional workers");
                }
                finally { release.Set(); }
                Check(SpinWait.SpinUntil(() => !service.IsBusy, 2000) && backend.Disposals == 1 && backend.Browser.Navigated == null,
                    "late COM return disposes backend and never navigates after cancellation");
            }
            using (var entered = new ManualResetEventSlim())
            using (var release = new ManualResetEventSlim())
            {
                var backend = new CreationBackend(); var source = Identity(2); var handles = new System.Collections.Generic.List<long> { 2 };
                service = new StaExplorerTabService(() => { entered.Set(); if (!release.Wait(3000)) throw new TimeoutException("test worker timeout"); return backend; });
                var task = service.CreateAsync(@"C:\New", source, handles, default);
                try { Check(entered.Wait(2000), "snapshot test worker started"); source.Handle = 1; handles[0] = 10; }
                finally { release.Set(); }
                var created = task.GetAwaiter().GetResult();
                Check(backend.ReadSource!.Handle == 2 && created.Identity.Handle == 10,
                    "worker snapshots source identity and attached handles before caller model changes");
                Check(SpinWait.SpinUntil(() => !service.IsBusy, 2000), "snapshot worker exits before test releases coordination handles");
            }
            service = new StaExplorerTabService(() => new CreationBackend { FailDispose = true });
            Check(Throws(() => service.CreateAsync(@"C:\New", null, Array.Empty<long>(), default).GetAwaiter().GetResult()) is IOException,
                "backend disposal failure is not silently reported as creation success");
            Check(SpinWait.SpinUntil(() => !service.IsBusy, 2000), "disposal failure releases worker gate");
            service = new StaExplorerTabService(() => throw new IOException("fake factory failure"));
            Check(Throws(() => service.CreateAsync(@"C:\New", null, Array.Empty<long>(), default).GetAwaiter().GetResult()) is IOException,
                "STA factory exception is observed by caller");
            Check(SpinWait.SpinUntil(() => !service.IsBusy, 2000), "STA factory failure releases single-flight gate");
        }
        private sealed class CreationBackend : IExplorerCreationBackend
        {
            public readonly List<string> Events = new List<string>();
            public readonly CreationBrowser Browser;
            public string SourceDirectory = @"C:\Source";
            public WindowIdentity? ReadSource, Inspected;
            public bool Exists = true, ValidIdentity = true, ReadFailure, IsAtResult = true;
            public Action? OnCreate, OnPause;
            public int Pauses, Disposals; public bool FailDispose;
            public CreationBackend() { Browser = new CreationBrowser(Events); }
            public IReadOnlyCollection<long> CaptureExistingHandles() { Events.Add("snapshot"); return new long[] { 1 }; }
            public string ReadDirectory(WindowIdentity source)
            { ReadSource = source; if (ReadFailure) throw new InvalidOperationException("fake missing source"); return SourceDirectory; }
            public bool DirectoryExists(string directory) { Events.Add("exists"); return Exists; }
            public bool IsAt(ICreatedExplorerReference browser, string target) { Events.Add("isat:" + target); return IsAtResult; }
            public ICreatedExplorerReference Create() { Events.Add("create"); OnCreate?.Invoke(); return Browser; }
            public WindowIdentity Inspect(long handle) { Events.Add("inspect:" + handle); return Inspected ?? Identity(handle); }
            public bool Matches(WindowIdentity identity) => ValidIdentity;
            public void Pause(CancellationToken cancellation)
            { cancellation.ThrowIfCancellationRequested(); if (++Pauses > 100) throw new TimeoutException("test navigation guard"); OnPause?.Invoke(); cancellation.ThrowIfCancellationRequested(); }
            public void Dispose() { Disposals++; if (FailDispose) throw new IOException("fake dispose failure"); }
        }
        private sealed class CreationBrowser : ICreatedExplorerReference
        {
            private readonly List<string> events;
            public long CurrentHandle = 10; public bool IsBusy; public string Url = "file:///C:/New";
            public int Disposals, Shows; public string? Navigated; public Action? OnNavigate, OnShow;
            public CreationBrowser(List<string> events) { this.events = events; }
            public long Handle => CurrentHandle; public bool Busy => IsBusy; public string LocationUrl => Url;
            public void Navigate(string directory, CancellationToken cancellation) { cancellation.ThrowIfCancellationRequested(); events.Add("navigate:" + directory); Navigated = directory; Url = new Uri(directory).AbsoluteUri; OnNavigate?.Invoke(); }
            public void Show() { events.Add("show"); Shows++; OnShow?.Invoke(); }
            public void Dispose() { Disposals++; }
        }
    }
}
