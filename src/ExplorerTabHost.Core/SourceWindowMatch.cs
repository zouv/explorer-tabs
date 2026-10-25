namespace ExplorerTabHost.Core
{
    /// <summary>Match an exact browser or its view, not a common host/root, title, PID or order.</summary>
    public static class SourceWindowMatch
    {
        public static bool IsExact(long source, long automation, long browser, long view, bool viewIsChild)
        {
            if (source == 0) return false;
            // An available view is authoritative: reject stale automation HWND pointing to another tab.
            if (view != 0) return view == source || viewIsChild;
            if (browser != 0) return browser == source;
            return automation == source;
        }
    }
}
