using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

namespace ExplorerTabs.Core
{
    public sealed class CreatedExplorer : IDisposable
    {
        public WindowIdentity Identity { get; }
        public string Directory { get; }
        private readonly object presentationGate = new object();
        private Action? restoreIndependent;
        private CancellationTokenRegistration cancellationRegistration;
        private bool completed;
        private int cancellationBound;
        public bool DeferredPresentation { get; }
        public CreatedExplorer(WindowIdentity identity, string directory, Action? restoreIndependent = null)
        { Identity = identity; Directory = directory; this.restoreIndependent = restoreIndependent; DeferredPresentation = restoreIndependent != null; }
        public void BindCancellation(CancellationToken token)
        {
            if (Interlocked.Exchange(ref cancellationBound, 1) != 0) throw new InvalidOperationException("Creation cancellation is already bound.");
            // Register may synchronously invoke Dispose for an already-cancelled token.
            // Publish only while the lease is live; dispose outside the gate to avoid waiting
            // for a cancellation callback that is itself waiting for this gate.
            var registration = token.Register(Dispose);
            bool release;
            lock (presentationGate)
            {
                release = completed;
                if (!release) cancellationRegistration = registration;
            }
            if (release) registration.Dispose();
        }
        public void MarkAttached()
        {
            CancellationTokenRegistration registration;
            lock (presentationGate)
            {
                completed = true; restoreIndependent = null;
                registration = cancellationRegistration; cancellationRegistration = default;
            }
            registration.Dispose();
        }
        public void Dispose()
        {
            Action? restore;
            CancellationTokenRegistration registration;
            lock (presentationGate)
            {
                if (completed) return;
                completed = true; restore = restoreIndependent; restoreIndependent = null;
                registration = cancellationRegistration; cancellationRegistration = default;
            }
            try { restore?.Invoke(); }
            finally { registration.Dispose(); }
        }
    }
    public interface IExplorerTabService
    {
        bool IsBusy { get; }
        Task<CreatedExplorer> CreateAsync(string defaultDirectory, WindowIdentity? source,
            IReadOnlyCollection<long> attachedHandles, CancellationToken cancellation);
    }
    public interface IExplorerDirectoryService
    {
        bool IsReading { get; }
        Task<string> ReadDirectoryAsync(WindowIdentity identity, CancellationToken cancellation);
    }
    // Each reference comes from ONE explicit ShellBrowserWindow activation, never a window search.
    public interface ICreatedExplorerReference : IDisposable
    {
        long Handle { get; }
        void Navigate(string directory, CancellationToken cancellation);
        void Show();
        bool Busy { get; }
        string LocationUrl { get; }
    }
    // Optional capability: only the explicitly created, verified browser may be hidden.
    // The returned native-only callback can outlive the COM STA and is idempotent through CreatedExplorer.
    public interface IDeferredExplorerReference
    {
        Action DeferPresentation(WindowIdentity identity);
    }
    public interface IExplorerCreationBackend : IDisposable
    {
        IReadOnlyCollection<long> CaptureExistingHandles();
        string ReadDirectory(WindowIdentity source);
        bool DirectoryExists(string directory);
        /// <summary>Exact target check for virtual shell folders (currently This PC).</summary>
        bool IsAt(ICreatedExplorerReference browser, string target);
        ICreatedExplorerReference Create();
        WindowIdentity Inspect(long handle);
        bool Matches(WindowIdentity identity);
        void Pause(CancellationToken cancellation);
    }
    public static class ExplorerCreationWorkflow
    {
        public static CreatedExplorer Run(IExplorerCreationBackend backend, string defaultDirectory,
            WindowIdentity? source, IReadOnlyCollection<long> attachedHandles, CancellationToken token)
        {
            token.ThrowIfCancellationRequested();
            string directory = FolderTarget.Normalize(source == null ? defaultDirectory : backend.ReadDirectory(source));
            token.ThrowIfCancellationRequested();
            if (!FolderTarget.IsThisPc(directory) && !backend.DirectoryExists(directory)) throw new InvalidOperationException("文件夹不存在或无法访问：" + directory);
            token.ThrowIfCancellationRequested();
            var excluded = new HashSet<long>(backend.CaptureExistingHandles().Concat(attachedHandles));
            token.ThrowIfCancellationRequested();
            CreatedExplorer? pending = null;
            try
            {
                using (var browser = backend.Create())
                {
                    token.ThrowIfCancellationRequested();
                    // CoCreateInstance creates a fresh browser. Reject any already-assigned existing HWND BEFORE navigation.
                    long initial = browser.Handle;
                    if (initial != 0 && excluded.Contains(initial)) throw new InvalidOperationException("Shell 返回了已有窗口，已停止操作，不导航或接管该窗口。");
                    WindowIdentity? identity = initial == 0 ? null : Verify(backend, initial, excluded);
                    Action? restore = null;
                    bool transferred = false;
                    try
                    {
                        if (identity != null && browser is IDeferredExplorerReference early) restore = early.DeferPresentation(identity);
                        token.ThrowIfCancellationRequested();
                        browser.Navigate(directory, token);
                        token.ThrowIfCancellationRequested();
                        long handle = browser.Handle;
                        while (handle == 0) { backend.Pause(token); token.ThrowIfCancellationRequested(); handle = browser.Handle; }
                        if (identity != null && identity.Handle != handle) throw new InvalidOperationException("新建窗口句柄发生变化，保留独立窗口，不尝试匹配其他窗口。");
                        identity = identity ?? Verify(backend, handle, excluded);
                        token.ThrowIfCancellationRequested();
                        if (browser is IDeferredExplorerReference deferred)
                        { if (restore == null) restore = deferred.DeferPresentation(identity); else deferred.DeferPresentation(identity); }
                        else browser.Show(); // Compatibility for backends without deferred presentation.
                        while (true)
                        {
                            token.ThrowIfCancellationRequested();
                            if (browser.Handle != identity.Handle || !backend.Matches(identity))
                                throw new InvalidOperationException("新建窗口已关闭或身份改变，未接管其他窗口。");
                            if (!browser.Busy)
                            {
                                bool reached;
                                if (FolderTarget.IsThisPc(directory))
                                {
                                    // Virtual shell folders have no stable file LocationURL; compare the
                                    // active view's current PIDL with the requested namespace instead.
                                    reached = backend.IsAt(browser, directory);
                                }
                                else
                                {
                                    // Navigate2 is asynchronous: Busy may initially be false before LocationURL changes.
                                    string url = browser.LocationUrl;
                                    string? actual = null;
                                    try { actual = FolderTarget.FromLocationUrl(url); }
                                    catch (InvalidOperationException) { } // Initial blank/virtual location: await the requested folder.
                                    reached = actual != null && FolderTarget.Same(directory, actual);
                                }
                                if (reached)
                                {
                                    token.ThrowIfCancellationRequested();
                                    var result = new CreatedExplorer(identity, directory, restore);
                                    pending = result;
                                    transferred = true;
                                    return result;
                                }
                            }
                            backend.Pause(token);
                        }
                    }
                    finally { if (!transferred) restore?.Invoke(); }
                }
            }
            catch { pending?.Dispose(); throw; }
        }
        private static WindowIdentity Verify(IExplorerCreationBackend backend, long handle, HashSet<long> excluded)
        {
            if (handle == 0 || excluded.Contains(handle)) throw new InvalidOperationException("窗口不是本次新建，未接管。");
            var identity = backend.Inspect(handle);
            if (!NativeWindowApi.IsExplorer(identity) || identity.Handle != handle || !backend.Matches(identity))
                throw new InvalidOperationException("新建窗口不是可确认的 Explorer，未接管。");
            return identity;
        }
    }
    /// <summary>UI cancellation completes promptly even if an out-of-process COM call stalls.
    /// The single-flight gate stays held until that worker really exits; no thread abort or unlimited new workers.</summary>
    public sealed class StaExplorerTabService : IExplorerTabService, IExplorerDirectoryService
    {
        private readonly Func<IExplorerCreationBackend> factory;
        private int busy, reading;
        public bool IsReading => Volatile.Read(ref reading) != 0;
        public Task<string> ReadDirectoryAsync(WindowIdentity identity, CancellationToken cancellation)
        {
            if (cancellation.IsCancellationRequested) return Task.FromCanceled<string>(cancellation);
            if (Interlocked.CompareExchange(ref reading, 1, 0) != 0)
                return Task.FromException<string>(new InvalidOperationException("上一次目录读取尚未结束，请稍后重试。"));
            var copy = new WindowIdentity { Handle = identity.Handle, ProcessId = identity.ProcessId, ThreadId = identity.ThreadId,
                ProcessStartedUtcTicks = identity.ProcessStartedUtcTicks, ImagePath = identity.ImagePath, ClassName = identity.ClassName, Title = identity.Title };
            var result = new TaskCompletionSource<string>(TaskCreationOptions.RunContinuationsAsynchronously);
            var registration = cancellation.Register(() => result.TrySetCanceled());
            var thread = new Thread(() =>
            {
                try
                {
                    cancellation.ThrowIfCancellationRequested();
                    string path;
                    using (var backend = factory())
                    {
                        if (!backend.Matches(copy)) throw new InvalidOperationException("目录读取目标已变化。");
                        path = FolderTarget.Normalize(backend.ReadDirectory(copy));
                        if (!backend.Matches(copy)) throw new InvalidOperationException("目录读取后目标已变化。");
                    }
                    cancellation.ThrowIfCancellationRequested(); result.TrySetResult(path);
                }
                catch (OperationCanceledException) { result.TrySetCanceled(); }
                catch (Exception error) { result.TrySetException(error); }
                finally { registration.Dispose(); Volatile.Write(ref reading, 0); }
            }) { IsBackground = true, Name = "ExplorerTabs read-only directory" };
            try { thread.SetApartmentState(ApartmentState.STA); thread.Start(); }
            catch (Exception error) { registration.Dispose(); Volatile.Write(ref reading, 0); result.TrySetException(error); }
            return result.Task;
        }
        public bool IsBusy => Volatile.Read(ref busy) != 0;
        public StaExplorerTabService(Func<IExplorerCreationBackend> factory) { this.factory = factory; }
        public Task<CreatedExplorer> CreateAsync(string directory, WindowIdentity? source,
            IReadOnlyCollection<long> attachedHandles, CancellationToken cancellation)
        {
            if (cancellation.IsCancellationRequested) return Task.FromCanceled<CreatedExplorer>(cancellation);
            var handles = attachedHandles.ToArray();
            // Caller models are mutable; do not let navigation or pruning change the worker's identity snapshot.
            var sourceCopy = source == null ? null : new WindowIdentity { Handle = source.Handle, ProcessId = source.ProcessId,
                ThreadId = source.ThreadId, ProcessStartedUtcTicks = source.ProcessStartedUtcTicks, ImagePath = source.ImagePath,
                ClassName = source.ClassName, Title = source.Title };
            if (Interlocked.CompareExchange(ref busy, 1, 0) != 0)
                return Task.FromException<CreatedExplorer>(new InvalidOperationException("上一次 Shell 调用尚未结束，请稍后重试。"));
            var result = new TaskCompletionSource<CreatedExplorer>(TaskCreationOptions.RunContinuationsAsynchronously);
            var registration = cancellation.Register(() => result.TrySetCanceled());
            var thread = new Thread(() =>
            {
                try
                {
                    cancellation.ThrowIfCancellationRequested();
                    CreatedExplorer? created = null;
                    bool delivered = false;
                    try
                    {
                        using (var backend = factory()) created = ExplorerCreationWorkflow.Run(backend, directory, sourceCopy, handles, cancellation);
                        created.BindCancellation(cancellation);
                        cancellation.ThrowIfCancellationRequested();
                        delivered = result.TrySetResult(created);
                    }
                    finally { if (!delivered) created?.Dispose(); }
                }
                catch (OperationCanceledException) { result.TrySetCanceled(); }
                catch (Exception error) { result.TrySetException(error); }
                finally { registration.Dispose(); Volatile.Write(ref busy, 0); }
            }) { IsBackground = true, Name = "ExplorerTabs explicit Shell creation" };
            try { thread.SetApartmentState(ApartmentState.STA); thread.Start(); }
            catch (Exception error) { registration.Dispose(); Volatile.Write(ref busy, 0); result.TrySetException(error); }
            return result.Task;
        }
    }
}
