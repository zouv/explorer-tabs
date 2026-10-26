using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using ExplorerTabHost.Core;
namespace ExplorerTabHost.Tests
{
    internal static partial class Program
    {
        private static SavedWorkspace SampleWorkspace() => new SavedWorkspace { Name = "工作资料", ActiveIndex = 1, Tabs = new List<WorkspaceTab> {
            new WorkspaceTab { Directory = @"C:\资料", Alias = "资料" }, new WorkspaceTab { Directory = @"D:\项目", Alias = "项目 & 开发" } } };
        private static void WorkspaceUnitTests()
        {
            var sample = new WorkspaceLibrary { LastSession = SampleWorkspace(), Groups = new List<SavedWorkspace> { SampleWorkspace() }, Favorites = new List<FolderBookmark> { new FolderBookmark { Name = "常用资料", Directory = @"C:\资料" } } };
            WorkspaceLibrary.Validate(sample); var copy = sample.Copy(); copy.LastSession!.Tabs[0].Alias = "修改"; copy.Groups[0].Tabs[0].Directory = @"C:\另一目录"; copy.Favorites[0].Name = "修改";
            Check(sample.LastSession!.Tabs[0].Alias == "资料" && sample.Groups[0].Tabs[0].Directory == @"C:\资料" && sample.Favorites[0].Name == "常用资料", "workspace library deeply copies session, groups and favorites");
            Check(WorkspaceLibrary.SameSession(sample.LastSession, sample.LastSession.Copy()) && !WorkspaceLibrary.SameSession(sample.LastSession, copy.LastSession), "session content comparison ignores generated ID but detects aliases");
            Check(WorkspaceLibrary.SameSession(null, null) && !WorkspaceLibrary.SameSession(null, sample.LastSession), "empty session comparison is explicit");
            foreach (var mutate in new Action<WorkspaceLibrary>[] {
                w => w.Version = 9, w => w.LastSession!.ActiveIndex = 2, w => w.LastSession!.Tabs.Clear(), w => w.LastSession!.Id = "invalid",
                w => w.LastSession!.Tabs[0].Directory = "shell:Desktop", w => w.LastSession!.Tabs[0].Alias = "", w => w.Groups.Add(w.Groups[0].Copy()),
                w => w.Favorites.Add(w.Favorites[0].Copy()), w => w.Groups[0].Name = "", w => w.Favorites[0].Directory = "https://invalid/",
                w => w.LastSession!.Tabs = Enumerable.Range(0, 101).Select(_ => new WorkspaceTab { Directory = @"C:\Temp" }).ToList(),
                w => w.Favorites = Enumerable.Range(0, 101).Select(_ => new FolderBookmark { Name = "目录", Directory = @"C:\Temp" }).ToList() })
            { var invalid = sample.Copy(); mutate(invalid); Throws(() => WorkspaceLibrary.Validate(invalid)); Check(true, "invalid workspace rejected without native actions"); }
            var directory = Path.Combine(Path.GetTempPath(), "ExplorerTabHost-workspace-unit-" + Guid.NewGuid().ToString("N")); Directory.CreateDirectory(directory);
            try
            {
                var path = Path.Combine(directory, "workspace.json"); var store = new WorkspaceLibraryStore(path);
                Check(store.Read() == null, "missing workspace does not fabricate saved tabs"); store.Save(sample); var loaded = store.Read()!;
                Check(WorkspaceLibrary.SameSession(sample.LastSession, loaded.LastSession) && loaded.Groups[0].Name == "工作资料" && loaded.Favorites[0].Name == "常用资料", "workspace roundtrip preserves Unicode, order, alias and active index");
                store.Save(copy); Check(store.Read()!.LastSession!.Tabs[0].Alias == "修改" && Directory.GetFiles(directory, "*.tmp").Length == 0, "atomic workspace replacement cleans temporary file");
                var prior = File.ReadAllBytes(path); var invalid = sample.Copy(); invalid.LastSession!.ActiveIndex = -1; Throws(() => store.Save(invalid));
                Check(prior.SequenceEqual(File.ReadAllBytes(path)), "invalid save preserves prior workspace bytes");
                File.WriteAllText(path, "{bad json"); Throws(() => store.Read()); Check(File.ReadAllText(path) == "{bad json", "malformed workspace file retained for diagnosis");
                File.WriteAllBytes(path, new byte[1024 * 1024 + 1]); Throws(() => store.Read()); Check(new FileInfo(path).Length == 1024 * 1024 + 1, "oversized workspace rejected without rewriting");
                var settingsPath = Path.Combine(directory, "settings.json"); File.WriteAllText(settingsPath, "{\"Version\":1,\"Mode\":0,\"ShortcutsEnabled\":true,\"NewTabDirectory\":\"C:\\\\Temp\"}");
                var old = new AppSettingsStore(settingsPath).Read()!;
                Check(!old.WrapTabs && old.MaximumTabRows == 3 && old.RememberSession && old.KeyBindings.Count == 22, "0.4 settings gain safe 0.5 defaults when new fields are missing");
                old.WrapTabs = true; old.MaximumTabRows = 5; old.KeyBindings[0].Chord = ""; new AppSettingsStore(settingsPath).Save(old);
                var reread = new AppSettingsStore(settingsPath).Read()!;
                Check(reread.WrapTabs && reread.MaximumTabRows == 5 && reread.KeyBindings[0].Chord == "", "wrap and disabled shortcuts persist"); var settingsCopy = reread.Copy(); settingsCopy.KeyBindings[1].Chord = "";
                Check(reread.KeyBindings[1].Chord != "", "settings bindings copy is independent");
            }
            finally { foreach (var path in Directory.GetFiles(directory)) File.Delete(path); Directory.Delete(directory); }
            Check(TabSearchPolicy.Matches("开发 & 资料", @"D:\Project\Assets", "资料 assets") && !TabSearchPolicy.Matches("资料", @"C:\Temp", "资料 missing"), "search combines Unicode title and case-insensitive directory tokens with AND");
            Check(TabSearchPolicy.Matches("任意", "", "  \t ") && TabLayoutPolicy.Rows(100, 700, 216) == 34, "empty search and overflow row calculations");
            var bindings = HostShortcutCatalog.Defaults(); HostShortcutCatalog.Validate(bindings);
            foreach (var binding in bindings)
            {
                var policy = new HostShortcutPolicy(); int key = HostShortcutCatalog.Key(binding.Chord), modifiers = HostShortcutCatalog.Modifiers(binding.Chord);
                var press = policy.Handle(key, true, false, modifiers, true, true, true, true, bindings);
                Check(press.Suppress && press.Command == binding.Command, "default command routes: " + binding.Chord);
                Check(policy.Handle(key, true, false, modifiers, true, true, true, true, bindings).Command == HostCommand.None && policy.Handle(key, false, false, 0, false, false, false, false, bindings).Suppress, "repeat and release paired: " + binding.Chord);
            }
            var shortcuts = bindings.Select(b => b.Copy()).ToList(); shortcuts[1].Chord = shortcuts[0].Chord; Throws(() => HostShortcutCatalog.Validate(shortcuts));
            Check(true, "duplicate shortcut cannot save"); shortcuts = bindings.Select(b => b.Copy()).ToList(); shortcuts.RemoveAt(0); Throws(() => HostShortcutCatalog.Validate(shortcuts)); Check(true, "missing command cannot save");
            foreach (var scenario in new[] { "foreign", "injected", "win", "busy", "inactive", "switch" })
            {
                var p = new HostShortcutPolicy(); string chord = scenario == "switch" ? "Ctrl+Tab" : scenario == "busy" ? "Ctrl+T" : "Ctrl+W";
                var decision = p.Handle(HostShortcutCatalog.Key(chord), true, scenario == "injected", 1 | (scenario == "win" ? 8 : 0), scenario != "foreign", scenario != "busy", scenario != "switch", scenario != "inactive", bindings);
                Check(!decision.Suppress && decision.Command == HostCommand.None, "shortcut leaves unrelated scope untouched: " + scenario);
            }
            var held = new HostShortcutPolicy(new[] { (int)'W' }); Check(!held.Handle('W', true, false, 1, true, true, true, true, bindings).Suppress, "held key at hook startup is not mistaken for fresh close");
            held.Handle('W', false, false, 0, true, true, true, true, bindings); Check(held.Handle('W', true, false, 1, true, true, true, true, bindings).Suppress, "fresh press after startup release works");
            var disabled = bindings.Select(b => b.Copy()).ToList(); disabled.Single(b => b.Command == HostCommand.CloseTab).Chord = ""; HostShortcutCatalog.Validate(disabled);
            Check(!new HostShortcutPolicy().Handle('W', true, false, 1, true, true, true, true, disabled).Suppress, "disabled close leaves Explorer Ctrl+W untouched");
            var api = new FakeWindows(); var journal = new FakeStore(); var closer = new TestCloser(); var host = new HostController(api, journal, closer); var first = Add(host, 1); var second = Add(host, 2); host.Activate(first, Bounds); api.Events.Clear();
            journal.Fail = true; Throws(() => host.RequestClose(first)); Check(closer.Calls.Count == 0 && first.CloseRequestedUtc == null, "close journal failure sends no WM_CLOSE"); journal.Fail = false;
            closer.Fail = true; Throws(() => host.RequestClose(first)); Check(first.CloseRequestedUtc == null && host.Sessions.Count == 2, "failed close request retains managed target and journal"); closer.Fail = false; closer.Calls.Clear();
            host.RequestClose(first); host.RequestClose(first);
            Check(closer.Calls.SequenceEqual(new long[] { 1 }) && journal.Snapshots.Count == 2 && host.Sessions.Count == 2 && api.Events.Count == 0, "close sends once to exact target; no restore, hide, process kill or early prune");
            Check(host.PruneLost().Count == 0 && host.Sessions.Contains(first), "refused close keeps tab and recovery entry");
            host.RetryClose(first); host.RequestClose(first); Check(closer.Calls.Count == 2, "explicit retry may send close again");
            api.Alive.Remove(1); journal.Fail = true; Check(host.PruneLost().Count == 1 && host.Sessions.Contains(first) && api.Events.Count == 0, "dead close target with journal failure remains retryable without restore");
            journal.Fail = false; host.PruneLost(); Check(!host.Sessions.Contains(first) && host.Sessions.Single() == second && journal.Snapshots.Single().Identity.Handle == 2 && !api.Events.Any(e => e.StartsWith("restore")), "dead target prune cleans only target journal without restoring other windows");
            Throws(() => host.RequestClose(first)); Check(closer.Calls.Count == 2, "foreign/removed session cannot close"); api.Alive.Remove(2); Throws(() => host.RequestClose(second)); Check(closer.Calls.Count == 2, "lost identity cannot close");
            var rebound = bindings.Select(b => b.Copy()).ToList(); rebound.Single(b => b.Command == HostCommand.CloseTab).Chord = "Ctrl+Alt+W"; HostShortcutCatalog.Validate(rebound);
            var rebindPolicy = new HostShortcutPolicy(); Check(!rebindPolicy.Handle('W', true, false, 1, true, true, true, true, rebound).Suppress, "old close chord is released after rebinding"); rebindPolicy.Handle('W', false, false, 0, true, true, true, true, rebound);
            Check(rebindPolicy.Handle('W', true, false, 5, true, true, true, true, rebound).Command == HostCommand.CloseTab, "new user-selected chord routes correct command");
            Check(!rebindPolicy.Handle('W', false, true, 0, false, false, false, false, rebound).Suppress && rebindPolicy.Handle('W', false, false, 0, false, false, false, false, rebound).Suppress, "injected release does not steal real consumed key pair");
            var notExplorer = Identity(long.MaxValue); notExplorer.ClassName = "NotExplorer"; Throws(() => new NativeWindowCloser().RequestClose(notExplorer)); Check(true, "native closer refuses non-Explorer without posting message");
            Throws(() => new NativeWindowCloser().RequestClose(Identity(long.MaxValue))); Check(true, "native closer refuses unresolvable identity without posting message");
            DirectoryReadTests();
        }
        private static void DirectoryReadTests()
        {
            using (var entered = new ManualResetEventSlim()) using (var release = new ManualResetEventSlim())
            {
                var backend = new ReadBackend { Entered = entered, Release = release }; var factoryCalls = 0; var service = new StaExplorerTabService(() => { Interlocked.Increment(ref factoryCalls); return backend; });
                var identity = Identity(1); using (var cancel = new CancellationTokenSource())
                {
                    var task = service.ReadDirectoryAsync(identity, cancel.Token); Check(entered.Wait(2000), "directory read entered dedicated fake STA backend"); identity.Handle = 99;
                    cancel.Cancel(); Check(SpinWait.SpinUntil(() => task.IsCompleted, 1000) && task.IsCanceled && service.IsReading, "stalled read cancels promptly but holds worker gate");
                    Check(service.ReadDirectoryAsync(Identity(2), CancellationToken.None).IsFaulted && factoryCalls == 1 && backend.Creates == 0, "busy directory read creates neither second worker nor Shell browser");
                    release.Set(); Check(SpinWait.SpinUntil(() => !service.IsReading, 2000) && backend.SourceHandle == 1, "directory worker releases gate and uses copied identity");
                }
            }
            var changed = new ReadBackend { ChangeAfterRead = true }; var worker = new StaExplorerTabService(() => changed); Throws(() => worker.ReadDirectoryAsync(Identity(1), CancellationToken.None).GetAwaiter().GetResult());
            Check(changed.MatchesCalls == 2 && changed.Creates == 0, "directory identity revalidated after read with no create fallback");
        }
        private sealed class TestCloser : IWindowCloser
        { public bool Fail; public readonly List<long> Calls = new List<long>(); public void RequestClose(WindowIdentity identity) { Calls.Add(identity.Handle); if (Fail) throw new IOException("fake close failed"); } }
        private sealed class ReadBackend : IExplorerCreationBackend
        {
            public ManualResetEventSlim? Entered, Release; public long SourceHandle; public int Creates, MatchesCalls; public bool ChangeAfterRead;
            public bool Matches(WindowIdentity identity) { MatchesCalls++; return !ChangeAfterRead || MatchesCalls == 1; }
            public string ReadDirectory(WindowIdentity identity) { SourceHandle = identity.Handle; Entered?.Set(); Release?.Wait(); return @"C:\资料"; }
            public IReadOnlyCollection<long> CaptureExistingHandles() => Array.Empty<long>();
            public bool DirectoryExists(string directory) => true;
            public ICreatedExplorerReference Create() { Creates++; throw new InvalidOperationException("must not create"); }
            public WindowIdentity Inspect(long handle) => Identity(handle);
            public void Pause(CancellationToken token) => token.ThrowIfCancellationRequested(); public void Dispose() { }
        }
    }
}
