using System;
using System.Drawing;

namespace ExplorerTabs.Core
{
    /// <summary>Keep the last normal rectangle and non-minimized state; iconic bounds are not a layout.</summary>
    public sealed class WindowPlacementTracker
    {
        public Rectangle NormalBounds { get; private set; }
        public bool Maximized { get; private set; }
        public WindowPlacementTracker(Rectangle normalBounds, bool maximized)
        { NormalBounds = normalBounds; Maximized = maximized; }

        public void Capture(Rectangle bounds, bool minimized, bool maximized)
        {
            if (minimized) return;
            Maximized = maximized;
            if (!maximized && bounds.Width >= 720 && bounds.Height >= 500 &&
                Math.Abs((long)bounds.X) < 1000000 && Math.Abs((long)bounds.Y) < 1000000)
                NormalBounds = bounds;
        }
    }
}
