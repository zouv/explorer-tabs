using System;
using System.Collections.Generic;
using System.Drawing;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using ExplorerTabHost.Core;
namespace ExplorerTabHost.Tests
{
    internal static partial class Program
    {
        private static CreatedExplorer DeferredRun(DeferredBackend backend, CancellationToken token = default, IReadOnlyCollection<long>? attached = null) =>
            ExplorerCreationWorkflow.Run(backend, @"C:\New", null, attached ?? Array.Empty<long>(), token);
        private static void DeferredCreationTests()
        {
            var backend = new DeferredBackend();
            using (var created = DeferredRun(backend))
            {
                Check(created.DeferredPresentation && backend.Browser.Hidden && backend.Inner.Browser.Shows == 0, "deferred workflow never deliberately presents an independent Explorer");
                Check(backend.Inner.Events.IndexOf("inspect:10") < backend.Inner.Events.IndexOf("defer"), "fresh HWND is identity-checked before first presentation mutation");
                Check(backend.Inner.Events.IndexOf("defer") < backend.Inner.Events.IndexOf(@"navigate:C:\New"), "verified fresh browser defers presentation before navigation");
                Check(backend.Browser.Defers >= 2, "navigation presentation is suppressed again after HWND validation");
            }
            Check(backend.Browser.Restores == 1 && !backend.Browser.Hidden, "unclaimed creation result restores independent visibility on disposal");
            backend = new DeferredBackend(); var result = DeferredRun(backend); result.MarkAttached(); result.Dispose(); result.Dispose();
            Check(backend.Browser.Restores == 0, "successful attachment transfers ownership and disarms standalone restoration");
            backend = new DeferredBackend(); result = DeferredRun(backend); result.Dispose(); result.Dispose();
            Check(backend.Browser.Restores == 1, "standalone cleanup is idempotent");
            foreach (bool existing in new[] { true, false })
            {
                backend = new DeferredBackend(); backend.Inner.ValidIdentity = existing;
                Check(Throws(() => DeferredRun(backend, attached: existing ? new long[] { 10 } : null)) is InvalidOperationException && backend.Browser.Defers == 0,
                    "existing or recycled HWND is never hidden");
            }
            using (var token = new CancellationTokenSource())
            {
                backend = new DeferredBackend(); backend.Inner.OnCreate = token.Cancel;
                Check(Throws(() => DeferredRun(backend, token.Token)) is OperationCanceledException && backend.Browser.Defers == 0, "cancelled activation does not defer or navigate the late browser");
            }
            foreach (bool fail in new[] { false, true })
            using (var token = new CancellationTokenSource())
            {
                backend = new DeferredBackend(); backend.Inner.Browser.OnNavigate = () => { if (fail) throw new IOException("fake navigation failure"); token.Cancel(); };
                Check(Throws(() => DeferredRun(backend, token.Token)) is Exception && backend.Browser.Restores == 1 && !backend.Browser.Hidden,
                    "navigation failure/cancellation restores the explicitly created browser");
            }
            using (var token = new CancellationTokenSource())
            {
                backend = new DeferredBackend(); backend.Inner.Browser.IsBusy = true; backend.Inner.OnPause = token.Cancel;
                Check(Throws(() => DeferredRun(backend, token.Token)) is OperationCanceledException && backend.Browser.Restores == 1, "timeout while loading restores visibility without a close/fallback launch");
            }
            backend = new DeferredBackend(); backend.Inner.Browser.OnNavigate = () => backend.Inner.Browser.CurrentHandle = 11;
            Check(Throws(() => DeferredRun(backend)) is InvalidOperationException && backend.Browser.Restores == 1, "changed creation handle rejects attachment and runs original-identity cleanup");
            backend = new DeferredBackend(); backend.Browser.FailDefer = true;
            Check(Throws(() => DeferredRun(backend)) is IOException && backend.Browser.Restores == 1 && !backend.Browser.Hidden, "presentation capability must undo mutation if SetVisible-equivalent throws");
            backend = new DeferredBackend(); backend.Browser.FailDispose = true;
            Check(Throws(() => DeferredRun(backend)) is IOException && backend.Browser.Restores == 1, "COM reference disposal failure cannot lose a prepared result's cleanup lease");
            backend = new DeferredBackend(); backend.Inner.FailDispose = true; var service = new StaExplorerTabService(() => backend);
            Check(Throws(() => service.CreateAsync(@"C:\New", null, Array.Empty<long>(), default).GetAwaiter().GetResult()) is IOException && backend.Browser.Restores == 1,
                "backend disposal failure restores unpublished deferred creation");
            Check(SpinWait.SpinUntil(() => !service.IsBusy, 2000), "failed deferred service releases single-flight gate after cleanup");
            using (var token = new CancellationTokenSource())
            {
                backend = new DeferredBackend(); service = new StaExplorerTabService(() => backend);
                result = service.CreateAsync(@"C:\New", null, Array.Empty<long>(), token.Token).GetAwaiter().GetResult();
                token.Cancel(); result.Dispose();
                Check(backend.Browser.Restores == 1 && !backend.Browser.Hidden, "cancellation after publication restores visibility even if the UI continuation never consumes the result");
                Check(SpinWait.SpinUntil(() => !service.IsBusy, 2000), "published deferred service releases gate");
            }
            using (var token = new CancellationTokenSource())
            {
                backend = new DeferredBackend(); service = new StaExplorerTabService(() => backend);
                result = service.CreateAsync(@"C:\New", null, Array.Empty<long>(), token.Token).GetAwaiter().GetResult(); result.MarkAttached(); token.Cancel(); result.Dispose();
                Check(backend.Browser.Restores == 0, "late cancellation after attach does not reveal grouped or embedded Explorer");
                Check(SpinWait.SpinUntil(() => !service.IsBusy, 2000), "claimed deferred service releases gate");
            }
            using (var token = new CancellationTokenSource())
            {
                backend = new DeferredBackend(); result = DeferredRun(backend); token.Cancel(); result.BindCancellation(token.Token); result.Dispose();
                Check(backend.Browser.Restores == 1, "already-cancelled registration restores once and cannot retain a completed lease");
            }
            using (var token = new CancellationTokenSource())
            {
                backend = new DeferredBackend(); result = DeferredRun(backend); result.Dispose(); result.BindCancellation(token.Token); token.Cancel();
                Check(backend.Browser.Restores == 1, "binding after disposal unregisters rather than retaining a stale cancellation callback");
            }
            bool racesSafe = true;
            for (int i = 0; i < 64; i++)
            using (var token = new CancellationTokenSource())
            {
                int restores = 0;
                using (var racing = new CreatedExplorer(Identity(10), @"C:\New", () => Interlocked.Increment(ref restores)))
                {
                    Task.WaitAll(Task.Run(() => racing.BindCancellation(token.Token)), Task.Run(() => { token.Cancel(); racing.Dispose(); }));
                    racing.Dispose(); racesSafe &= restores == 1;
                }
            }
            Check(racesSafe, "concurrent bind/cancel/dispose has exactly one restoration and no registration deadlock");
            var api = new FakeWindows(); var store = new FakeStore(); var host = new HostController(api, store);
            var session = host.Attach(Identity(1), new IntPtr(100), HostingMode.Embedded, Bounds, true);
            Check(session.Snapshot.WasVisible && session.Snapshot.Placement.ShowCommand != 0 && store.Snapshots.Single().WasVisible,
                "fresh hidden Explorer's durable attach snapshot records visible standalone restoration intent");
            host.DetachAll(); Check(api.Events.Contains("restore:1"), "created-visible snapshot uses the existing detach/recovery path");
        }
        private sealed class DeferredBackend : IExplorerCreationBackend
        {
            public readonly CreationBackend Inner = new CreationBackend(); public readonly DeferredBrowser Browser;
            public DeferredBackend() { Browser = new DeferredBrowser(Inner); }
            public IReadOnlyCollection<long> CaptureExistingHandles() => Inner.CaptureExistingHandles();
            public string ReadDirectory(WindowIdentity identity) => Inner.ReadDirectory(identity);
            public bool DirectoryExists(string directory) => Inner.DirectoryExists(directory);
            public ICreatedExplorerReference Create() { Inner.Create(); return Browser; }
            public WindowIdentity Inspect(long handle) => Inner.Inspect(handle);
            public bool Matches(WindowIdentity identity) => Inner.Matches(identity);
            public void Pause(CancellationToken token) => Inner.Pause(token);
            public void Dispose() => Inner.Dispose();
        }
        private sealed class DeferredBrowser : ICreatedExplorerReference, IDeferredExplorerReference
        {
            private readonly CreationBackend backend;
            public int Defers, Restores; public bool Hidden, FailDefer, FailDispose;
            public DeferredBrowser(CreationBackend backend) { this.backend = backend; }
            public long Handle => backend.Browser.Handle;
            public bool Busy => backend.Browser.Busy;
            public string LocationUrl => backend.Browser.LocationUrl;
            public void Navigate(string directory, CancellationToken token) => backend.Browser.Navigate(directory, token);
            public void Show() => backend.Browser.Show();
            public Action DeferPresentation(WindowIdentity identity)
            {
                Defers++; Hidden = true; backend.Events.Add("defer");
                Action restore = () => { Restores++; Hidden = false; };
                if (FailDefer) { restore(); throw new IOException("fake mutating defer failure"); }
                return restore;
            }
            public void Dispose() { backend.Browser.Dispose(); if (FailDispose) throw new IOException("fake COM release failure"); }
        }
    }
}
