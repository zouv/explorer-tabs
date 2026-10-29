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
                    var toolbarRight = UiField<FlowLayoutPanel>(form, "commandToolbarRight");
                    var toolbarHost = toolbar.Parent!;
                    Check(UiField<object?>(form, "keyboard") == null, "hidden chrome does not register global keyboard hook");
                    var hotkeys = UiField<object>(form, "globalHotkeys");
                    Check((string?)hotkeys.GetType().GetProperty("Current")!.GetValue(hotkeys) == null, "hidden chrome does not register the global hotkey");
                    Check(form.Icon != null && form.Icon.Width > 0, "application icon is loaded from embedded multi-size ICO");
                    long style = ChromeStyle(form.Handle, -16).ToInt64();
                    Check((style & 0x00C00000) == 0 && (style & 0x000F0000) == 0x000F0000, "own host HWND has no caption but retains resizing/system/min/max styles");
                    Check(form.FormBorderStyle == FormBorderStyle.None, "main form removes the independent standard title bar");
                    Check(form.Padding.Left == 0 && form.Padding.Right == 0 && form.Padding.Bottom == 0 && form.Padding.Top > 0,
                        "only the top resize band is reserved inside the client");
                    Check(form.Padding.Top == (int)UiCall(form, "ChromePixels", 3)! && form.BackColor == chrome.BackColor,
                        "top visual band is 3 logical px and uses the tab-strip colour");
                    var stripTop = form.PointToScreen(new Point(100, form.Padding.Top + 1));
                    long stripPacked = ((stripTop.Y & 0xffff) << 16) | (stripTop.X & 0xffff);
                    Check(ChromeSend(strip.Handle, 0x0084, IntPtr.Zero, new IntPtr(stripPacked)) == new IntPtr(12),
                        "tab strip forwards its top edge as HTTOP to keep the resize zone wide");
                    var welcome = content.Controls.Cast<Control>().Single(c => c.GetType().Name == "WelcomePanel");
                    var welcomeButtons = welcome.Controls.OfType<Button>().ToArray();
                    form.Height = 900; form.PerformLayout(); welcome.PerformLayout();
                    int welcomeTop = welcomeButtons[0].Top;
                    form.Height = 700; form.PerformLayout(); welcome.PerformLayout();
                    Check(welcomeButtons[0].Top < welcomeTop, "welcome actions follow window resizing");
                    form.Height = 800; form.PerformLayout();
                    var windowRect = NativeWindowApi.ReadRect(form.Handle).ToRectangle();
                    IntPtr minMax = Marshal.AllocHGlobal(Marshal.SizeOf(typeof(TestMinMaxInfo)));
                    try
                    {
                        Marshal.StructureToPtr(new TestMinMaxInfo(), minMax, false);
                        ChromeSend(form.Handle, 0x0024, IntPtr.Zero, minMax);
                        var info = (TestMinMaxInfo)Marshal.PtrToStructure(minMax, typeof(TestMinMaxInfo))!;
                        var work = Screen.FromHandle(form.Handle).WorkingArea;
                        var monitor = Screen.FromHandle(form.Handle).Bounds;
                        int side = (form.Width - form.ClientSize.Width) / 2;
                        int bottom = form.Height - form.ClientSize.Height;
                        Check(info.MaxSize.X == work.Width + side * 2 && info.MaxSize.Y == work.Height + side + bottom &&
                            info.MaxPosition.X == work.Left - monitor.Left - side && info.MaxPosition.Y == work.Top - monitor.Top - side,
                            "maximized window overhangs the work area by the native frame instead of covering the taskbar");
                        Check(info.MinTrackSize.X == form.MinimumSize.Width && info.MinTrackSize.Y == form.MinimumSize.Height,
                            "min track size still follows the managed minimum");
                    }
                    finally { Marshal.FreeHGlobal(minMax); }
                    Check(ChromeGetClientRect(form.Handle, out var frameClient) &&
                        windowRect.Width > frameClient.ToRectangle().Width && windowRect.Height > frameClient.ToRectangle().Height,
                        "native sizing frame stays outside the client area so children cannot cover it");
                    long leftPacked = (((windowRect.Top + windowRect.Height / 2) & 0xffff) << 16) | ((windowRect.Left + 1) & 0xffff);
                    Check(ChromeSend(form.Handle, 0x0084, IntPtr.Zero, new IntPtr(leftPacked)) == new IntPtr(10),
                        "native left resize border answers WM_NCHITTEST");
                    long topPacked = (((windowRect.Top + 1) & 0xffff) << 16) | ((windowRect.Left + windowRect.Width / 2) & 0xffff);
                    Check(ChromeSend(form.Handle, 0x0084, IntPtr.Zero, new IntPtr(topPacked)) == new IntPtr(12),
                        "native top resize border answers WM_NCHITTEST");
                    var insideScreen = form.PointToScreen(new Point(100, 100));
                    long insidePacked = ((insideScreen.Y & 0xffff) << 16) | (insideScreen.X & 0xffff);
                    Check(ChromeSend(form.Handle, 0x0084, IntPtr.Zero, new IntPtr(insidePacked)) == new IntPtr(1),
                        "client area answers WM_NCHITTEST as HTCLIENT");
                    for (int cycle = 0; cycle < 4; cycle++)
                    {
                        Check(ChromeSend(form.Handle, 0x0086, IntPtr.Zero, IntPtr.Zero) != IntPtr.Zero, "nonclient deactivation keeps the native frame state " + cycle);
                        ChromeSend(form.Handle, 0x0086, new IntPtr(1), IntPtr.Zero);
                        Check(ChromeGetClientRect(form.Handle, out var client) && client.ToRectangle().Size == frameClient.ToRectangle().Size,
                            "activation cycles keep the native client inset stable " + cycle);
                    }
                    Check((ChromeStyle(form.Handle, -16).ToInt64() & 0x000F0000) == 0x000F0000, "activation cycles preserve resize/system styles");
                    var brandArea = chrome.Controls.OfType<Panel>().Single(p => !(p is FlowLayoutPanel) && p != strip);
                    var captions = chrome.Controls.OfType<FlowLayoutPanel>().Single(p => p != strip);
                    var capButtons = captions.Controls.OfType<Button>().ToArray();
                    Check(capButtons.Length == 3 && capButtons.All(b => b.AccessibleName!.Length > 0 && b.TabStop), "minimize/maximize/close remain accessible interactive buttons");
                    Check(capButtons.Last().Text.Contains("关闭所有"), "caption close closes every session window and exits");
                    foreach (int width in new[] { 720, 1100, 1600 })
                    {
                        form.Width = width; form.PerformLayout(); strip.PerformLayout();
                        Check(chrome.Top == form.Padding.Top && toolbarHost.Top == chrome.Bottom && content.Top == toolbarHost.Bottom, "browser chrome and toolbar dock after the top resize band at width " + width);
                        using (var image = new Bitmap(toolbar.Width, toolbar.Height))
                        {
                            toolbar.DrawToBitmap(image, toolbar.ClientRectangle);
                            Check(image.GetPixel(toolbar.Width / 2, toolbar.Height - 1).ToArgb() == Color.FromArgb(215, 222, 231).ToArgb(),
                                "low-saturation one-pixel divider is painted inside the toolbar without adding a blank row at width " + width);
                        }
                        Check(chrome.Height <= 36 && toolbar.Height <= 36, "empty chrome plus toolbar occupies at most 72 client pixels at width " + width);
                        Check(strip.Right <= captions.Left && strip.Left >= brandArea.Right && captions.Right <= chrome.ClientSize.Width, "tabs never overlap brand or caption controls at width " + width);
                        Check(capButtons.All(b => b.Top == 0 && b.Bottom <= 36) && brandArea.Controls[0].Top == 0 && brandArea.Controls[0].Height <= 36, "window buttons and brand stay in the first tab row at width " + width);
                        Check(toolbar.Controls.Cast<Control>().Where(c => c.Visible).All(c => c.Bottom <= toolbar.Height && c.Right <= toolbar.ClientSize.Width) &&
                            toolbarRight.Controls.Cast<Control>().Where(c => c.Visible).All(c => c.Bottom <= toolbarRight.Height && c.Right <= toolbarRight.ClientSize.Width),
                            "all command glyphs fit without a second toolbar row at width " + width);
                        Check(toolbarRight.Left > toolbar.Left && toolbarRight.Right <= toolbarHost.ClientSize.Width,
                            "dialog-opening commands keep their own right-aligned group at width " + width);
                    }
                    var tips = UiField<ToolTip>(form, "chromeTips");
                    var newButton = UiField<Button>(form, "newTab");
                    var leftCommands = toolbar.Controls.OfType<Button>().Select(b => b.AccessibleName).ToArray();
                    Check(leftCommands.Length >= 2 && leftCommands[0] == "加入窗口…" && leftCommands[1] == "分离窗口…",
                        "join and detach window actions stay adjacent in the left group");
                    Check(toolbarRight.Controls.OfType<Button>().Select(b => b.AccessibleName).SequenceEqual(new[] { "搜索标签…" }),
                        "search remains the right-aligned dialog command");
                    foreach (Button button in toolbar.Controls.OfType<Button>().Concat(toolbarRight.Controls.OfType<Button>()).Concat(new[] { newButton }))
                        Check(!string.IsNullOrEmpty(tips.GetToolTip(button)) && !string.IsNullOrEmpty(button.AccessibleName), "command tooltip/accessibility exists: " + button.Text);
                    Check(newButton.Parent is FlowLayoutPanel stripParent && stripParent == strip &&
                        strip.Controls.Cast<Control>().Last() == newButton,
                        "new-tab action flows directly after the last tab rather than in the toolbar");
                    var settings = new AppSettings(); settings.KeyBindings.Single(k => k.Command == HostCommand.NewTab).Chord = "Ctrl+Shift+T";
                    settings.KeyBindings.Single(k => k.Command == HostCommand.ReopenTab).Chord = "";
                    UiCall(form, "ApplySettings", settings);
                    Check(tips.GetToolTip(newButton).Contains("Ctrl+Shift+T") && !tips.GetToolTip(newButton).Contains(" · Ctrl+T"), "tooltip reflects configured shortcut rather than hardcoded default");
                    settings.ShortcutsEnabled = false; UiCall(form, "ApplySettings", settings);
                    Check(!tips.GetToolTip(newButton).Contains("Ctrl+"), "disabled keyboard bindings are not advertised in toolbar tooltips");
                    for (int i = 1; i <= 20; i++) { api.Alive.Add(i); Add(host, i); }
                    host.Activate(host.Sessions[0], Bounds); form.Width = 720; UiCall(form, "RefreshTabs"); form.PerformLayout();
                    Check((bool)UiCall(strip, "HasHorizontalScrollbar")! && !(bool)UiCall(strip, "HasVerticalScrollbar")! && chrome.Height <= 40, "single-row overflow keeps the compact chrome and only a thin horizontal bar");
                    Check(content.Controls.Cast<Control>().All(c => !c.Visible), "welcome actions disappear when real Explorer tabs are present");
                    UiAnyCall(strip, "ConfigureLayout", true, 3); form.PerformLayout();
                    Check(chrome.Height == strip.Height && chrome.Height <= 100 && toolbarHost.Top == chrome.Bottom, "wrap height updates enclosing chrome without overlapping toolbar");
                    Check(capButtons.All(b => b.Top == 0) && brandArea.Controls[0].Height <= 36, "wrapped tabs do not vertically center window controls or brand");
                    Check((bool)UiCall(strip, "HasVerticalScrollbar")! && !(bool)UiCall(strip, "HasHorizontalScrollbar")!, "wrapped browser chrome keeps vertical overflow");
                    Check(!api.Events.Any(e => e.StartsWith("restore")), "browser chrome layout never reparents or restores managed Explorer");
                    UiCall(form, "SetCreationControls", true);
                    Check(!newButton.Enabled, "async preparation immediately disables new commands");
                    Check(PumpUntil(() => UiField<Button>(form, "cancelCreation").Visible, 1200),
                        "async preparation exposes the cancel glyph after the reveal delay");
                    UiCall(form, "SetCreationControls", false);
                    Check(!UiField<Button>(form, "cancelCreation").Visible, "cancel glyph hides immediately when preparation ends");
                    UiCall(form, "SetCreationControls", true);
                    UiCall(form, "SetCreationControls", false);
                    PumpFor(700);
                    Check(!UiField<Button>(form, "cancelCreation").Visible, "quick preparation never flashes the cancel glyph");
                    settings.ShortcutsEnabled = true; UiCall(form, "ApplySettings", settings);
                    using (var appMenu = (ContextMenuStrip)UiCall(form, "CreateAppMenu")!)
                    {
                        var appTexts = appMenu.Items.OfType<ToolStripMenuItem>().Select(i => i.Text).ToArray();
                        Check(appTexts.SequenceEqual(new[] { "设置…", "关闭（分离所有窗口）", "关闭", "关于" }),
                            "app menu exposes settings, restore-and-exit, close, and about last in its own group");
                        var settingsItem = appMenu.Items.OfType<ToolStripMenuItem>().Single(i => i.Text == "设置…");
                        Check(settingsItem.ShortcutKeyDisplayString == "Ctrl+," && settingsItem.ShowShortcutKeys && !settingsItem.Text.Contains("Ctrl"),
                            "settings shortcut uses the right-aligned shortcut column instead of inline text");
                        Check(appMenu.Items[appMenu.Items.Count - 1] is ToolStripMenuItem lastAppItem && lastAppItem.Text == "关于" &&
                            appMenu.Items[appMenu.Items.Count - 2] is ToolStripSeparator,
                            "about is separated at the bottom of the app menu");
                        Check(!appTexts.Any(t => t.Contains("移动") || t.Contains("大小") || t.Contains("最小化") || t.Contains("最大化")),
                            "app menu no longer duplicates window layout commands");
                        var restoreItem = appMenu.Items.OfType<ToolStripMenuItem>().Single(i => i.Text == "关闭（分离所有窗口）");
                        Check(restoreItem.ToolTipText.Contains("分离所有会话窗口"),
                            "restore-and-exit menu item explains that windows stay open");
                        var closeItem = appMenu.Items.OfType<ToolStripMenuItem>().Single(i => i.Text == "关闭");
                        Check(closeItem.ShortcutKeyDisplayString == "Alt+F4" && closeItem.ToolTipText.Contains("关闭所有会话窗口") &&
                            closeItem.ShowShortcutKeys && !closeItem.Text.Contains("Alt+F4"),
                            "app menu close advertises Alt+F4 in the right-aligned shortcut column and explains the close-all behaviour");
                    }
                }
                var aboutType = Assembly.Load("ExplorerTabs").GetType("ExplorerTabs.App.AboutDialog", true)!;
                using (var about = (Form)Activator.CreateInstance(aboutType)!)
                {
                    MakeHidden(about);
                    Check(about.Text.Contains("ExplorerTabs") && about.Padding.All == 1 && HasWindowRegion(about),
                        "about dialog keeps the shared 1px border ring and rounded region");
                    var version = (Label)aboutType.GetField("versionLabel", BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(about)!;
                    Check(version.Text.Contains(Assembly.Load("ExplorerTabs").GetName().Version!.ToString(3)),
                        "about dialog shows the assembly version");
                    var logo = (PictureBox)aboutType.GetField("logo", BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(about)!;
                    Check(logo.Image != null, "about dialog shows the embedded application icon");
                }
                AdaptiveTabWidthsSmoke(dir);
                TrailingActionWrapSmoke(dir);
                BoundsRestoreSmoke(dir);
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
                var strip = UiField<FlowLayoutPanel>(form, "tabs"); var buttons = TabControls(strip);
                Check(buttons[0].Width == 110 && buttons[2].Width == 280, "adaptive titles clamp to 110 / 280 logical pixels at 96 DPI");
                Check(buttons[1].Width > buttons[0].Width && buttons[1].Width < buttons[2].Width, "medium Chinese title uses its measured width instead of a fixed column");
                Check(buttons.All(b => b.Width >= 110 && b.Width <= 280), "every single-row title respects width limits");
                Check(buttons[3].Text.Contains("&") && buttons[3].Width > buttons[0].Width, "ampersands remain literal measured title characters");
                var original = buttons.Select(b => b.Width).ToArray();
                UiAnyCall(strip, "ConfigureLayout", true, 3); strip.PerformLayout();
                Check(!(bool)UiCall(strip, "HasHorizontalScrollbar")! && buttons.All(b => b.Right <= strip.ClientSize.Width), "mixed-width wrapped titles stay inside the viewport");
                Check(buttons.Select(b => b.Top).Distinct().Count() >= 2 && buttons.Select(b => b.Width).SequenceEqual(original), "wrapping packs mixed widths without turning them into equal columns");
                for (int i = 1; i < buttons.Length; i++)
                    Check(buttons[i].Top != buttons[i - 1].Top || buttons[i].Left >= buttons[i - 1].Right,
                        "variable-width neighbors do not overlap " + i);
                form.Width = 720; form.PerformLayout(); strip.PerformLayout();
                Check((bool)UiCall(strip, "HasVerticalScrollbar")! && !(bool)UiCall(strip, "HasHorizontalScrollbar")! && buttons.All(b => b.Right <= strip.ClientSize.Width), "narrow mixed-width wrap reserves the vertical scrollbar without horizontal overflow");
                form.Width = 1600; form.PerformLayout(); strip.PerformLayout();
                Check(!(bool)UiCall(strip, "HasVerticalScrollbar")! && !(bool)UiCall(strip, "HasHorizontalScrollbar")! && buttons.Select(b => b.Top).Distinct().Count() <= 3, "expanding removes an unneeded vertical scrollbar and blank wrapped rows");
                UiAnyCall(strip, "ConfigureLayout", false, 3); strip.PerformLayout();
                Check(buttons.Select(b => b.Width).SequenceEqual(original), "wrap toggle preserves each measured title width");
                host.RenameTab(host.Sessions[2], "短名"); UiCall(form, "RefreshTabs");
                Check(TabControls(strip)[2].Width == 110, "renaming a long title immediately shrinks its tab");
                using (var large = new Font(form.Font.FontFamily, 13F))
                {
                    int before = TabControls(strip)[1].Width; strip.Font = large;
                    Check(TabControls(strip)[1].Width > before && TabControls(strip)[1].Width <= 280, "font changes remeasure title widths within the same maximum");
                    strip.Font = form.Font;
                }
                Check(!api.Events.Any(e => e.StartsWith("restore")), "adaptive measurements and wrap never restore/reparent Explorer");
            }
        }
        private static void TrailingActionWrapSmoke(string directory)
        {
            // A nearly full wrapped row must never leave the new-tab action alone on the next
            // line: shrink the last tab when it can, otherwise move the last tab down with it.
            var api = new FakeWindows(); var host = new HostController(api, new FakeStore());
            for (int i = 1; i <= 10; i++) { api.Alive.Add(i); Add(host, i); }
            foreach (var session in host.Sessions) host.RenameTab(session, new string('W', 90));
            using (var form = WorkspaceForm(host, new WorkspaceService(), directory, new WorkspaceStore(), new AppSettings { WrapTabs = true, MaximumTabRows = 3 }))
            {
                form.Width = 760; UiCall(form, "RefreshTabs"); form.PerformLayout();
                var strip = UiField<FlowLayoutPanel>(form, "tabs"); strip.PerformLayout();
                var tabs = TabControls(strip); var trailing = UiField<Button>(form, "newTab");
                Check(tabs.Length == 10 && tabs.Last().Width < 280 && tabs.Last().Width >= 110 &&
                    trailing.Top == tabs.Last().Top && trailing.Left >= tabs.Last().Right,
                    "a nearly full row shrinks the last tab so the new-tab action still hugs it [" +
                    string.Join(" | ", tabs.Select(t => t.Width + "@" + t.Left + "," + t.Top)) +
                    " action=" + trailing.Left + "," + trailing.Top + " strip=" + strip.ClientSize + "]");
                foreach (var session in host.Sessions) host.RenameTab(session, "D:");
                UiCall(form, "RefreshTabs"); strip.PerformLayout();
                tabs = TabControls(strip);
                Check(tabs.All(t => t.Width == 110) && tabs.Last().Top != tabs[0].Top &&
                    trailing.Top == tabs.Last().Top && trailing.Left >= tabs.Last().Right,
                    "at minimum width the last tab moves down with the action instead of leaving it alone");
            }
        }
        private static void BoundsRestoreSmoke(string directory)
        {
            var api = new FakeWindows(); var host = new HostController(api, new FakeStore());
            var settings = new AppSettings { Bounds = new NativeRect { Left = 120, Top = 90, Right = 920, Bottom = 690 } };
            using (var form = WorkspaceForm(host, new WorkspaceService(), directory, new WorkspaceStore(), settings))
                Check(form.Bounds == new Rectangle(120, 90, 800, 600),
                    "saved window bounds initialise the next launch size");
        }
        [StructLayout(LayoutKind.Sequential)] private struct TestPoint { public int X, Y; }
        [StructLayout(LayoutKind.Sequential)] private struct TestMinMaxInfo { public TestPoint Reserved, MaxSize, MaxPosition, MinTrackSize, MaxTrackSize; }
        [DllImport("user32.dll", EntryPoint = "SendMessageW")] private static extern IntPtr ChromeSend(IntPtr hwnd, int message, IntPtr wparam, IntPtr lparam);
        [DllImport("user32.dll", EntryPoint = "GetClientRect")] private static extern bool ChromeGetClientRect(IntPtr hwnd, out NativeRect rect);
        [DllImport("user32.dll", EntryPoint = "GetWindowLongPtrW")] private static extern IntPtr ChromeStyle(IntPtr hwnd, int index);
    }
}
