using System;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.InteropServices;
using System.Threading;
using System.Windows.Forms;
using ExplorerTabs.Core;

namespace ExplorerTabs.App
{
    /// <summary>All COM lives on the single creation STA. No polling/activation by title, PID or newest HWND.</summary>
    internal sealed class ShellExplorerBackend : IExplorerCreationBackend
    {
        private readonly NativeWindowApi windows = new NativeWindowApi();
        private readonly bool initialized;
        private readonly Action<string>? trace;
        public ShellExplorerBackend(Action<string>? trace = null)
        {
            this.trace = trace;
            int hr = CoInitializeEx(IntPtr.Zero, 6); // STA + disable OLE1 DDE
            Marshal.ThrowExceptionForHR(hr); initialized = true;
        }
        public IReadOnlyCollection<long> CaptureExistingHandles()
        {
            var handles = new List<long>();
            if (!EnumWindows((hwnd, _) => { handles.Add(hwnd.ToInt64()); return true; }, IntPtr.Zero))
                throw new InvalidOperationException("无法记录已有窗口，已停止新建。");
            trace?.Invoke("Creation snapshot; existing-top-level-handles=" + handles.Count);
            return handles; // Include hidden and inaccessible windows; do not silently omit them.
        }
        public string ReadDirectory(WindowIdentity source)
        {
            trace?.Invoke($"Creation source read; HWND=0x{source.Handle:X}");
            if (!windows.Matches(source)) throw new InvalidOperationException("原标签窗口已关闭或身份改变。");
            object? shellWindows = null;
            try
            {
                shellWindows = Activator.CreateInstance(Type.GetTypeFromCLSID(new Guid("9BA05972-F6A8-11CF-A442-00A0C90A8F39"), true)!);
                var collection = (IShellWindowsDispatch)shellWindows!;
                int count = collection.Count;
                trace?.Invoke($"Creation source enumeration; count={count}");
                if (count < 0 || count > 512) throw new InvalidOperationException("Shell 窗口列表数量异常。");
                for (int i = 0; i < count; i++)
                {
                    object? item = null;
                    string? directory = null;
                    try
                    {
                        item = collection.Item(i);
                        if (!(item is IWebBrowser2Native browser))
                        { trace?.Invoke($"Creation source candidate; index={i}; IWebBrowser2=False"); continue; }
                        long automationHandle = browser.GetHWND();
                        using (var view = ShellViewReference.TryOpen(item, trace))
                        {
                            long browserHandle = view?.BrowserHandle ?? 0;
                            long viewHandle = view?.ViewHandle ?? 0;
                            // The automation HWND can become the shared host after SetParent.
                            // Resolve the browser's own view; never match the common root/owner.
                            bool viewBelongs = viewHandle != 0 && IsChild(new IntPtr(source.Handle), new IntPtr(viewHandle));
                            bool matched = SourceWindowMatch.IsExact(source.Handle, automationHandle, browserHandle, viewHandle, viewBelongs);
                            trace?.Invoke($"Creation source candidate; index={i}; automation=0x{automationHandle:X}; browser=0x{browserHandle:X}; view=0x{viewHandle:X}; view-child={viewBelongs}; matched={matched}");
                            if (!matched) continue;
                            if (!windows.Matches(source)) throw new InvalidOperationException("读取目录时原标签身份改变。");
                            if (browser.GetBusy()) throw new InvalidOperationException("当前目录仍在导航，请等待完成后再复制标签。");
                            // IPersistFolder2 supplies the current absolute PIDL even when LocationURL is empty.
                            directory = view?.ReadDirectory();
                            if (directory == null) directory = FolderTarget.FromLocationUrl(browser.GetLocationURL());
                            if (browser.GetBusy() || !windows.Matches(source) ||
                                !SourceWindowMatch.IsExact(source.Handle, automationHandle, browserHandle, viewHandle,
                                    viewHandle != 0 && IsChild(new IntPtr(source.Handle), new IntPtr(viewHandle))))
                                throw new InvalidOperationException("读取目录时窗口身份或导航状态改变，请稍后重试。");
                        }
                    }
                    catch (COMException error)
                    {
                        // A stale/unrelated Shell entry must not prevent enumerating the selected one.
                        trace?.Invoke($"Creation source candidate COM failure; index={i}; HRESULT=0x{error.HResult:X8}; {error.Message}");
                    }
                    finally { Release(item); }
                    if (directory != null)
                    {
                        trace?.Invoke($"Creation source directory resolved; HWND=0x{source.Handle:X}; directory={directory}");
                        return directory;
                    }
                }
                throw new InvalidOperationException("Shell 未提供此标签的目录；已记录窗口和 Shell 视图匹配日志，未通过标题猜测。");
            }
            finally { Release(shellWindows); }
        }
        public bool DirectoryExists(string directory) => System.IO.Directory.Exists(directory);
        public ICreatedExplorerReference Create()
        {
            Guid clsid = new Guid("C08AFD90-F2A1-11D1-8455-00A0C91F3880"), iid = typeof(IWebBrowser2Native).GUID;
            trace?.Invoke("Creation CoCreateInstance begin; dedicated ShellBrowserWindow");
            Marshal.ThrowExceptionForHR(CoCreateInstance(ref clsid, IntPtr.Zero, 4, ref iid, out object instance));
            trace?.Invoke("Creation CoCreateInstance returned");
            try { return new BrowserReference((IWebBrowser2Native)instance, trace); }
            catch { Release(instance); throw; }
        }
        public WindowIdentity Inspect(long handle)
        {
            var identity = NativeWindowApi.Inspect(new IntPtr(handle));
            // Records which host served the new ShellBrowserWindow (explorer.exe or the
            // registered rundll32 local server) without affecting the identity check.
            trace?.Invoke($"Creation inspect; HWND=0x{handle:X}; class={identity.ClassName}; image={identity.ImagePath}; PID={identity.ProcessId}");
            return identity;
        }
        public bool Matches(WindowIdentity identity) => windows.Matches(identity);
        public void Pause(CancellationToken cancellation)
        { Application.DoEvents(); cancellation.ThrowIfCancellationRequested(); System.Threading.Thread.Sleep(50); cancellation.ThrowIfCancellationRequested(); }
        public void Dispose() { if (initialized) CoUninitialize(); trace?.Invoke("Creation STA backend released"); }
        private static void Release(object? value) { if (value != null && Marshal.IsComObject(value)) Marshal.ReleaseComObject(value); }
        private sealed class BrowserReference : ICreatedExplorerReference, IDeferredExplorerReference
        {
            private readonly IWebBrowser2Native browser;
            private readonly Action<string>? trace;
            public BrowserReference(IWebBrowser2Native browser, Action<string>? trace) { this.browser = browser; this.trace = trace; }
            public long Handle => browser.GetHWND();
            public bool Busy => browser.GetBusy();
            public string LocationUrl => browser.GetLocationURL();
            public void Navigate(string directory, CancellationToken cancellation)
            {
                // Pass an absolute PIDL SAFEARRAY, avoiding URL parsing and ambiguous Shell string dispatch.
                cancellation.ThrowIfCancellationRequested();
                trace?.Invoke("Creation parse folder begin; directory=" + directory);
                Marshal.ThrowExceptionForHR(SHParseDisplayName(directory, IntPtr.Zero, out IntPtr pidl, 0, out _));
                try
                {
                    int size = checked((int)ILGetSize(pidl));
                    if (size < 2 || size > 1048576) throw new InvalidOperationException("Shell 文件夹标识大小异常。");
                    var bytes = new byte[size]; Marshal.Copy(pidl, bytes, 0, size);
                    // Match native VARIANT initialization: optional values are VT_EMPTY, not
                    // VT_ERROR/DISP_E_PARAMNOTFOUND sent by Type.Missing through late binding.
                    object url = bytes, flags = null!, target = null!, post = null!, headers = null!;
                    cancellation.ThrowIfCancellationRequested();
                    trace?.Invoke($"Creation Navigate2 begin; dedicated browser; ABI=vtable; URL=VT_ARRAY|VT_UI1; PIDL-bytes={size}; optional=VT_EMPTY");
                    cancellation.ThrowIfCancellationRequested();
                    try { browser.Navigate2(ref url, ref flags, ref target, ref post, ref headers); }
                    catch (Exception error)
                    { trace?.Invoke($"Creation Navigate2 failed; HRESULT=0x{error.HResult:X8}; {error.Message}"); throw; }
                    trace?.Invoke("Creation Navigate2 returned");
                }
                finally { Marshal.FreeCoTaskMem(pidl); }
            }
            public Action DeferPresentation(WindowIdentity identity)
            {
                var api = new NativeWindowApi();
                if (browser.GetHWND() != identity.Handle || !api.Matches(identity) || GetParent(identity.Hwnd) != IntPtr.Zero)
                    throw new InvalidOperationException("新建呈现目标已变化，不隐藏其他窗口。");
                // Install recovery before the COM call: SetVisible can mutate and then throw.
                Action restore = () =>
                {
                    try
                    {
                        if (!api.Matches(identity) || GetParent(identity.Hwnd) != IntPtr.Zero ||
                            (GetWindowLongPtr(identity.Hwnd, -16).ToInt64() & 0x40000000L) != 0)
                        { trace?.Invoke($"Creation independent recovery skipped; changed/attached HWND=0x{identity.Handle:X}"); return; }
                        ShowWindow(identity.Hwnd, 4); // SW_SHOWNOACTIVATE; never activate, Quit or search.
                        trace?.Invoke($"Creation independent recovery; HWND=0x{identity.Handle:X}");
                    }
                    catch (Exception error) { trace?.Invoke("Creation independent recovery failed; " + error); }
                };
                try { browser.SetVisible(false); trace?.Invoke($"Creation deferred presentation; HWND=0x{identity.Handle:X}"); }
                catch { restore(); throw; }
                return restore;
            }
            public void Show() { trace?.Invoke("Creation show independent window"); browser.SetVisible(true); }
            public void Dispose() { Release(browser); } // Never Quit: a failed request must not close a user's window.
        }
        [ComImport, Guid("85CB6900-4D95-11CF-960C-0080C7F4EE85"), InterfaceType(ComInterfaceType.InterfaceIsIDispatch)]
        private interface IShellWindowsDispatch
        {
            [DispId(1610743808)] int Count { get; }
            [DispId(0)] [return: MarshalAs(UnmanagedType.IDispatch)] object Item([In, MarshalAs(UnmanagedType.Struct)] object index);
        }
        [DllImport("user32.dll")] private static extern IntPtr GetParent(IntPtr hwnd);
        [DllImport("user32.dll", EntryPoint = "GetWindowLongPtrW")] private static extern IntPtr GetWindowLongPtr(IntPtr hwnd, int index);
        [DllImport("user32.dll")] [return: MarshalAs(UnmanagedType.Bool)] private static extern bool ShowWindow(IntPtr hwnd, int command);
        [DllImport("user32.dll")] [return: MarshalAs(UnmanagedType.Bool)] private static extern bool IsChild(IntPtr parent, IntPtr child);
        [DllImport("shell32.dll", CharSet = CharSet.Unicode)] private static extern int SHParseDisplayName(string name, IntPtr bindContext,
            out IntPtr pidl, uint attributes, out uint resultingAttributes);
        [DllImport("shell32.dll")] private static extern uint ILGetSize(IntPtr pidl);
        private delegate bool EnumCallback(IntPtr hwnd, IntPtr data);
        [DllImport("user32.dll", SetLastError = true)] [return: MarshalAs(UnmanagedType.Bool)] private static extern bool EnumWindows(EnumCallback callback, IntPtr data);
        [DllImport("ole32.dll")] private static extern int CoInitializeEx(IntPtr reserved, uint flags);
        [DllImport("ole32.dll")] private static extern void CoUninitialize();
        [DllImport("ole32.dll")] private static extern int CoCreateInstance(ref Guid clsid, IntPtr outer, uint context, ref Guid iid,
            [MarshalAs(UnmanagedType.Interface)] out object instance);
    }
}
