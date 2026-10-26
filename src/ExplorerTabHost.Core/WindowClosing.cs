using System;
using System.ComponentModel;
using System.Runtime.InteropServices;

namespace ExplorerTabHost.Core
{
    public interface IWindowCloser { void RequestClose(WindowIdentity identity); }
    /// <summary>Close ONE identity-checked Explorer HWND. Never terminate its shared process or detach first.</summary>
    public sealed class NativeWindowCloser : IWindowCloser
    {
        public void RequestClose(WindowIdentity identity)
        {
            if (!NativeWindowApi.IsExplorer(identity) || !new NativeWindowApi().Matches(identity))
                throw new InvalidOperationException("目标 Explorer 身份无法确认，未发送关闭。");
            if (!PostMessage(identity.Hwnd, 0x0010, IntPtr.Zero, IntPtr.Zero)) throw new Win32Exception(Marshal.GetLastWin32Error());
        }
        [DllImport("user32.dll", SetLastError = true)] [return: MarshalAs(UnmanagedType.Bool)]
        private static extern bool PostMessage(IntPtr hwnd, uint message, IntPtr wparam, IntPtr lparam);
    }
}
