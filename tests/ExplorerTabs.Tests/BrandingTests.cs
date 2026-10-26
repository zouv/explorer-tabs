using System;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Runtime.Serialization.Json;
using System.Threading;
using ExplorerTabs.Core;

namespace ExplorerTabs.Tests
{
    internal static partial class Program
    {
        private static int ProbeIdentityMutex(string user)
        {
            using (var mutex = new Mutex(false, ProductIdentity.GetMutexName(user)))
            {
                bool acquired;
                try { acquired = mutex.WaitOne(0); }
                catch (AbandonedMutexException) { acquired = true; }
                if (!acquired) return 0;
                mutex.ReleaseMutex();
                return 1;
            }
        }

        private static int RunIdentityProbe(string user)
        {
            using (var child = Process.Start(new ProcessStartInfo(Assembly.GetExecutingAssembly().Location, "--identity-probe " + user)
            { UseShellExecute = false, CreateNoWindow = true })!)
            {
                if (!child.WaitForExit(10000)) throw new TimeoutException("Synthetic identity probe did not finish.");
                return child.ExitCode;
            }
        }

        private static void BrandingUnitTests()
        {
            Check(ProductIdentity.Name == "ExplorerTabs", "public product name is ExplorerTabs");
            string root = Path.Combine(Path.GetTempPath(), "ExplorerTabs-branding-" + Guid.NewGuid().ToString("N"));
            string state = ProductIdentity.GetStateDirectory(root);
            Check(state == Path.Combine(root, "ExplorerTabHost"), "rename preserves the existing data directory");
            Check(!Directory.Exists(root), "identity resolution does not create or migrate data");
            Check(Throws(() => ProductIdentity.GetStateDirectory(" ")) is ArgumentException, "empty data root rejected");
            Check(Throws(() => ProductIdentity.GetMutexName("")) is ArgumentException, "empty mutex identity rejected");
            string user = "ExplorerTabs-test-" + Guid.NewGuid().ToString("N");
            string legacyMutex = @"Local\ExplorerTabHost." + user;
            Check(ProductIdentity.GetMutexName(user) == legacyMutex, "new and legacy versions use the same per-user mutex");
            Check(ProductIdentity.GetMutexName(user) != ProductIdentity.GetMutexName(user + "-other"), "different users retain separate identities");
            using (var oldHost = new Mutex(true, legacyMutex, out bool created))
            {
                Check(created, "mutex compatibility test uses its own synthetic identity");
                try { Check(RunIdentityProbe(user) == 0, "legacy owner excludes renamed host in a separate process"); }
                finally { oldHost.ReleaseMutex(); }
                Check(RunIdentityProbe(user) == 1, "renamed host can start after legacy owner releases mutex");
            }

            var app = Assembly.Load(ProductIdentity.Name);
            Check(app.GetName().Name == "ExplorerTabs", "application assembly is renamed");
            Check(typeof(AppSettings).Assembly.GetName().Name == "ExplorerTabs.Core", "Core assembly is renamed");
            Check(Assembly.GetExecutingAssembly().GetName().Name == "ExplorerTabs.Tests", "test runner assembly is renamed");
            Check(app.GetType("ExplorerTabs.App.MainForm", true) != null, "reflection resolves the renamed MainForm");
            Check(app.GetCustomAttribute<AssemblyProductAttribute>()?.Product == ProductIdentity.Name, "assembly product metadata matches branding");
            Check(app.GetReferencedAssemblies().All(a => !a.Name!.StartsWith("ExplorerTabHost", StringComparison.Ordinal)), "application has no stale assembly dependency");
            using (var icon = app.GetManifestResourceStream("ExplorerTabs.AppIcon.ico"))
                Check(icon != null && icon.Length > 0, "renamed embedded icon resource resolves");

            string fixtures = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "Fixtures", "v0.6.1");
            try
            {
                Directory.CreateDirectory(state);
                foreach (string name in new[] { "settings.json", "workspace.json", "recovery.json" })
                    File.Copy(Path.Combine(fixtures, name), Path.Combine(state, name));
                string settingsPath = Path.Combine(state, "settings.json");
                var settings = new AppSettingsStore(settingsPath).Read()!;
                Check(settings.Version == 1 && settings.Mode == HostingMode.Embedded, "legacy settings version and enum values remain readable");
                Check(settings.NewTabDirectory == @"C:\ExplorerTabs-fixtures\常用目录", "legacy Unicode default directory preserved");
                Check(settings.WrapTabs && settings.MaximumTabRows == 4 && settings.Maximized, "legacy UI preferences preserved");
                Check(settings.Bounds.Left == -100 && settings.Bounds.Top == 50 && settings.Bounds.Right == 1200 && settings.Bounds.Bottom == 850,
                    "legacy window bounds preserved");
                Check(settings.KeyBindings.Count == 22 && settings.KeyBindings.Any(k => k.Command == HostCommand.CloseTab && k.Chord == "Ctrl+W"),
                    "legacy shortcut commands and chords preserved");
                CheckLegacyRoundTrip(settingsPath, settings);

                string workspacePath = Path.Combine(state, "workspace.json");
                var workspace = new WorkspaceLibraryStore(workspacePath).Read()!;
                Check(workspace.Favorites.Single().Name == "工作目录" && workspace.Favorites.Single().Id == "aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa",
                    "legacy favorite name and identity preserved");
                Check(workspace.Groups.Single().Name == "工作组" && workspace.Groups.Single().Tabs.Count == 2, "legacy tab group preserved");
                Check(workspace.LastSession!.ActiveIndex == 1 && workspace.LastSession.Tabs[0].Alias == "项目", "legacy session selection and alias preserved");
                Check(workspace.LastSession.Tabs[1].Directory == @"\\fixture-server\share\资料", "legacy UNC target preserved");
                CheckLegacyRoundTrip(workspacePath, workspace);

                string recoveryPath = Path.Combine(state, "recovery.json");
                var recovery = new RecoveryJournal(recoveryPath).Read()!; // Read only; never recover synthetic windows.
                Check(recovery.HostProcessId == 12345 && recovery.HostStartedUtcTicks == 111111, "legacy recovery owner preserved");
                var snapshot = recovery.Windows.Single();
                Check(snapshot.Identity.Handle == 123 && snapshot.Identity.ProcessId == 456 && snapshot.Identity.ThreadId == 789 && snapshot.Identity.ProcessStartedUtcTicks == 222222,
                    "legacy recovery window identity preserved");
                Check(snapshot.Style == 0x00CF0000 && snapshot.ExtendedStyle == 0x00040000 && snapshot.HostHandle == 999 && snapshot.WasVisible,
                    "legacy restore styles and host handle preserved");
                Check(snapshot.Mode == HostingMode.Grouped && snapshot.Placement.Length == 44 && snapshot.Placement.ShowCommand == 3,
                    "legacy restore placement and mode preserved");
                CheckLegacyRoundTrip(recoveryPath, recovery);

                foreach (string name in new[] { "settings.json", "workspace.json", "recovery.json" })
                    Check(File.ReadAllBytes(Path.Combine(state, name)).SequenceEqual(File.ReadAllBytes(Path.Combine(fixtures, name))),
                        "legacy read does not modify " + name);
            }
            finally { if (Directory.Exists(root)) Directory.Delete(root, true); }
        }

        private static void CheckLegacyRoundTrip<T>(string fixturePath, T document)
        {
            using (var buffer = new MemoryStream())
            {
                new DataContractJsonSerializer(typeof(T)).WriteObject(buffer, document);
                Check(buffer.ToArray().SequenceEqual(File.ReadAllBytes(fixturePath)), "renamed contract emits byte-identical legacy " + Path.GetFileName(fixturePath));
            }
        }
    }
}
