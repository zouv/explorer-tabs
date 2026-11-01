using System;
using System.Drawing;
using System.Linq;
using System.Windows.Forms;
using ExplorerTabs.Core;

namespace ExplorerTabs.App
{
    internal sealed partial class MainForm
    {
        private WindowPlacementTracker windowPlacement = null!;
        private bool windowPlacementLoaded;
        private bool restoringWindowPlacement;

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
                windowPlacementLoaded = true;
            }
            finally { restoringWindowPlacement = false; }
        }

        private void CaptureWindowPlacement(bool allowBeforeLoad = false)
        {
            if (restoringWindowPlacement || (!windowPlacementLoaded && !allowBeforeLoad)) return;
            windowPlacement.Capture(Bounds, WindowState == FormWindowState.Minimized, WindowState == FormWindowState.Maximized);
        }

        private void RestoreHostWindowState()
        {
            if (WindowState == FormWindowState.Minimized)
                WindowState = windowPlacement.Maximized ? FormWindowState.Maximized : FormWindowState.Normal;
        }
    }
}
