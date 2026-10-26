using System;
using System.ComponentModel;
using System.Drawing;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Windows.Forms;
using ExplorerTabHost.Core;

namespace ExplorerTabHost.Tests
{
    internal static partial class Program
    {
        // Register these classes in this test process only. No real Explorer HWNDs are used.
        private static readonly ChromeWindowProc ChromeTestProc = ChromeDefaultProc;
        private static void EmbeddedChromeNativeSmoke()
        {
            IntPtr instance = ChromeModule(null);
            string[] classes = { "UIRibbonCommandBarDock", "UIRibbonCommandBar" };
            int registered = 0;
            try
            {
                foreach (string name in classes)
                {
                    var wc = new ChromeWindowClass { Size = (uint)Marshal.SizeOf<ChromeWindowClass>(), Instance = instance,
                        Procedure = Marshal.GetFunctionPointerForDelegate(ChromeTestProc), ClassName = name };
                    if (ChromeRegister(ref wc) == 0) throw new Win32Exception(Marshal.GetLastWin32Error(), "Register owned chrome test class");
                    registered++;
                }
                using (var parent = Synthetic("Embedded chrome viewport"))
                using (var root = new ChromeOwnedWindow("STATIC", parent.Handle, new Rectangle(0, 0, 620, 420)))
                using (var dock = new ChromeOwnedWindow(classes[0], root.Handle, new Rectangle(0, 31, 620, 27)))
                using (var bar = new ChromeOwnedWindow(classes[1], dock.Handle, new Rectangle(0, 0, 620, 27)))
                {
                    int Measure(IntPtr hwnd) => (int)typeof(NativeWindowApi).GetMethod("MeasureEmbeddedTitleInset", BindingFlags.NonPublic | BindingFlags.Static)!.Invoke(null, new object[] { hwnd })!;
                    Rectangle Crop(Rectangle viewport, int inset) => (Rectangle)typeof(NativeWindowApi).GetMethod("EmbeddedViewportBounds", BindingFlags.NonPublic | BindingFlags.Static)!.Invoke(null, new object[] { viewport, inset })!;
                    Check(!NativeWindowApi.IsExplorer(NativeWindowApi.Inspect(root.Handle)), "ribbon fixture belongs to this test process, never Explorer");
                    Check(!root.Visible && !dock.Visible && !bar.Visible, "native ribbon fixtures remain hidden");
                    Check(Measure(root.Handle) == 31, "measures retained title band from full-width top Ribbon dock");
                    var viewport = new Rectangle(0, 0, 620, 420); var adjusted = Crop(viewport, 31);
                    Check(adjusted.Top == -31 && adjusted.Height == 451 && adjusted.Bottom == viewport.Bottom, "cropping title band preserves the exact viewport bottom and width");
                    root.SetBounds(adjusted);
                    Check(NativeWindowApi.ReadRect(dock.Handle).Top == parent.PointToScreen(viewport.Location).Y,
                        "owned root offset retains Ribbon geometry without mutating its children");
                    Check(Measure(root.Handle) == 31, "remeasuring a cropped root remains stable, not cumulative");
                    Check(Crop(viewport, Measure(root.Handle)) == adjusted, "repeated layout does not grow or progressively crop the root");
                    dock.SetBounds(new Rectangle(0, 0, 620, 27)); Check(Measure(root.Handle) == 0, "no title reserve means no cropping");
                    dock.SetBounds(new Rectangle(0, 500, 620, 27)); Check(Measure(root.Handle) == 0, "implausibly large gap fails open");
                    dock.SetBounds(new Rectangle(0, 31, 0, 27)); Check(Measure(root.Handle) == 0, "zero-width side dock is ignored");
                    dock.SetBounds(new Rectangle(1, 31, 619, 27)); Check(Measure(root.Handle) == 0, "partial-width Ribbon-like pane is not treated as a title band");
                    dock.SetBounds(new Rectangle(0, 31, 620, 0)); Check(Measure(root.Handle) == 0, "zero-height bottom dock is ignored");
                    dock.SetBounds(new Rectangle(0, 31, 620, 600)); Check(Measure(root.Handle) == 0, "dock extending beyond root fails open");
                    dock.SetBounds(new Rectangle(0, 31, 620, 27));
                    using (var nestedRoot = new ChromeOwnedWindow("STATIC", root.Handle, new Rectangle(0, 0, 620, 400)))
                    using (var nestedDock = new ChromeOwnedWindow(classes[0], nestedRoot.Handle, new Rectangle(0, 20, 620, 27)))
                    using (var nestedBar = new ChromeOwnedWindow(classes[1], nestedDock.Handle, new Rectangle(0, 0, 620, 27)))
                    { Check(Measure(root.Handle) == 31, "nested Ribbon-like children never override the direct top dock"); }
                    bar.Dispose(); Check(Measure(root.Handle) == 0, "unknown dock without a Ribbon command bar is not cropped");
                    Check(Measure(IntPtr.Zero) == 0, "missing HWND fails open without a crop");
                    Check(Crop(viewport, 0) == viewport, "unknown layout retains the entire ordinary viewport");
                    // NativeWindowApi itself must not crop synthetic/non-Explorer windows, even if
                    // their descendants mimic a Ribbon. Test full native attach/layout/restore too.
                    var api = new NativeWindowApi(new FakeTaskbar());
                    using (var source = new Form { Text = "ExplorerTabHost test Non-Explorer safety", Opacity = 0,
                        StartPosition = FormStartPosition.Manual, Location = Point.Empty, Size = new Size(640, 480) })
                    using (var ordinaryDock = new ChromeOwnedWindow(classes[0], source.Handle, new Rectangle(0, 31, 620, 27)))
                    using (var ordinaryBar = new ChromeOwnedWindow(classes[1], ordinaryDock.Handle, new Rectangle(0, 0, 620, 27)))
                    {
                        var identity = NativeWindowApi.Inspect(source.Handle); var before = api.Capture(identity);
                        var snapshot = api.Capture(identity); snapshot.Mode = HostingMode.Embedded; snapshot.HostHandle = root.Handle.ToInt64();
                        api.Attach(snapshot, root.Handle, viewport); api.Layout(snapshot, viewport);
                        Check(NativeWindowApi.ReadRect(identity.Hwnd).ToRectangle().Size == viewport.Size, "non-Explorer native attach/layout bypasses cropping");
                        api.Restore(snapshot);
                        var after = api.Capture(identity);
                        Check(after.Style == before.Style && after.Parent == before.Parent && after.Placement.NormalPosition.ToRectangle() == before.Placement.NormalPosition.ToRectangle(),
                            "ordinary native restoration preserves original styles, parent and placement");
                    }
                }
            }
            finally
            {
                for (int i = registered - 1; i >= 0; i--)
                    if (!ChromeUnregister(classes[i], instance)) throw new Win32Exception(Marshal.GetLastWin32Error(), "Unregister owned chrome test class");
            }
        }
        private sealed class ChromeOwnedWindow : IDisposable
        {
            public ChromeOwnedWindow(string className, IntPtr parent, Rectangle bounds)
            {
                Handle = ChromeCreate(0, className, "", 0x40000000, bounds.X, bounds.Y, bounds.Width, bounds.Height, parent, IntPtr.Zero, ChromeModule(null), IntPtr.Zero);
                if (Handle == IntPtr.Zero) throw new Win32Exception(Marshal.GetLastWin32Error(), "Create owned chrome test window");
            }
            public IntPtr Handle { get; private set; }
            public bool Visible => ChromeWindowVisible(Handle);
            public void SetBounds(Rectangle r)
            { if (!ChromePosition(Handle, IntPtr.Zero, r.X, r.Y, r.Width, r.Height, 0x14)) throw new Win32Exception(Marshal.GetLastWin32Error()); }
            public void Dispose() { if (Handle != IntPtr.Zero) { ChromeDestroy(Handle); Handle = IntPtr.Zero; } }
        }
        [UnmanagedFunctionPointer(CallingConvention.Winapi)] private delegate IntPtr ChromeWindowProc(IntPtr hwnd, uint msg, IntPtr wp, IntPtr lp);
        [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)] private struct ChromeWindowClass
        {
            public uint Size, Style; public IntPtr Procedure; public int ClassExtra, WindowExtra;
            public IntPtr Instance, Icon, Cursor, Background;
            public string? MenuName; public string ClassName; public IntPtr SmallIcon;
        }
        [DllImport("user32.dll", EntryPoint = "CreateWindowExW", CharSet = CharSet.Unicode, SetLastError = true)] private static extern IntPtr ChromeCreate(uint extended, string name, string title, uint style, int x, int y, int width, int height, IntPtr parent, IntPtr menu, IntPtr instance, IntPtr param);
        [DllImport("user32.dll", EntryPoint = "DestroyWindow")] private static extern bool ChromeDestroy(IntPtr hwnd);
        [DllImport("user32.dll", EntryPoint = "RegisterClassExW", CharSet = CharSet.Unicode, SetLastError = true)] private static extern ushort ChromeRegister(ref ChromeWindowClass wc);
        [DllImport("user32.dll", EntryPoint = "UnregisterClassW", CharSet = CharSet.Unicode, SetLastError = true)] private static extern bool ChromeUnregister(string name, IntPtr instance);
        [DllImport("user32.dll", EntryPoint = "DefWindowProcW")] private static extern IntPtr ChromeDefaultProc(IntPtr hwnd, uint message, IntPtr wp, IntPtr lp);
        [DllImport("kernel32.dll", EntryPoint = "GetModuleHandleW", CharSet = CharSet.Unicode)] private static extern IntPtr ChromeModule(string? name);
        [DllImport("user32.dll", EntryPoint = "SetWindowPos", SetLastError = true)] private static extern bool ChromePosition(IntPtr hwnd, IntPtr after, int x, int y, int width, int height, uint flags);
        [DllImport("user32.dll", EntryPoint = "IsWindowVisible")] private static extern bool ChromeWindowVisible(IntPtr hwnd);
    }
}
