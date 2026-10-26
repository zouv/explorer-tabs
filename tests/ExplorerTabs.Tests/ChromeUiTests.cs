using System;
using System.Drawing;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Windows.Forms;
using ExplorerTabs.Core;
namespace ExplorerTabs.Tests
{
    internal static partial class Program
    {
        private static void ChromeUiSmoke()
        {
            Console.WriteLine("Chrome smoke: actual hidden controls, pure hit-test policy and own HWND styles; no Show, hooks or Explorer.");
            var dir = Path.Combine(Path.GetTempPath(), "ExplorerTabs-chrome-" + Guid.NewGuid().ToString("N")); Directory.CreateDirectory(dir);
            try
            {
                var api = new FakeWindows(); var host = new HostController(api, new FakeStore());
                using (var form = WorkspaceForm(host, new WorkspaceService(), dir, new WorkspaceStore()))
                {
                    var chrome = UiField<Panel>(form, "chrome"); var strip = UiField<FlowLayoutPanel>(form, "tabs"); var content = UiField<Panel>(form, "content");
                    var toolbar = UiField<FlowLayoutPanel>(form, "commandToolbar");
                    Check(UiField<object?>(form, "keyboard") == null, "hidden chrome does not register global keyboard hook");
                    Check(form.Icon != null && form.Icon.Width > 0, "application icon is loaded from embedded multi-size ICO");
                    long style = ChromeStyle(form.Handle, -16).ToInt64();
                    Check((style & 0x00C00000) == 0 && (style & 0x000F0000) == 0x000F0000, "own host HWND has no caption but retains resizing/system/min/max styles");
                    Check(form.FormBorderStyle == FormBorderStyle.None, "main form removes the independent standard title bar");
                    for (int cycle = 0; cycle < 4; cycle++)
                    {
                        Check(ChromeSend(form.Handle, 0x0085, new IntPtr(1), IntPtr.Zero) == IntPtr.Zero, "default nonclient frame repaint is suppressed " + cycle);
                        Check(ChromeSend(form.Handle, 0x0086, IntPtr.Zero, IntPtr.Zero) != IntPtr.Zero, "nonclient deactivation retains default state transition " + cycle);
                        ChromeSend(form.Handle, 0x0086, new IntPtr(1), IntPtr.Zero);
                        Check(ChromeGetClientRect(form.Handle, out var client) && NativeWindowApi.ReadRect(form.Handle).ToRectangle().Size == client.ToRectangle().Size,
                            "activation cycles keep client area extended through native frame " + cycle);
                    }
                    Check((ChromeStyle(form.Handle, -16).ToInt64() & 0x000F0000) == 0x000F0000, "activation painting suppression preserves resize/system styles");
                    var brandArea = chrome.Controls.OfType<Panel>().Single(p => !(p is FlowLayoutPanel) && p != strip);
                    var captions = chrome.Controls.OfType<FlowLayoutPanel>().Single(p => p != strip);
                    var capButtons = captions.Controls.OfType<Button>().ToArray();
                    Check(capButtons.Length == 3 && capButtons.All(b => b.AccessibleName!.Length > 0 && b.TabStop), "minimize/maximize/close remain accessible interactive buttons");
                    Check(capButtons.Last().Text.Contains("恢复"), "caption close explicitly indicates restoration, not killing Explorer");
                    foreach (int width in new[] { 720, 1100, 1600 })
                    {
                        form.Width = width; form.PerformLayout(); strip.PerformLayout();
                        Check(chrome.Top == 1 && toolbar.Top == chrome.Bottom && content.Top == toolbar.Bottom, "browser chrome and toolbar dock before content at width " + width);
                        using (var image = new Bitmap(toolbar.Width, toolbar.Height))
                        {
                            toolbar.DrawToBitmap(image, toolbar.ClientRectangle);
                            Check(image.GetPixel(toolbar.Width / 2, toolbar.Height - 1).ToArgb() == Color.FromArgb(215, 222, 231).ToArgb(),
                                "low-saturation one-pixel divider is painted inside the toolbar without adding a blank row at width " + width);
                        }
                        Check(chrome.Height <= 36 && toolbar.Height <= 36, "empty chrome plus toolbar occupies at most 72 client pixels at width " + width);
                        Check(strip.Right <= captions.Left && strip.Left >= brandArea.Right && captions.Right <= chrome.ClientSize.Width, "tabs never overlap brand or caption controls at width " + width);
                        Check(capButtons.All(b => b.Top == 0 && b.Bottom <= 36) && brandArea.Controls[0].Top == 0 && brandArea.Controls[0].Height <= 36, "window buttons and brand stay in the first tab row at width " + width);
                        Check(toolbar.Controls.Cast<Control>().Where(c => c.Visible).All(c => c.Bottom <= toolbar.Height && c.Right <= toolbar.ClientSize.Width), "all command glyphs fit without a second toolbar row at width " + width);
                    }
                    var tips = UiField<ToolTip>(form, "chromeTips");
                    foreach (Button button in toolbar.Controls.OfType<Button>())
                        Check(!string.IsNullOrEmpty(tips.GetToolTip(button)) && !string.IsNullOrEmpty(button.AccessibleName), "command tooltip/accessibility exists: " + button.Text);
                    var newButton = UiField<Button>(form, "newTab");
                    var settings = new AppSettings(); settings.KeyBindings.Single(k => k.Command == HostCommand.NewTab).Chord = "Ctrl+Shift+T";
                    settings.KeyBindings.Single(k => k.Command == HostCommand.ReopenTab).Chord = "";
                    UiCall(form, "ApplySettings", settings);
                    Check(tips.GetToolTip(newButton).Contains("Ctrl+Shift+T") && !tips.GetToolTip(newButton).Contains(" · Ctrl+T"), "tooltip reflects configured shortcut rather than hardcoded default");
                    settings.ShortcutsEnabled = false; UiCall(form, "ApplySettings", settings);
                    Check(!tips.GetToolTip(newButton).Contains("Ctrl+"), "disabled keyboard bindings are not advertised in toolbar tooltips");
                    for (int i = 1; i <= 20; i++) { api.Alive.Add(i); Add(host, i); }
                    host.Activate(host.Sessions[0], Bounds); form.Width = 720; UiCall(form, "RefreshTabs"); form.PerformLayout();
                    Check(strip.HorizontalScroll.Visible && !strip.VerticalScroll.Visible && chrome.Height <= 56, "single-row overflow reserves only its real scrollbar height");
                    Check(content.Controls.Cast<Control>().All(c => !c.Visible), "welcome actions disappear when real Explorer tabs are present");
                    UiAnyCall(strip, "ConfigureLayout", true, 3); form.PerformLayout();
                    Check(chrome.Height == strip.Height && chrome.Height <= 100 && toolbar.Top == chrome.Bottom, "wrap height updates enclosing chrome without overlapping toolbar");
                    Check(capButtons.All(b => b.Top == 0) && brandArea.Controls[0].Height <= 36, "wrapped tabs do not vertically center window controls or brand");
                    Check(strip.VerticalScroll.Visible && !strip.HorizontalScroll.Visible, "wrapped browser chrome keeps vertical overflow");
                    Check(!api.Events.Any(e => e.StartsWith("restore")), "browser chrome layout never reparents or restores managed Explorer");
                    UiCall(form, "SetCreationControls", true);
                    Check(!newButton.Enabled && UiField<Button>(form, "cancelCreation").Visible, "async preparation disables new commands and exposes cancel glyph");
                    UiCall(form, "SetCreationControls", false);
                }
                AdaptiveTabWidthsSmoke(dir);
                var hit = MainType.GetMethod("ChromeHitTest", BindingFlags.Static | BindingFlags.NonPublic)!;
                var points = new[] { new Point(1, 1), new Point(799, 1), new Point(1, 599), new Point(799, 599), new Point(1, 100), new Point(799, 100), new Point(100, 1), new Point(100, 599), new Point(100, 100) };
                int[] expected = { 13, 14, 16, 17, 10, 11, 12, 15, 1 };
                for (int i = 0; i < points.Length; i++)
                {
                    Check((int)hit.Invoke(null, new object[] { points[i], new Size(800, 600), 6, false })! == expected[i], "native resize hit region " + expected[i]);
                    Check((int)hit.Invoke(null, new object[] { points[i], new Size(800, 600), 6, true })! == 1, "maximized host does not advertise resize edges " + i);
                }
            }
            finally { Directory.Delete(dir, true); }
        }
        private static void AdaptiveTabWidthsSmoke(string directory)
        {
            var api = new FakeWindows(); var host = new HostController(api, new FakeStore());
            for (int i = 1; i <= 12; i++) { api.Alive.Add(i); Add(host, i); }
            string[] titles = { "D:", "项目资源目录", new string('W', 90), "A&B · 工作空间" };
            for (int i = 0; i < host.Sessions.Count; i++) host.RenameTab(host.Sessions[i], titles[i % titles.Length]);
            host.Activate(host.Sessions[0], Bounds);
            using (var form = WorkspaceForm(host, new WorkspaceService(), directory, new WorkspaceStore()))
            {
                form.Width = 1100; UiCall(form, "RefreshTabs"); form.PerformLayout();
                var strip = UiField<FlowLayoutPanel>(form, "tabs"); var buttons = strip.Controls.Cast<Control>().ToArray();
                Check(buttons[0].Width == 110 && buttons[2].Width == 280, "adaptive titles clamp to 110 / 280 logical pixels at 96 DPI");
                Check(buttons[1].Width > buttons[0].Width && buttons[1].Width < buttons[2].Width, "medium Chinese title uses its measured width instead of a fixed column");
                Check(buttons.All(b => b.Width >= 110 && b.Width <= 280), "every single-row title respects width limits");
                Check(buttons[3].Text.Contains("&") && buttons[3].Width > buttons[0].Width, "ampersands remain literal measured title characters");
                var original = buttons.Select(b => b.Width).ToArray();
                UiAnyCall(strip, "ConfigureLayout", true, 3); strip.PerformLayout();
                Check(!strip.HorizontalScroll.Visible && buttons.All(b => b.Right <= strip.ClientSize.Width), "mixed-width wrapped titles stay inside the viewport");
                Check(buttons.Select(b => b.Top).Distinct().Count() >= 2 && buttons.Select(b => b.Width).SequenceEqual(original), "wrapping packs mixed widths without turning them into equal columns");
                for (int i = 1; i < buttons.Length; i++)
                    Check(buttons[i].Top != buttons[i - 1].Top || buttons[i].Left >= buttons[i - 1].Right,
                        "variable-width neighbors do not overlap " + i);
                form.Width = 720; form.PerformLayout(); strip.PerformLayout();
                Check(strip.VerticalScroll.Visible && !strip.HorizontalScroll.Visible && buttons.All(b => b.Right <= strip.ClientSize.Width), "narrow mixed-width wrap reserves the vertical scrollbar without horizontal overflow");
                form.Width = 1600; form.PerformLayout(); strip.PerformLayout();
                Check(!strip.VerticalScroll.Visible && !strip.HorizontalScroll.Visible && buttons.Select(b => b.Top).Distinct().Count() <= 3, "expanding removes an unneeded vertical scrollbar and blank wrapped rows");
                UiAnyCall(strip, "ConfigureLayout", false, 3); strip.PerformLayout();
                Check(buttons.Select(b => b.Width).SequenceEqual(original), "wrap toggle preserves each measured title width");
                host.RenameTab(host.Sessions[2], "短名"); UiCall(form, "RefreshTabs");
                Check(strip.Controls[2].Width == 110, "renaming a long title immediately shrinks its tab");
                using (var large = new Font(form.Font.FontFamily, 13F))
                {
                    int before = strip.Controls[1].Width; strip.Font = large;
                    Check(strip.Controls[1].Width > before && strip.Controls[1].Width <= 280, "font changes remeasure title widths within the same maximum");
                    strip.Font = form.Font;
                }
                Check(!api.Events.Any(e => e.StartsWith("restore")), "adaptive measurements and wrap never restore/reparent Explorer");
            }
        }
        [DllImport("user32.dll", EntryPoint = "SendMessageW")] private static extern IntPtr ChromeSend(IntPtr hwnd, int message, IntPtr wparam, IntPtr lparam);
        [DllImport("user32.dll", EntryPoint = "GetClientRect")] private static extern bool ChromeGetClientRect(IntPtr hwnd, out NativeRect rect);
        [DllImport("user32.dll", EntryPoint = "GetWindowLongPtrW")] private static extern IntPtr ChromeStyle(IntPtr hwnd, int index);
    }
}
