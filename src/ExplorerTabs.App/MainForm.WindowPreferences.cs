using System;
using System.Drawing;
using System.Linq;
using System.Runtime.InteropServices;
using System.Windows.Forms;
using ExplorerTabs.Core;

namespace ExplorerTabs.App
{
    internal sealed partial class MainForm
    {
        private WindowPlacementTracker windowPlacement = null!;
        private bool windowPlacementLoaded;
        private bool restoringWindowPlacement;
        private bool restoringNormalPlacement;
        private FormWindowState previousWindowState = FormWindowState.Normal;

        private void InitializeWindowPlacement()
        {
            // Pre-position on the saved monitor so initial DPI selection uses the right screen.
            if (settings.Bounds.Right != settings.Bounds.Left)
            {
                StartPosition = FormStartPosition.Manual;
                Bounds = RestoreSavedWindowBounds();
            }
            windowPlacement = new WindowPlacementTracker(Bounds, settings.Maximized);
            if (windowPlacement.Maximized) WindowState = FormWindowState.Maximized;
        }

        private Rectangle RestoreSavedWindowBounds() => SettingsLayoutPolicy.Restore(settings.Bounds,
            Screen.AllScreens.Select(s => s.WorkingArea).ToArray(), Bounds);

        protected override void OnLoad(EventArgs e)
        {
            base.OnLoad(e);
            if (windowPlacementLoaded) return;
            restoringWindowPlacement = true;
            try
            {
                // WinForms has completed initial automatic scaling. Stored bounds are physical
                // pixels: applying them only in the constructor scales them a second time.
                WindowState = FormWindowState.Normal;
                if (settings.Bounds.Right != settings.Bounds.Left) Bounds = RestoreSavedWindowBounds();
                windowPlacement = new WindowPlacementTracker(Bounds, settings.Maximized);
                if (windowPlacement.Maximized) WindowState = FormWindowState.Maximized;
                previousWindowState = WindowState;
                windowPlacementLoaded = true;
            }
            finally { restoringWindowPlacement = false; }
        }

        private void CaptureWindowPlacement(bool allowBeforeLoad = false)
        {
            if (restoringNormalPlacement || restoringWindowPlacement || (!windowPlacementLoaded && !allowBeforeLoad)) return;
            windowPlacement.Capture(Bounds, WindowState == FormWindowState.Minimized, WindowState == FormWindowState.Maximized);
        }

        /// <summary>
        /// The custom chrome extends the client into the top frame. WinForms' minimize /
        /// restore path adds that band to the outer height on every restore, so the window
        /// grows each time it returns to the foreground. Reapply the tracked normal rectangle
        /// after the restore has settled; a direct SetWindowPos avoids the WinForms bounds
        /// conversion that caused the growth.
        /// </summary>
        private void TrackWindowStateForRestore()
        {
            if (!windowPlacementLoaded) return;
            var state = WindowState;
            if (state == FormWindowState.Minimized) { previousWindowState = state; return; }
            if (state == FormWindowState.Normal && previousWindowState != FormWindowState.Normal)
            {
                previousWindowState = state;
                var target = windowPlacement.NormalBounds;
                if (restoringNormalPlacement || !IsHandleCreated || target.Width < 720 || target.Height < 500) return;
                restoringNormalPlacement = true;
                try { BeginInvoke(new Action(() => ApplyNormalPlacementAfterRestore(target))); }
                catch (InvalidOperationException) { restoringNormalPlacement = false; }
                return;
            }
            previousWindowState = state;
        }

        private void ApplyNormalPlacementAfterRestore(Rectangle target)
        {
            try
            {
                if (IsDisposed || closing || WindowState != FormWindowState.Normal || Bounds == target) return;
                log.Write($"Restore normal window bounds; current={Bounds}; tracked={target}");
                SetWindowPos(Handle, IntPtr.Zero, target.Left, target.Top, target.Width, target.Height, SwpNoZOrder | SwpNoActivate);
            }
            catch (Exception error) { log.Write("Restore normal window bounds failed", error); }
            finally { restoringNormalPlacement = false; }
        }

        private const uint SwpNoZOrder = 0x0004, SwpNoActivate = 0x0010;
        [DllImport("user32.dll")] private static extern bool SetWindowPos(IntPtr hwnd, IntPtr insertAfter, int x, int y, int width, int height, uint flags);

        private void RestoreHostWindowState()
        {
            if (WindowState == FormWindowState.Minimized)
                WindowState = windowPlacement.Maximized ? FormWindowState.Maximized : FormWindowState.Normal;
        }
    }
}
