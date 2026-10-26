using System;

namespace ExplorerTabs.Core
{
    /// <summary>Pure key-pair policy. No Win32 calls, I/O, window mutations or ordinary-key storage.</summary>
    public sealed class TabShortcutPolicy
    {
        private bool tabDown;
        private bool suppressTabUntilUp;
        public TabShortcutPolicy(bool tabInitiallyDown = false) { tabDown = tabInitiallyDown; }
        public ShortcutDecision Handle(int key, bool down, bool injected, bool control, bool shift, bool alt, bool eligible)
        {
            if (key != 9 || injected) return new ShortcutDecision(false, 0);
            if (!down)
            {
                bool suppress = suppressTabUntilUp;
                suppressTabUntilUp = false;
                tabDown = false;
                return new ShortcutDecision(suppress, 0);
            }
            // Only the first physical down decides. A plain Tab or an outside chord stays untouched
            // even if Ctrl is pressed, the checkbox enabled or the foreground changes while held.
            if (tabDown) return new ShortcutDecision(suppressTabUntilUp, 0);
            tabDown = true;
            if (!eligible || !control || alt) return new ShortcutDecision(false, 0);
            suppressTabUntilUp = true;
            return new ShortcutDecision(true, shift ? -1 : 1);
        }
    }

    public readonly struct ShortcutDecision
    {
        public bool Suppress { get; }
        public int Direction { get; }
        public ShortcutDecision(bool suppress, int direction) { Suppress = suppress; Direction = direction; }
    }
}
