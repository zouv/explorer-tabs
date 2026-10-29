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
            if (closing || exitPreparing || IsDisposed) return;
            if (Visible && WindowState != FormWindowState.Minimized) HideHostForHotkey();
            else ShowHostFromHotkey();
        }
        private void HideHostForHotkey()
        {
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
            if (WindowState == FormWindowState.Minimized) WindowState = FormWindowState.Normal;
            Activate();
            try { controller.SetActiveVisible(true); controller.LayoutActive(CurrentBounds(), true); }
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
                var menu = new ContextMenuStrip(); HostTheme.StyleMenu(menu);
                menu.Items.Add("显示 ExplorerTabs", null, (_, __) => QueueMenuAction(ShowHostFromHotkey));
                menu.Items.Add(new ToolStripSeparator());
                menu.Items.Add("关闭（分离所有窗口）", null, (_, __) => QueueMenuAction(CloseRestoringWindows));
                menu.Items.Add("关闭", null, (_, __) => QueueMenuAction(RequestExitByClosingSessions));
                tray.ContextMenuStrip = menu;
                tray.DoubleClick += (_, __) => QueueMenuAction(ShowHostFromHotkey);
            }
            tray.Visible = true;
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
