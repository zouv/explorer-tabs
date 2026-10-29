using System;
using System.Runtime.InteropServices;
using ExplorerTabs.Core;

namespace ExplorerTabs.App
{
    /// <summary>Registers the single system-wide show/hide hotkey on the MainForm HWND.</summary>
    internal sealed class GlobalHotkeyRegistration : IDisposable
    {
        internal const int HotkeyId = 0x4554;
        private const int ProbeId = 0x4555;
        private const uint ModAlt = 0x0001, ModControl = 0x0002, ModShift = 0x0004, ModNoRepeat = 0x4000;
        private const int ErrorHotkeyAlreadyRegistered = 1409;
        private IntPtr window;
        private string? chord;
        private bool registered;
        public string? Current => registered ? chord : null;

        /// <summary>Applies a new chord; returns false and restores the previous registration on failure.</summary>
        public bool Apply(IntPtr hwnd, string? value, out string? error)
        {
            error = null;
            string canonical;
            try { canonical = GlobalHotkeyDefinition.Normalize(value); }
            catch (ArgumentException issue) { error = issue.Message; return false; }
            if (canonical.Length == 0) { Unregister(); return true; }
            if (registered && window == hwnd && string.Equals(chord, canonical, StringComparison.OrdinalIgnoreCase)) return true;
            string? previous = registered ? chord : null;
            Unregister();
            window = hwnd;
            if (Register(hwnd, canonical)) { chord = canonical; registered = true; return true; }
            error = Describe(Marshal.GetLastWin32Error());
            if (!string.IsNullOrEmpty(previous) && Register(hwnd, previous!)) { chord = previous; registered = true; }
            else Unregister();
            return false;
        }

        /// <summary>Tests whether a chord can be registered, without disturbing the active hotkey.</summary>
        public string? Probe(IntPtr hwnd, string value)
        {
            if (!GlobalHotkeyDefinition.TryParse(value, out int modifiers, out int key)) return "全局快捷键格式无效。";
            if (!RegisterHotKey(hwnd, ProbeId, ToNative(modifiers) | ModNoRepeat, (uint)key))
                return Describe(Marshal.GetLastWin32Error());
            UnregisterHotKey(hwnd, ProbeId);
            return null;
        }

        public void Unregister()
        {
            if (registered && window != IntPtr.Zero) UnregisterHotKey(window, HotkeyId);
            registered = false; chord = null; window = IntPtr.Zero;
        }
        public void Dispose() => Unregister();

        private static bool Register(IntPtr hwnd, string value)
        {
            if (!GlobalHotkeyDefinition.TryParse(value, out int modifiers, out int key)) return false;
            return RegisterHotKey(hwnd, HotkeyId, ToNative(modifiers) | ModNoRepeat, (uint)key);
        }
        private static uint ToNative(int modifiers) =>
            ((modifiers & GlobalHotkeyDefinition.Control) != 0 ? ModControl : 0) |
            ((modifiers & GlobalHotkeyDefinition.Alt) != 0 ? ModAlt : 0) |
            ((modifiers & GlobalHotkeyDefinition.Shift) != 0 ? ModShift : 0);
        private static string Describe(int error) => error == ErrorHotkeyAlreadyRegistered
            ? "该全局快捷键已被其他程序占用。"
            : "无法注册全局快捷键（错误 " + error + "）。";

        [DllImport("user32.dll", SetLastError = true)]
        [return: MarshalAs(UnmanagedType.Bool)] private static extern bool RegisterHotKey(IntPtr hwnd, int id, uint modifiers, uint vk);
        [DllImport("user32.dll", SetLastError = true)]
        [return: MarshalAs(UnmanagedType.Bool)] private static extern bool UnregisterHotKey(IntPtr hwnd, int id);
    }
}
