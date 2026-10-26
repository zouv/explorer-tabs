using System;
using System.ComponentModel;
using System.Runtime.InteropServices;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Forms;
using ExplorerTabHost.Core;

namespace ExplorerTabHost.App
{
    /// <summary>
    /// WH_KEYBOARD_LL executes on this app's dedicated pumping thread, never inside Explorer.
    /// The callback only evaluates the chord and queues work; no COM, disk, process queries or SetParent.
    /// </summary>
    internal sealed class TabKeyboardHook : IDisposable
    {
        private readonly Action<int> switchTab;
        private readonly Action createTab;
        private HostShortcutPolicy actionPolicy = new HostShortcutPolicy();
        private readonly Action<HostCommand>? command;
        private readonly Thread thread;
        private readonly TaskCompletionSource<Exception?> ready = new TaskCompletionSource<Exception?>(TaskCreationOptions.RunContinuationsAsynchronously);
        private readonly HookCallback callback;

        private Control? dispatcher;
        private ApplicationContext? context;
        private IntPtr hook;
        private Scope scope = Scope.Disabled;
        private bool leftControl, rightControl, leftShift, rightShift, leftAlt, rightAlt, leftWin, rightWin;
        private int disposeRequested;

        public TabKeyboardHook(Action<int> switchTab, Action createTab, Action<HostCommand>? command = null)
        {
            this.switchTab = switchTab; this.createTab = createTab; this.command = command;
            callback = OnKey;
            thread = new Thread(Pump) { IsBackground = true, Name = "ExplorerTabHost keyboard hook" };
            thread.SetApartmentState(ApartmentState.STA);
            thread.Start();
            try
            {
                if (!ready.Task.Wait(5000)) throw new TimeoutException("键盘快捷键线程启动超时。");
                var error = ready.Task.GetAwaiter().GetResult();
                if (error != null) throw new InvalidOperationException("快捷键注册失败，仍可鼠标切换。", error);
            }
            catch
            {
                // A timed-out thread can still finish starting. Request cancellation before returning.
                Dispose();
                throw;
            }
        }

        public void UpdateScope(IntPtr host, WindowSession? active, bool enabled, bool canSwitch = true, bool canCreate = false, System.Collections.Generic.IReadOnlyList<HostShortcut>? bindings = null)
        {
            Volatile.Write(ref scope, enabled && host != IntPtr.Zero
                ? new Scope(host, active?.State == SessionState.Attached ? active.Identity.Hwnd : IntPtr.Zero,
                    active?.Identity.ProcessId ?? 0, active?.Identity.ThreadId ?? 0, canSwitch && active?.State == SessionState.Attached, canCreate, bindings)
                : Scope.Disabled);
        }

        internal static bool IsTargetForeground(IntPtr host, WindowSession? active)
        {
            return Eligible(new Scope(host, active?.State == SessionState.Attached ? active.Identity.Hwnd : IntPtr.Zero,
                active?.Identity.ProcessId ?? 0, active?.Identity.ThreadId ?? 0));
        }

        private static bool Eligible(Scope current)
        {
            if (current.Host == IntPtr.Zero) return false;
            IntPtr foreground = GetForegroundWindow();
            uint foregroundThread = GetWindowThreadProcessId(foreground, out _);
            var gui = new GuiThreadInfo { Size = (uint)Marshal.SizeOf<GuiThreadInfo>() };
            if (!GetGUIThreadInfo(foregroundThread, ref gui) || (gui.Flags & 0x1C) != 0) return false;
            IntPtr root = GetAncestor(foreground, 2); // GA_ROOT excludes separately owned dialogs.
            if (root == current.Host) return true;
            if (root != current.Active) return false;
            uint tid = GetWindowThreadProcessId(root, out uint pid);
            return tid == current.ThreadId && pid == current.ProcessId;
        }

        private void Pump()
        {
            try
            {
                using (var control = new Control())
                using (var pump = new ApplicationContext())
                {
                    context = pump;
                    _ = control.Handle;
                    Volatile.Write(ref dispatcher, control);
                    if (Volatile.Read(ref disposeRequested) != 0) return;
                    leftControl = IsDown(0xA2); rightControl = IsDown(0xA3);
                    leftShift = IsDown(0xA0); rightShift = IsDown(0xA1);
                    leftAlt = IsDown(0xA4); rightAlt = IsDown(0xA5);
                    leftWin = IsDown(0x5B); rightWin = IsDown(0x5C);
                    actionPolicy = new HostShortcutPolicy(System.Linq.Enumerable.Where(HostShortcutPolicy.SupportedKeys, IsDown));
                    try
                    {
                        hook = SetWindowsHookEx(13, callback, GetModuleHandle(null), 0); // WH_KEYBOARD_LL
                        if (hook == IntPtr.Zero) throw new Win32Exception(Marshal.GetLastWin32Error());
                        ready.TrySetResult(null);
                        // Dispose either prevents Run or queues ExitThread on this dispatcher.
                        if (Volatile.Read(ref disposeRequested) == 0) Application.Run(pump);
                    }
                    finally
                    {
                        if (hook != IntPtr.Zero) { UnhookWindowsHookEx(hook); hook = IntPtr.Zero; }
                    }
                }
            }
            catch (Exception error) { ready.TrySetResult(error); }
            finally
            {
                Volatile.Write(ref dispatcher, null);
                context = null;
                ready.TrySetResult(new OperationCanceledException("快捷键线程已停止。"));
            }
        }

        private IntPtr OnKey(int code, IntPtr message, IntPtr data)
        {
            if (code < 0 || Volatile.Read(ref disposeRequested) != 0) return CallNextHookEx(hook, code, message, data);
            try
            {
                int kind = message.ToInt32();
                bool down = kind == 0x100 || kind == 0x104;
                bool up = kind == 0x101 || kind == 0x105;
                if (!down && !up) return CallNextHookEx(hook, code, message, data);
                var key = Marshal.PtrToStructure<KeyboardData>(data);
                bool injected = (key.Flags & 0x10) != 0;
                if (!injected)
                {
                    switch (key.VirtualKey)
                    {
                        case 0xA2: leftControl = down; break;
                        case 0xA3: rightControl = down; break;
                        case 0xA0: leftShift = down; break;
                        case 0xA1: rightShift = down; break;
                        case 0xA4: leftAlt = down; break;
                        case 0xA5: rightAlt = down; break;
                        case 0x5B: leftWin = down; break;
                        case 0x5C: rightWin = down; break;
                    }
                }
                var current = Volatile.Read(ref scope);
                int modifiers = ((leftControl || rightControl) ? 1 : 0) | ((leftShift || rightShift) ? 2 : 0) |
                    ((leftAlt || rightAlt) ? 4 : 0) | ((leftWin || rightWin) ? 8 : 0);
                var decision = actionPolicy.Handle(key.VirtualKey, down, injected, modifiers, Eligible(current), current.CanCreate,
                    current.CanSwitch, current.Active != IntPtr.Zero, current.Bindings);
                if (decision.Command != HostCommand.None)
                {
                    if (command != null) command(decision.Command);
                    else if (decision.Command == HostCommand.NewTab) createTab();
                    else if (decision.Command == HostCommand.NextTab || decision.Command == HostCommand.NextPage) switchTab(1);
                    else if (decision.Command == HostCommand.PreviousTab || decision.Command == HostCommand.PreviousPage) switchTab(-1);
                }
                if (decision.Suppress) return new IntPtr(1);
            }
            catch { /* Never allow exceptions to cross the native hook boundary. */ }
            return CallNextHookEx(hook, code, message, data);
        }

        private static bool IsDown(int key) => (GetAsyncKeyState(key) & 0x8000) != 0; // Initialise BEFORE hook installation only.

        public void Dispose()
        {
            if (Interlocked.Exchange(ref disposeRequested, 1) != 0) return;
            Volatile.Write(ref scope, Scope.Disabled);
            var control = Volatile.Read(ref dispatcher);
            if (control != null)
            {
                try { control.BeginInvoke(new Action(() => context?.ExitThread())); }
                catch (InvalidOperationException) { } // Pump is not started yet or already stopped.
            }
            if (Thread.CurrentThread != thread) thread.Join(1000);
            // A delayed startup observes disposeRequested; no wait-handle disposal race or thread abort.
        }

        private sealed class Scope
        {
            public static readonly Scope Disabled = new Scope(IntPtr.Zero, IntPtr.Zero, 0, 0);
            public IntPtr Host { get; }
            public IntPtr Active { get; }
            public int ProcessId { get; }
            public uint ThreadId { get; }
            public bool CanSwitch { get; }
            public bool CanCreate { get; }
            public System.Collections.Generic.IReadOnlyList<HostShortcut> Bindings { get; }
            public Scope(IntPtr host, IntPtr active, int pid, uint tid, bool canSwitch = false, bool canCreate = false, System.Collections.Generic.IReadOnlyList<HostShortcut>? bindings = null)
            { Host = host; Active = active; ProcessId = pid; ThreadId = tid; CanSwitch = canSwitch; CanCreate = canCreate; Bindings = bindings == null ? HostShortcutCatalog.Defaults() : System.Linq.Enumerable.ToArray(System.Linq.Enumerable.Select(bindings, b => b.Copy())); }
        }
        [StructLayout(LayoutKind.Sequential)]
        private struct KeyboardData
        {
            public int VirtualKey;
            public uint ScanCode;
            public uint Flags;
            public uint Time;
            public UIntPtr ExtraInfo;
        }
        [StructLayout(LayoutKind.Sequential)]
        private struct GuiThreadInfo
        {
            public uint Size, Flags;
            public IntPtr Active, Focus, Capture, MenuOwner, MoveSize, Caret;
            public NativeRect CaretRect;
        }
        [DllImport("user32.dll")] [return: MarshalAs(UnmanagedType.Bool)]
        private static extern bool GetGUIThreadInfo(uint threadId, ref GuiThreadInfo info);
        private delegate IntPtr HookCallback(int code, IntPtr message, IntPtr data);
        [DllImport("user32.dll", SetLastError = true)] private static extern IntPtr SetWindowsHookEx(int id, HookCallback callback, IntPtr module, uint threadId);
        [DllImport("user32.dll", SetLastError = true)] [return: MarshalAs(UnmanagedType.Bool)] private static extern bool UnhookWindowsHookEx(IntPtr hook);
        [DllImport("user32.dll")] private static extern IntPtr CallNextHookEx(IntPtr hook, int code, IntPtr message, IntPtr data);
        [DllImport("user32.dll")] private static extern short GetAsyncKeyState(int key);
        [DllImport("user32.dll")] private static extern IntPtr GetForegroundWindow();
        [DllImport("user32.dll")] private static extern IntPtr GetAncestor(IntPtr hwnd, uint flags);
        [DllImport("user32.dll")] private static extern uint GetWindowThreadProcessId(IntPtr hwnd, out uint pid);
        [DllImport("kernel32.dll", CharSet = CharSet.Unicode)] private static extern IntPtr GetModuleHandle(string? name);
    }
}
