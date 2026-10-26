namespace ExplorerTabs.Core
{
    /// <summary>Independent Ctrl+T key pair; the proven Ctrl+Tab policy is unchanged.</summary>
    public sealed class NewTabShortcutPolicy
    {
        private bool keyDown, suppressUntilUp;
        public NewTabShortcutPolicy(bool initiallyDown = false) { keyDown = initiallyDown; }
        public NewTabDecision Handle(int key, bool down, bool injected, bool control, bool shift, bool alt, bool eligible)
        {
            if (key != 0x54 || injected) return new NewTabDecision(false, false);
            if (!down) { bool suppress = suppressUntilUp; keyDown = false; suppressUntilUp = false; return new NewTabDecision(suppress, false); }
            if (keyDown) return new NewTabDecision(suppressUntilUp, false);
            keyDown = true;
            if (!control || shift || alt || !eligible) return new NewTabDecision(false, false);
            suppressUntilUp = true; return new NewTabDecision(true, true);
        }
    }
    public readonly struct NewTabDecision
    {
        public bool Suppress { get; }
        public bool Create { get; }
        public NewTabDecision(bool suppress, bool create) { Suppress = suppress; Create = create; }
    }
}
