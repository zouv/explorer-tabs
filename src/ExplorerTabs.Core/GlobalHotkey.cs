using System;
using System.Collections.Generic;
using System.Linq;

namespace ExplorerTabs.Core
{
    /// <summary>系统级“呼出 / 隐藏”快捷键的解析与规范化。</summary>
    public static class GlobalHotkeyDefinition
    {
        public const int Control = 1, Alt = 2, Shift = 4;
        private static readonly Dictionary<string, int> KeysByName = BuildKeys();
        private static readonly Dictionary<int, string> NamesByKey = KeysByName.ToDictionary(pair => pair.Value, pair => pair.Key);

        public static bool IsValid(string? chord) => TryParse(chord, out _, out _);

        /// <summary>规范化为 Ctrl+Alt+E 形式；空值表示停用，其它非法值抛出 ArgumentException。</summary>
        public static string Normalize(string? chord)
        {
            if (string.IsNullOrWhiteSpace(chord)) return "";
            if (!TryParse(chord, out int modifiers, out int key))
                throw new ArgumentException("全局快捷键必须包含 Ctrl / Alt / Shift 修饰键，以及字母、数字或 F1–F12 主键。");
            return Format(modifiers, key);
        }

        public static bool TryParse(string? chord, out int modifiers, out int key)
        {
            modifiers = 0; key = 0;
            if (chord == null || string.IsNullOrWhiteSpace(chord)) return false;
            var tokens = chord.Split('+').Select(token => token.Trim()).Where(token => token.Length > 0).ToArray();
            if (tokens.Length < 2) return false;
            for (int i = 0; i < tokens.Length - 1; i++)
            {
                switch (tokens[i].ToLowerInvariant())
                {
                    case "ctrl": case "control": modifiers |= Control; break;
                    case "alt": modifiers |= Alt; break;
                    case "shift": modifiers |= Shift; break;
                    default: return false;
                }
            }
            if (modifiers == 0 || !KeysByName.TryGetValue(tokens[tokens.Length - 1], out key)) return false;
            // Alt+F4 closes windows; registering it would break normal window management.
            if (modifiers == Alt && key == 0x73) return false;
            return true;
        }

        public static string Format(int modifiers, int key)
        {
            if (modifiers == 0 || !NamesByKey.ContainsKey(key)) return "";
            var parts = new List<string>(4);
            if ((modifiers & Control) != 0) parts.Add("Ctrl");
            if ((modifiers & Alt) != 0) parts.Add("Alt");
            if ((modifiers & Shift) != 0) parts.Add("Shift");
            parts.Add(NamesByKey[key]);
            return string.Join("+", parts);
        }

        public static bool Conflicts(string chord, IEnumerable<HostShortcut> bindings)
        {
            string canonical = Normalize(chord);
            if (canonical.Length == 0) return false;
            return bindings.Any(binding => !string.IsNullOrWhiteSpace(binding.Chord) &&
                string.Equals(binding.Chord.Trim(), canonical, StringComparison.OrdinalIgnoreCase));
        }

        /// <summary>WinForms key codes for A-Z / 0-9 / F1-F12 are already their Win32 virtual-key codes.</summary>
        public static string FromVirtualKey(int virtualKey, int modifiers) => Format(modifiers, virtualKey);

        private static Dictionary<string, int> BuildKeys()
        {
            var map = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
            for (char c = 'A'; c <= 'Z'; c++) map[c.ToString()] = c;
            for (char c = '0'; c <= '9'; c++) map[c.ToString()] = c;
            for (int i = 1; i <= 12; i++) map["F" + i] = 0x70 + i - 1;
            return map;
        }
    }
}
