using System;
using System.Runtime.InteropServices;
using System.Text;
using ExplorerTabs.Core;

namespace ExplorerTabs.App
{
    /// <summary>Read-only browser/view identity and current folder. Never reparents or navigates a source.</summary>
    internal sealed class ShellViewReference : IDisposable
    {
        private static readonly Guid TopLevelBrowser = new Guid("4C96BE40-915C-11CF-99D3-00AA004AE837");
        private readonly IShellBrowserNative browser;
        private readonly IShellViewNative view;
        private readonly Action<string>? trace;
        public long BrowserHandle { get; }
        public long ViewHandle { get; }
        private ShellViewReference(IShellBrowserNative browser, IShellViewNative view, long browserHandle,
            long viewHandle, Action<string>? trace)
        { this.browser = browser; this.view = view; BrowserHandle = browserHandle; ViewHandle = viewHandle; this.trace = trace; }

        public static ShellViewReference? TryOpen(object item, Action<string>? trace)
        {
            IShellBrowserNative? browser = null;
            IShellViewNative? view = null;
            try
            {
                if (!(item is IShellServiceProvider provider)) return null;
                Guid service = TopLevelBrowser, iid = typeof(IShellBrowserNative).GUID;
                int hr = provider.QueryService(ref service, ref iid, out browser);
                trace?.Invoke($"Creation source QueryService; HRESULT=0x{hr:X8}");
                if (hr < 0 || browser == null) return null;
                hr = browser.GetWindow(out IntPtr browserWindow);
                if (hr < 0) browserWindow = IntPtr.Zero;
                hr = browser.QueryActiveShellView(out view);
                trace?.Invoke($"Creation source QueryActiveShellView; HRESULT=0x{hr:X8}");
                if (hr < 0 || view == null) return null;
                hr = view.GetWindow(out IntPtr viewWindow);
                if (hr < 0) return null;
                var reference = new ShellViewReference(browser, view, browserWindow.ToInt64(), viewWindow.ToInt64(), trace);
                browser = null; view = null; // Transfer the two references to the returned lifetime.
                return reference;
            }
            catch (COMException error)
            { trace?.Invoke($"Creation source view unavailable; HRESULT=0x{error.HResult:X8}; {error.Message}"); return null; }
            finally { Release(view); Release(browser); }
        }

        public string? ReadDirectory()
        {
            if (!(view is IFolderViewNative folderView))
            { trace?.Invoke("Creation source folder view unavailable; fallback=LocationURL"); return null; }
            IPersistFolder2Native? folder = null;
            IntPtr pidl = IntPtr.Zero;
            try
            {
                Guid iid = typeof(IPersistFolder2Native).GUID;
                int hr = folderView.GetFolder(ref iid, out folder);
                trace?.Invoke($"Creation source GetFolder; HRESULT=0x{hr:X8}");
                if (hr < 0 || folder == null) return null;
                Marshal.ThrowExceptionForHR(folder.GetCurFolder(out pidl));
                if (pidl == IntPtr.Zero) throw new InvalidOperationException("Shell 当前文件夹标识为空，请等待导航完成。");
                var directory = new StringBuilder(32768);
                if (!SHGetPathFromIDListEx(pidl, directory, (uint)directory.Capacity, 0))
                {
                    if (IsThisPcPidl(pidl))
                    {
                        trace?.Invoke("Creation source directory provider=IPersistFolder2/ThisPC");
                        return FolderTarget.ThisPc;
                    }
                    throw new InvalidOperationException("此标签是虚拟目录，不能复制为普通文件夹标签；仅支持“此电脑”，未通过标题猜测。");
                }
                trace?.Invoke("Creation source directory provider=IPersistFolder2/absolute-PIDL");
                return FolderTarget.Normalize(directory.ToString());
            }
            finally { if (pidl != IntPtr.Zero) Marshal.FreeCoTaskMem(pidl); Release(folder); }
        }
        /// <summary>Compares the active view's current PIDL with a parsed shell target (used for This PC).</summary>
        public bool CurrentFolderIs(string target)
        {
            if (!(view is IFolderViewNative folderView)) return false;
            IPersistFolder2Native? folder = null;
            IntPtr pidl = IntPtr.Zero, targetPidl = IntPtr.Zero;
            try
            {
                Guid iid = typeof(IPersistFolder2Native).GUID;
                if (folderView.GetFolder(ref iid, out folder) < 0 || folder == null) return false;
                if (folder.GetCurFolder(out pidl) < 0 || pidl == IntPtr.Zero) return false;
                if (SHParseDisplayName(target, IntPtr.Zero, out targetPidl, 0, out _) < 0 || targetPidl == IntPtr.Zero) return false;
                return ILIsEqual(pidl, targetPidl);
            }
            catch (COMException error)
            { trace?.Invoke($"Creation target compare failed; HRESULT=0x{error.HResult:X8}; {error.Message}"); return false; }
            finally
            {
                if (pidl != IntPtr.Zero) Marshal.FreeCoTaskMem(pidl);
                if (targetPidl != IntPtr.Zero) Marshal.FreeCoTaskMem(targetPidl);
                Release(folder);
            }
        }
        private static bool IsThisPcPidl(IntPtr pidl)
        {
            IntPtr target = IntPtr.Zero;
            try
            {
                return SHParseDisplayName(FolderTarget.ThisPc, IntPtr.Zero, out target, 0, out _) >= 0 &&
                    target != IntPtr.Zero && ILIsEqual(pidl, target);
            }
            finally { if (target != IntPtr.Zero) Marshal.FreeCoTaskMem(target); }
        }
        public void Dispose() { Release(view); Release(browser); }
        private static void Release(object? value) { if (value != null && Marshal.IsComObject(value)) Marshal.ReleaseComObject(value); }

        [ComImport, Guid("6D5140C1-7436-11CE-8034-00AA006009FA"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
        private interface IShellServiceProvider
        { [PreserveSig] int QueryService(ref Guid service, ref Guid iid, [MarshalAs(UnmanagedType.Interface)] out IShellBrowserNative? result); }

        // Prefix of IShellBrowser's native vtable through QueryActiveShellView, including IOleWindow.
        // These inherited slots must not be removed/reordered. Only GetWindow and QueryActiveShellView are invoked.
        [ComImport, Guid("000214E2-0000-0000-C000-000000000046"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
        private interface IShellBrowserNative
        {
            [PreserveSig] int GetWindow(out IntPtr window);
            [PreserveSig] int ContextSensitiveHelp([MarshalAs(UnmanagedType.Bool)] bool enter);
            [PreserveSig] int InsertMenusSB(IntPtr menu, IntPtr widths);
            [PreserveSig] int SetMenuSB(IntPtr menu, IntPtr oleMenu, IntPtr activeWindow);
            [PreserveSig] int RemoveMenusSB(IntPtr menu);
            [PreserveSig] int SetStatusTextSB([MarshalAs(UnmanagedType.LPWStr)] string text);
            [PreserveSig] int EnableModelessSB([MarshalAs(UnmanagedType.Bool)] bool enable);
            [PreserveSig] int TranslateAcceleratorSB(IntPtr message, ushort id);
            [PreserveSig] int BrowseObject(IntPtr pidl, uint flags);
            [PreserveSig] int GetViewStateStream(uint mode, out IntPtr stream);
            [PreserveSig] int GetControlWindow(uint id, out IntPtr window);
            [PreserveSig] int SendControlMsg(uint id, uint message, IntPtr wparam, IntPtr lparam, out IntPtr result);
            [PreserveSig] int QueryActiveShellView([MarshalAs(UnmanagedType.Interface)] out IShellViewNative? view);
        }
        [ComImport, Guid("000214E3-0000-0000-C000-000000000046"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
        private interface IShellViewNative
        {
            [PreserveSig] int GetWindow(out IntPtr window);
            [PreserveSig] int ContextSensitiveHelp([MarshalAs(UnmanagedType.Bool)] bool enter);
        }
        [ComImport, Guid("CDE725B0-CCC9-4519-917E-325D72FAB4CE"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
        private interface IFolderViewNative
        {
            [PreserveSig] int GetCurrentViewMode(out uint mode);
            [PreserveSig] int SetCurrentViewMode(uint mode);
            [PreserveSig] int GetFolder(ref Guid iid, [MarshalAs(UnmanagedType.Interface)] out IPersistFolder2Native? folder);
        }
        [ComImport, Guid("1AC3D9F0-175C-11D1-95BE-00609797EA4F"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
        private interface IPersistFolder2Native
        {
            [PreserveSig] int GetClassID(out Guid clsid);
            [PreserveSig] int Initialize(IntPtr pidl);
            [PreserveSig] int GetCurFolder(out IntPtr pidl);
        }
        [DllImport("shell32.dll", CharSet = CharSet.Unicode)]
        [return: MarshalAs(UnmanagedType.Bool)] private static extern bool SHGetPathFromIDListEx(IntPtr pidl,
            StringBuilder path, uint length, uint flags);
        [DllImport("shell32.dll", CharSet = CharSet.Unicode, PreserveSig = true)]
        private static extern int SHParseDisplayName([MarshalAs(UnmanagedType.LPWStr)] string name,
            IntPtr bindingContext, out IntPtr pidl, uint attributesIn, out uint attributesOut);
        [DllImport("shell32.dll")]
        [return: MarshalAs(UnmanagedType.Bool)] private static extern bool ILIsEqual(IntPtr pidl1, IntPtr pidl2);
    }
}
