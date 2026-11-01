namespace ExplorerTabs.Core
{
    public enum GlobalHotkeyAction { ShowHost, MinimizeHost, HideToTray }

    /// <summary>A visible host behind another application must be brought forward, not hidden.</summary>
    public static class GlobalHotkeyPolicy
    {
        public static GlobalHotkeyAction Decide(bool visible, bool minimized, bool targetForeground, bool hideToTray)
        {
            if (!visible || minimized || !targetForeground) return GlobalHotkeyAction.ShowHost;
            return hideToTray ? GlobalHotkeyAction.HideToTray : GlobalHotkeyAction.MinimizeHost;
        }
    }
}
