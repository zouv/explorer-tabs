using System;

namespace ExplorerTabs.Core
{
    /// <summary>Narrow embedded-Explorer input cases that the host must repair itself.
    /// A reparented Explorer keeps its own input thread; Ctrl+A can reach its rename edit
    /// without the shell translating select-all, so the host posts EM_SETSEL to the focused edit.</summary>
    public static class EmbeddedInputPolicy
    {
        public const int VirtualKeyA = 0x41;
        public const int ModifierControl = 1;

        /// <summary>Only a physical Ctrl+A key-down while the active embedded Explorer is eligible.</summary>
        public static bool IsSelectAll(int key, bool down, bool injected, int modifiers, bool eligible, bool hasActive) =>
            down && !injected && key == VirtualKeyA && modifiers == ModifierControl && eligible && hasActive;

        /// <summary>Standard edit and rich-edit window classes (Explorer's rename box lives in one).</summary>
        public static bool IsEditClass(string? className) =>
            !string.IsNullOrEmpty(className) && className!.IndexOf("Edit", StringComparison.OrdinalIgnoreCase) >= 0;
    }
}
