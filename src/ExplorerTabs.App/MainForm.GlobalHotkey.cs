using System;
using System.Drawing;
using System.Windows.Forms;
using ExplorerTabs.Core;

namespace ExplorerTabs.App
{
    internal sealed partial class MainForm
    {
        private bool hotkeyRegistrationActive;
        private void ApplyGlobalHotkey(string? chord)
        {
            if (!hotkeyRegistrationActive || !IsHandleCreated) return;
            if (!globalHotkeys.Apply(Handle, chord, out string? error))
            {
                log.Write("Global hotkey registration failed; chord=" + (chord ?? "") + "; " + error);
                if (!string.IsNullOrWhiteSpace(chord)) status.Text = "全局快捷键未注册：" + error;
                return;
            }
            if (!string.IsNullOrWhiteSpace(chord)) log.Write("Global hotkey registered: " + globalHotkeys.Current);
        }
        private string? CheckGlobalHotkey(string chord)
        {
            if (!IsHandleCreated) return null;
            if (string.Equals(chord, globalHotkeys.Current, StringComparison.OrdinalIgnoreCase)) return null;
            return globalHotkeys.Probe(Handle, chord);
        }
        internal void ToggleHostVisibility()
        {
            if (closing || exitPreparing || IsDisposed || modalDepth > 0) return;
            switch (GlobalHotkeyPolicy.Decide(Visible, WindowState == FormWindowState.Minimized,
                TabKeyboardHook.IsTargetForeground(Handle, controller.Active), settings.HideToTrayOnGlobalHotkey))
            {
                case GlobalHotkeyAction.HideToTray: HideHostForHotkey(); break;
                case GlobalHotkeyAction.MinimizeHost: MinimizeHostForHotkey(); break;
                default: ShowHostFromHotkey(); break;
            }
        }
        private void MinimizeHostForHotkey()
        {
            CaptureWindowPlacement(true);
            try { controller.SetActiveVisible(false); } catch (Exception error) { log.Write("Minimize active window for hotkey failed", error); }
            WindowState = FormWindowState.Minimized;
            SyncShortcutScope();
            HideTrayIcon();
            log.Write("Host minimized by global hotkey");
        }
        private void HideHostForHotkey()
        {
            CaptureWindowPlacement(true);
            try { controller.SetActiveVisible(false); } catch (Exception error) { log.Write("Hide active window for hotkey failed", error); }
            Hide();
            SyncShortcutScope();
            ShowTrayIcon();
            log.Write("Host hidden by global hotkey");
        }
        private void ShowHostFromHotkey()
        {
            if (IsDisposed) return;
            if (!Visible) Show();
            RestoreHostWindowState();
            Activate();
            try { if (CanPresentExplorer) { controller.SetActiveVisible(true); controller.LayoutActive(CurrentBounds(), true); } }
            catch (Exception error) { log.Write("Restore active window from hotkey failed", error); }
            SyncShortcutScope();
            HideTrayIcon();
            log.Write("Host shown by global hotkey");
        }
        private void ShowTrayIcon()
        {
            if (tray == null)
            {
                tray = new NotifyIcon { Icon = applicationIcon ?? SystemIcons.Application, Text = "ExplorerTabs", Visible = false };
                tray.ContextMenuStrip = CreateTrayMenu();
                tray.DoubleClick += (_, __) => QueueMenuAction(ShowHostFromHotkey);
            }
            tray.Visible = true;
        }
        private ContextMenuStrip CreateTrayMenu()
        {
            var menu = new ContextMenuStrip(); HostTheme.StyleMenu(menu);
            PopulateTrayMenu(menu);
            menu.Opening += (_, __) => PopulateTrayMenu(menu);
            return menu;
        }
        private void PopulateTrayMenu(ContextMenuStrip menu)
        {
            while (menu.Items.Count > 0) menu.Items[0].Dispose();
            var show = menu.Items.Add("显示 ExplorerTabs", null, (_, __) => QueueMenuAction(ShowHostFromHotkey));
            show.Enabled = !closing && !exitPreparing && !disposingUi;
            menu.Items.Add(new ToolStripSeparator());
            AddExitMenuItems(menu);
        }
        private void HideTrayIcon() { if (tray != null) tray.Visible = false; }
        private void DisposeTray()
        {
            if (tray == null) return;
            tray.Visible = false;
            tray.ContextMenuStrip?.Dispose();
            tray.Dispose();
            tray = null;
        }
        protected override void OnHandleDestroyed(EventArgs e)
        {
            globalHotkeys.Unregister();
            base.OnHandleDestroyed(e);
        }
    }
}
