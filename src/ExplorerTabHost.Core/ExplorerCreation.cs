using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

namespace ExplorerTabHost.Core
{
    public sealed class CreatedExplorer
    {
        public WindowIdentity Identity { get; }
        public string Directory { get; }
        public CreatedExplorer(WindowIdentity identity, string directory) { Identity = identity; Directory = directory; }
    }
    public interface IExplorerTabService
    {
        bool IsBusy { get; }
        Task<CreatedExplorer> CreateAsync(string defaultDirectory, WindowIdentity? source,
            IReadOnlyCollection<long> attachedHandles, CancellationToken cancellation);
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
    public interface IExplorerCreationBackend : IDisposable
    {
        IReadOnlyCollection<long> CaptureExistingHandles();
        string ReadDirectory(WindowIdentity source);
        bool DirectoryExists(string directory);
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
            if (!backend.DirectoryExists(directory)) throw new InvalidOperationException("文件夹不存在或无法访问：" + directory);
            token.ThrowIfCancellationRequested();
            var excluded = new HashSet<long>(backend.CaptureExistingHandles().Concat(attachedHandles));
            token.ThrowIfCancellationRequested();
            using (var browser = backend.Create())
            {
                token.ThrowIfCancellationRequested();
                // CoCreateInstance creates a fresh browser. Reject any already-assigned existing HWND BEFORE navigation.
                long initial = browser.Handle;
                if (initial != 0 && excluded.Contains(initial)) throw new InvalidOperationException("Shell 返回了已有窗口，已停止操作，不导航或接管该窗口。");
                WindowIdentity? identity = initial == 0 ? null : Verify(backend, initial, excluded);
                token.ThrowIfCancellationRequested();
                browser.Navigate(directory, token);
                token.ThrowIfCancellationRequested();
                long handle = browser.Handle;
                while (handle == 0) { backend.Pause(token); token.ThrowIfCancellationRequested(); handle = browser.Handle; }
                if (identity != null && identity.Handle != handle) throw new InvalidOperationException("新建窗口句柄发生变化，保留独立窗口，不尝试匹配其他窗口。");
                identity = identity ?? Verify(backend, handle, excluded);
                token.ThrowIfCancellationRequested();
                browser.Show(); // New window stays independent if loading/attachment later fails. No Quit or fallback launch.
                while (true)
                {
                    token.ThrowIfCancellationRequested();
                    if (browser.Handle != identity.Handle || !backend.Matches(identity))
                        throw new InvalidOperationException("新建窗口已关闭或身份改变，未接管其他窗口。");
                    if (!browser.Busy)
                    {
                        // Navigate2 is asynchronous: Busy may initially be false before LocationURL changes.
                        string url = browser.LocationUrl;
                        string? actual = null;
                        try { actual = FolderTarget.FromLocationUrl(url); }
                        catch (InvalidOperationException) { } // Initial blank/virtual location: await the requested folder.
                        if (actual != null && FolderTarget.Same(directory, actual))
                        {
                            token.ThrowIfCancellationRequested();
                            return new CreatedExplorer(identity, directory);
                        }
                    }
                    backend.Pause(token);
                }
            }
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
    public sealed class StaExplorerTabService : IExplorerTabService
    {
        private readonly Func<IExplorerCreationBackend> factory;
        private int busy;
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
                    CreatedExplorer created;
                    using (var backend = factory()) created = ExplorerCreationWorkflow.Run(backend, directory, sourceCopy, handles, cancellation);
                    cancellation.ThrowIfCancellationRequested();
                    result.TrySetResult(created);
                }
                catch (OperationCanceledException) { result.TrySetCanceled(); }
                catch (Exception error) { result.TrySetException(error); }
                finally { registration.Dispose(); Volatile.Write(ref busy, 0); }
            }) { IsBackground = true, Name = "ExplorerTabHost explicit Shell creation" };
            try { thread.SetApartmentState(ApartmentState.STA); thread.Start(); }
            catch (Exception error) { registration.Dispose(); Volatile.Write(ref busy, 0); result.TrySetException(error); }
            return result.Task;
        }
    }
}
