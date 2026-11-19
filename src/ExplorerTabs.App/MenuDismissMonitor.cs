using System;
using System.Collections.Generic;
using System.Drawing;
using System.Runtime.InteropServices;
using System.Windows.Forms;

namespace ExplorerTabs.App
{
    /// <summary>
    /// Closes host-owned menus when the user presses a mouse button outside them. Embedded
    /// Explorer windows belong to another process, so their clicks never reach the host's
    /// message loop; a low-level mouse hook bridges only that gap. It never suppresses input
    /// and is installed only while a real (visible) managed menu is open.
    /// </summary>
    internal sealed class MenuDismissMonitor : IDisposable
    {
        private readonly Action<Point> outsidePressed;
        private readonly Action<string>? trace;
        private readonly HashSet<ContextMenuStrip> menus = new HashSet<ContextMenuStrip>();
        private readonly LowLevelMouseCallback callback;
        private IntPtr hook;

        public MenuDismissMonitor(Action<Point> outsidePressed, Action<string>? trace = null)
        {
            this.outsidePressed = outsidePressed; this.trace = trace;
            callback = OnMouse;
        }

        public bool IsActive => hook != IntPtr.Zero;
        public bool IsRegistered(ContextMenuStrip menu) => menus.Contains(menu);

        public void Register(ContextMenuStrip menu)
        {
            if (menu == null || !menus.Add(menu)) return;
            if (menus.Count == 1) Install();
        }

        public void Unregister(ContextMenuStrip menu)
        {
            if (menu == null || !menus.Remove(menu)) return;
            if (menus.Count == 0) Uninstall();
        }

        private void Install()
        {
            if (hook != IntPtr.Zero) return;
            try
            {
                hook = SetWindowsHookEx(WhMouseLl, callback, GetModuleHandle(null), 0);
                if (hook == IntPtr.Zero) trace?.Invoke("Menu dismiss hook unavailable; menus keep the standard close behavior.");
            }
            catch (Exception error) { trace?.Invoke("Menu dismiss hook failed: " + error.Message); }
        }

        private void Uninstall()
        {
            if (hook == IntPtr.Zero) return;
            try { UnhookWindowsHookEx(hook); } catch { /* Best effort on teardown. */ }
            hook = IntPtr.Zero;
        }

        private IntPtr OnMouse(int code, IntPtr message, IntPtr data)
        {
            try
            {
                if (code >= 0 && hook != IntPtr.Zero)
                {
                    int kind = message.ToInt32();
                    if (kind == WmLeftButtonDown || kind == WmRightButtonDown)
                    {
                        var info = Marshal.PtrToStructure<MouseHookData>(data);
                        outsidePressed(new Point(info.Point.X, info.Point.Y));
                    }
                }
            }
            catch { /* Never let a UI close exception cross the native hook boundary. */ }
            return CallNextHookEx(hook, code, message, data);
        }

        /// <summary>True when the screen point hits the menu or one of its visible submenus.</summary>
        public static bool IsInsideMenuTree(ToolStripDropDown menu, Point screenPoint)
        {
            if (menu == null || menu.IsDisposed) return false;
            if (menu.Visible && menu.RectangleToScreen(menu.ClientRectangle).Contains(screenPoint)) return true;
            foreach (ToolStripItem item in menu.Items)
                if (item is ToolStripMenuItem entry && entry.HasDropDown && IsInsideMenuTree(entry.DropDown, screenPoint)) return true;
            return false;
        }

        public void Dispose()
        {
            menus.Clear();
            Uninstall();
        }

        private const int WhMouseLl = 14, WmLeftButtonDown = 0x0201, WmRightButtonDown = 0x0204;

        private delegate IntPtr LowLevelMouseCallback(int code, IntPtr message, IntPtr data);
        [StructLayout(LayoutKind.Sequential)] private struct Point32 { public int X, Y; }
        [StructLayout(LayoutKind.Sequential)]
        private struct MouseHookData { public Point32 Point; public uint MouseData, Flags, Time; public UIntPtr ExtraInfo; }
        [DllImport("user32.dll", SetLastError = true)] private static extern IntPtr SetWindowsHookEx(int hook, LowLevelMouseCallback callback, IntPtr module, uint threadId);
        [DllImport("user32.dll", SetLastError = true)] [return: MarshalAs(UnmanagedType.Bool)] private static extern bool UnhookWindowsHookEx(IntPtr hook);
        [DllImport("user32.dll")] private static extern IntPtr CallNextHookEx(IntPtr hook, int code, IntPtr message, IntPtr data);
        [DllImport("kernel32.dll", CharSet = CharSet.Unicode)] private static extern IntPtr GetModuleHandle(string? name);
    }
}
