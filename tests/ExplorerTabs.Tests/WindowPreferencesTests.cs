using System;
using System.Drawing;
using System.IO;
using System.Linq;
using System.Windows.Forms;
using ExplorerTabs.Core;

namespace ExplorerTabs.Tests
{
    internal static partial class Program
    {
        private static void WindowPreferenceUnitTests()
        {
            for (int bits = 0; bits < 16; bits++)
            {
                bool visible = (bits & 1) != 0, minimized = (bits & 2) != 0;
                bool foreground = (bits & 4) != 0, tray = (bits & 8) != 0;
                var expected = !visible || minimized || !foreground ? GlobalHotkeyAction.ShowHost :
                    tray ? GlobalHotkeyAction.HideToTray : GlobalHotkeyAction.MinimizeHost;
                Check(GlobalHotkeyPolicy.Decide(visible, minimized, foreground, tray) == expected,
                    "global hotkey visibility/foreground/tray policy combination " + bits);
            }
            var normal = new Rectangle(-1300, 40, 1000, 700);
            var placement = new WindowPlacementTracker(normal, false);
            placement.Capture(new Rectangle(-32000, -32000, 160, 28), true, false);
            Check(placement.NormalBounds == normal && !placement.Maximized, "minimized iconic bounds never replace normal layout");
            placement.Capture(new Rectangle(0, 0, 1920, 1080), false, true);
            Check(placement.NormalBounds == normal && placement.Maximized, "maximize keeps the restore rectangle");
            placement.Capture(new Rectangle(-32000, -32000, 160, 28), true, false);
            Check(placement.NormalBounds == normal && placement.Maximized, "minimized exit preserves previous maximized state");
            var resized = new Rectangle(70, 80, 1200, 900);
            placement.Capture(resized, false, false);
            Check(placement.NormalBounds == resized && !placement.Maximized, "restoring and resizing replaces the last normal layout");
            placement.Capture(new Rectangle(-32000, -32000, 160, 28), false, false);
            Check(placement.NormalBounds == resized, "transient tiny window rectangles cannot destroy saved layout");
            string directory = Path.Combine(Path.GetTempPath(), "ExplorerTabs-preferences-" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(directory);
            var path = Path.Combine(directory, "settings.json"); var store = new AppSettingsStore(path);
            try
            {
                Check(!new AppSettings().HideToTrayOnGlobalHotkey, "new settings do not hide to tray by default");
                File.WriteAllText(path, "{}");
                Check(!store.Read()!.HideToTrayOnGlobalHotkey, "legacy settings without tray option default to false");
                var settings = new AppSettings { GlobalHotkey = "Ctrl+Alt+E", HideToTrayOnGlobalHotkey = true };
                Check(settings.Copy().HideToTrayOnGlobalHotkey, "settings copy preserves enabled tray preference");
                store.Save(settings); Check(store.Read()!.HideToTrayOnGlobalHotkey, "enabled tray preference roundtrips");
                settings.HideToTrayOnGlobalHotkey = false; store.Save(settings);
                Check(!store.Read()!.HideToTrayOnGlobalHotkey && !File.ReadAllText(path).Contains("HideToTrayOnGlobalHotkey"),
                    "unchecked preference roundtrips and retains legacy JSON shape");
            }
            finally { File.Delete(path); Directory.Delete(directory); }
        }

        private sealed class PreferenceSettingsStore : IAppSettingsStore
        {
            public AppSettings? Value;
            public int Saves;
            public AppSettings? Read() => Value?.Copy();
            public void Save(AppSettings settings) { Value = settings.Copy(); Saves++; }
        }

        private static void WindowPreferenceUiTests(string directory)
        {
            SettingsTrayPreferenceUiTests();
            var saved = SettingsLayoutPolicy.Save(new Rectangle(100, 50, 1000, 700));
            var expected = SettingsLayoutPolicy.Restore(saved, Screen.AllScreens.Select(s => s.WorkingArea).ToArray(), Bounds);
            var store = new PreferenceSettingsStore(); var service = new WorkspaceService();
            using (var form = (Form)Activator.CreateInstance(MainType, new HostController(new FakeWindows(), new FakeStore()),
                new AppLog(Path.Combine(directory, "preferences.log")), new AppSettings { Bounds = saved }, store, service)!)
            {
                MakeHidden(form);
                form.Scale(new SizeF(1.25F, 1.25F));
                Check(form.Size != expected.Size, "safe layout probe reproduces constructor bounds being scaled again");
                UiCall(form, "OnLoad", EventArgs.Empty);
                Check(form.Bounds == expected, "initial load restores physical saved bounds after automatic scaling");
                var resized = new Rectangle(expected.Location, new Size(expected.Width + 60, expected.Height + 30));
                form.Bounds = resized;
                UiCall(form, "OnLoad", EventArgs.Empty);
                Check(form.Bounds == resized, "subsequent load/show does not reset user-resized bounds");
                UiCall(form, "MinimizeHostForHotkey");
                var minimized = (AppSettings)UiCall(form, "CurrentSettings")!;
                Check(form.Visible && form.WindowState == FormWindowState.Minimized && UiField<object?>(form, "tray") == null,
                    "default hotkey background action minimizes without creating a tray icon");
                Check(minimized.Bounds.Right - minimized.Bounds.Left == resized.Width && minimized.Bounds.Bottom - minimized.Bounds.Top == resized.Height,
                    "closing while minimized captures last normal size instead of iconic bounds");
                MainType.GetMethod("OnClosing", PrivateInstance, null, new[] { typeof(object), typeof(FormClosingEventArgs) }, null)!
                    .Invoke(form, new object?[] { null, new FormClosingEventArgs(CloseReason.UserClosing, false) });
                Check(store.Saves == 1 && store.Value!.Bounds.Right - store.Value.Bounds.Left == resized.Width && store.Value.Bounds.Bottom - store.Value.Bounds.Top == resized.Height,
                    "normal close persists last user size through actual exit preparation");
            }
            using (var form = (Form)Activator.CreateInstance(MainType, new HostController(new FakeWindows(), new FakeStore()),
                new AppLog(Path.Combine(directory, "preferences-reopen.log")), store.Value!, store, service)!)
            {
                MakeHidden(form); UiCall(form, "OnLoad", EventArgs.Empty);
                Check(form.Size == new Size(store.Value!.Bounds.Right - store.Value.Bounds.Left, store.Value.Bounds.Bottom - store.Value.Bounds.Top), "next launch reuses the size persisted by close");
                MainType.GetField("restoringOnExit", PrivateInstance)!.SetValue(form, true);
                MainType.GetMethod("OnClosing", PrivateInstance, null, new[] { typeof(object), typeof(FormClosingEventArgs) }, null)!
                    .Invoke(form, new object?[] { null, new FormClosingEventArgs(CloseReason.UserClosing, false) });
                Check(store.Saves == 2, "detach-and-exit also commits window layout");
            }
            using (var form = (Form)Activator.CreateInstance(MainType, new HostController(new FakeWindows(), new FakeStore()),
                new AppLog(Path.Combine(directory, "preferences-maximized.log")), new AppSettings { Bounds = saved, Maximized = true }, null, service)!)
            {
                MakeHidden(form); UiCall(form, "OnLoad", EventArgs.Empty);
                var maximized = (AppSettings)UiCall(form, "CurrentSettings")!;
                Check(form.WindowState == FormWindowState.Maximized && maximized.Maximized,
                    "launch preserves saved maximized state after physical bounds restoration");
                UiCall(form, "MinimizeHostForHotkey");
                var minimized = (AppSettings)UiCall(form, "CurrentSettings")!;
                Check(minimized.Maximized && minimized.Bounds.Right - minimized.Bounds.Left == expected.Width &&
                    minimized.Bounds.Bottom - minimized.Bounds.Top == expected.Height,
                    "maximized host minimizes without losing restore size or maximized exit state");
                UiCall(form, "RestoreHostWindowState");
                Check(form.WindowState == FormWindowState.Maximized, "hotkey restore retains maximized state without foreground activation");
            }
        }
        private static void SettingsTrayPreferenceUiTests()
        {
            var type = MainType.Assembly.GetType("ExplorerTabs.App.SettingsDialog", true)!;
            foreach (float scale in new[] { 1F, 1.25F, 1.5F })
            using (var form = (Form)Activator.CreateInstance(type, new AppSettings { HideToTrayOnGlobalHotkey = true }, false, true)!)
            {
                MakeHidden(form);
                if (scale != 1F) form.Scale(new SizeF(scale, scale));
                var option = UiField<CheckBox>(form, "hideToTrayOnGlobalHotkey");
                var page = (TableLayoutPanel)option.Parent;
                var scroll = (Panel)page.Parent;
                var nav = Descendants(form).OfType<Button>().Single(b => b.Text == "快捷键");
                nav.PerformClick(); form.PerformLayout();
                Check(option.Checked && scroll.AutoScroll, "settings load enabled tray option in scrollable keys page at scale " + scale);
                var reset = Descendants(page).OfType<Button>().Single(b => b.Text == "恢复默认快捷键");
                scroll.ScrollControlIntoView(reset); form.PerformLayout();
                var resetBounds = scroll.RectangleToClient(reset.RectangleToScreen(reset.ClientRectangle));
                Check(scroll.ClientRectangle.Contains(resetBounds) && page.Controls.Cast<Control>().All(c => c.Bottom <= page.Height),
                    "all shortcut rows and reset control remain reachable at scale " + scale);
                option.Checked = false;
                ((Button)form.AcceptButton!).PerformClick();
                Check(form.DialogResult == DialogResult.OK && !((AppSettings)type.GetProperty("Result")!.GetValue(form)!).HideToTrayOnGlobalHotkey,
                    "settings can persist turning the tray option off at scale " + scale);
            }
        }
        private static System.Collections.Generic.IEnumerable<Control> Descendants(Control root)
        {
            foreach (Control child in root.Controls)
            {
                yield return child;
                foreach (var descendant in Descendants(child)) yield return descendant;
            }
        }
    }
}
