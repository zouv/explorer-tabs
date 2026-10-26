using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Diagnostics;
using System.Drawing;
using System.IO;
using System.Runtime.InteropServices;
using System.Text;

namespace ExplorerTabs.Core
{
    public sealed class NativeWindowApi : IWindowApi
    {
        private readonly ITaskbarService taskbar;
        private readonly Action<string>? warning;
        private readonly Action<string>? trace;
        private readonly IWindowRepaintService repaint;
        public NativeWindowApi(ITaskbarService? taskbar = null, Action<string>? warning = null,
            Action<string>? trace = null, IWindowRepaintService? repaint = null)
        {
            this.taskbar = taskbar ?? new ShellTaskbarService(trace);
            this.warning = warning;
            this.trace = trace;
            this.repaint = repaint ?? new WindowRepaintService();
        }

        public static WindowIdentity Inspect(IntPtr hwnd)
        {
            if (!NativeMethods.IsWindow(hwnd)) throw new InvalidOperationException("窗口已不存在。");
            uint thread = NativeMethods.GetWindowThreadProcessId(hwnd, out uint pid);
            var process = ReadProcessIdentity(pid);
            var text = new StringBuilder(1024);
            var className = new StringBuilder(256);
            NativeMethods.GetWindowText(hwnd, text, text.Capacity);
            NativeMethods.GetClassName(hwnd, className, className.Capacity);
            return new WindowIdentity
            {
                Handle = hwnd.ToInt64(), ProcessId = checked((int)pid), ThreadId = thread,
                ProcessStartedUtcTicks = process.StartedUtcTicks, ImagePath = process.ImagePath,
                ClassName = className.ToString(), Title = text.ToString()
            };
        }

        private static (long StartedUtcTicks, string ImagePath) ReadProcessIdentity(uint pid)
        {
            // Limited-query handles avoid Process.MainModule's expensive remote module enumeration.
            using (var process = NativeMethods.OpenProcess(0x1000, false, pid))
            {
                if (process.IsInvalid) throw new Win32Exception(Marshal.GetLastWin32Error(), "OpenProcess(identity)");
                Check(NativeMethods.GetProcessTimes(process, out long created, out _, out _, out _), "GetProcessTimes");
                var path = new StringBuilder(32768);
                uint size = (uint)path.Capacity;
                Check(NativeMethods.QueryFullProcessImageName(process, 0, path, ref size), "QueryFullProcessImageName");
                return (DateTime.FromFileTimeUtc(created).Ticks, path.ToString());
            }
        }

        public static bool IsExplorer(WindowIdentity identity) =>
            (identity.ClassName == "CabinetWClass" || identity.ClassName == "ExploreWClass") &&
            string.Equals(identity.ImagePath, Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.Windows),
                "explorer.exe"), StringComparison.OrdinalIgnoreCase);

        public static IReadOnlyList<WindowIdentity> DiscoverExplorers()
        {
            var result = new List<WindowIdentity>();
            NativeMethods.EnumWindows((hwnd, _) =>
            {
                try
                {
                    // Filtering by class first avoids opening every process on the desktop.
                    var name = new StringBuilder(256);
                    NativeMethods.GetClassName(hwnd, name, name.Capacity);
                    if ((name.ToString() != "CabinetWClass" && name.ToString() != "ExploreWClass") ||
                        !NativeMethods.IsWindowVisible(hwnd) || NativeMethods.GetWindow(hwnd, NativeMethods.GwOwner) != IntPtr.Zero)
                        return true;
                    var identity = Inspect(hwnd);
                    if (IsExplorer(identity)) result.Add(identity);
                }
                catch (Exception error) when (error is Win32Exception || error is InvalidOperationException ||
                                              error is ArgumentException || error is OverflowException)
                { /* Inaccessible or disappearing windows are not eligible. */ }
                return true;
            }, IntPtr.Zero);
            return result;
        }

        public bool Matches(WindowIdentity identity)
        {
            // A failed process query is UNKNOWN, not proof the window disappeared. Propagate
            // access errors so a recovery journal is not discarded while a child still exists.
            if (!NativeMethods.IsWindow(identity.Hwnd)) return false;
            uint thread = NativeMethods.GetWindowThreadProcessId(identity.Hwnd, out uint pid);
            if (thread != identity.ThreadId || pid != identity.ProcessId) return false;
            var className = new StringBuilder(256);
            NativeMethods.GetClassName(identity.Hwnd, className, className.Capacity);
            if (className.ToString() != identity.ClassName) return false;
            try
            {
                var process = ReadProcessIdentity(pid);
                return process.StartedUtcTicks == identity.ProcessStartedUtcTicks &&
                    string.Equals(process.ImagePath, identity.ImagePath, StringComparison.OrdinalIgnoreCase);
            }
            catch (Win32Exception)
            {
                if (!NativeMethods.IsWindow(identity.Hwnd)) return false;
                // Access denied and other unknown states MUST retain recovery data.
                throw;
            }
        }

        public WindowSnapshot Capture(WindowIdentity identity)
        {
            RequireMatch(identity);
            long style = ReadLong(identity.Hwnd, NativeMethods.GwlStyle);
            var placement = new WindowPlacement { Length = (uint)Marshal.SizeOf(typeof(WindowPlacement)) };
            Check(NativeMethods.GetWindowPlacement(identity.Hwnd, ref placement), "GetWindowPlacement");
            return new WindowSnapshot
            {
                Identity = identity, Style = style,
                ExtendedStyle = ReadLong(identity.Hwnd, NativeMethods.GwlExStyle),
                Parent = (style & NativeMethods.WsChild) != 0 ? NativeMethods.GetParent(identity.Hwnd).ToInt64() : 0,
                Owner = NativeMethods.GetWindow(identity.Hwnd, NativeMethods.GwOwner).ToInt64(),
                WasVisible = NativeMethods.IsWindowVisible(identity.Hwnd), Placement = placement
            };
        }

        public void ValidateAttach(WindowSnapshot snapshot, IntPtr host, HostingMode mode)
        {
            RequireMatch(snapshot.Identity);
            if (!NativeMethods.IsWindow(host)) throw new InvalidOperationException("宿主容器尚未创建。");
            if (snapshot.Parent != 0 || snapshot.Owner != 0 || (snapshot.Style & NativeMethods.WsChild) != 0)
                throw new InvalidOperationException("只接管无 owner 的顶层 Explorer 窗口。");
            if ((snapshot.ExtendedStyle & NativeMethods.WsExTopmost) != 0)
                throw new InvalidOperationException("暂不接管置顶窗口，请先取消置顶。");
            if (NativeMethods.IsHungAppWindow(snapshot.Identity.Hwnd) ||
                NativeMethods.SendMessageTimeout(snapshot.Identity.Hwnd, 0, IntPtr.Zero, IntPtr.Zero,
                    0x0002 | 0x0020, 250, out _) == IntPtr.Zero)
                throw new InvalidOperationException("Explorer 未响应，暂不接管。");
            if (mode == HostingMode.Embedded)
            {
                var targetContext = NativeMethods.GetWindowDpiAwarenessContext(snapshot.Identity.Hwnd);
                var hostContext = NativeMethods.GetWindowDpiAwarenessContext(host);
                if (targetContext == IntPtr.Zero || hostContext == IntPtr.Zero ||
                    !NativeMethods.AreDpiAwarenessContextsEqual(targetContext, hostContext))
                    throw new InvalidOperationException("Explorer 与宿主的 DPI awareness 不一致，已拒绝 SetParent。请用顶层分组模式，或重启宿主自动匹配 DPI。");
            }
        }

        public void Attach(WindowSnapshot snapshot, IntPtr host, Rectangle bounds)
        {
            RequireMatch(snapshot.Identity);
            TraceState("attach.before", snapshot, true);
            // Normalise while hidden. Preserve original maximise/minimise state solely in the snapshot.
            HideForStructure(snapshot);
            var normal = snapshot.Placement;
            normal.ShowCommand = 4; // SW_SHOWNOACTIVATE
            normal.Flags = 0;
            Check(NativeMethods.SetWindowPlacement(snapshot.Identity.Hwnd, ref normal), "SetWindowPlacement(normalise)");
            HideForStructure(snapshot);
            if (snapshot.Mode == HostingMode.Embedded)
            {
                long childStyle = (snapshot.Style & ~(NativeMethods.WsPopup | NativeMethods.WsCaption |
                    NativeMethods.WsThickFrame | NativeMethods.WsMinimizeBox | NativeMethods.WsMaximizeBox |
                    NativeMethods.WsSysMenu | 0x20000000L | 0x01000000L | 0x10000000L)) | NativeMethods.WsChild | NativeMethods.WsClipSiblings;
                WriteLong(snapshot.Identity.Hwnd, NativeMethods.GwlStyle, childStyle);
                WriteLong(snapshot.Identity.Hwnd, NativeMethods.GwlExStyle, snapshot.ExtendedStyle & ~NativeMethods.WsExAppWindow);
                ChangeParent(snapshot.Identity.Hwnd, host);
                if (NativeMethods.GetParent(snapshot.Identity.Hwnd) != host)
                    throw new InvalidOperationException("SetParent 后父窗口校验失败。");
            }
            FrameChanged(snapshot.Identity.Hwnd);
            Layout(snapshot, bounds);
            if (snapshot.Mode == HostingMode.Embedded)
                Trace($"Native: embedded.chrome HWND=0x{snapshot.Identity.Handle:X} titleInset={MeasureEmbeddedTitleInset(snapshot.Identity.Hwnd)} viewport=({bounds.X},{bounds.Y},{bounds.Width},{bounds.Height})");
            if (snapshot.Mode == HostingMode.Embedded)
                UpdateTaskbar(snapshot, false);
            TraceState("attach.after", snapshot, true);
        }

        public void Layout(WindowSnapshot snapshot, Rectangle bounds)
        {
            RequireMatch(snapshot.Identity);
            if (bounds.Width < 1 || bounds.Height < 1) return;
            // The Windows 10 Ribbon can retain its own title band even with WS_CAPTION
            // removed. Move only this embedded root through the parent viewport; never edit
            // Explorer's Ribbon children, placement snapshot, or ordinary/grouped windows.
            int inset = snapshot.Mode == HostingMode.Embedded && IsExplorer(snapshot.Identity)
                ? MeasureEmbeddedTitleInset(snapshot.Identity.Hwnd) : 0;
            bounds = EmbeddedViewportBounds(bounds, inset);
            Check(NativeMethods.SetWindowPos(snapshot.Identity.Hwnd, IntPtr.Zero, bounds.X, bounds.Y,
                bounds.Width, bounds.Height, NativeMethods.SwpNoZOrder | NativeMethods.SwpNoActivate), "SetWindowPos(layout)");
        }

        private static Rectangle EmbeddedViewportBounds(Rectangle bounds, int inset) =>
            new Rectangle(bounds.X, bounds.Y - inset, bounds.Width, bounds.Height + inset);

        private static int MeasureEmbeddedTitleInset(IntPtr hwnd)
        {
            // Fail open on unknown Explorer layouts: never guess a fixed title height.
            if (!NativeMethods.GetClientRect(hwnd, out var client) || !NativeMethods.GetWindowRect(hwnd, out var root) ||
                client.Right < 1 || root.Right - root.Left != client.Right || root.Bottom - root.Top != client.Bottom) return 0;
            int dpi = (int)NativeMethods.GetDpiForWindow(hwnd);
            int limit = Math.Max(80, (int)Math.Round(80 * Math.Max(96, dpi) / 96F));
            int inset = 0, inspected = 0;
            // Direct children only; do not enumerate shell file views or send cross-thread messages.
            for (IntPtr child = NativeMethods.GetWindow(hwnd, 5); child != IntPtr.Zero && inspected++ < 256;
                child = NativeMethods.GetWindow(child, 2)) // GW_CHILD / GW_HWNDNEXT
            {
                var name = new StringBuilder(128); NativeMethods.GetClassName(child, name, name.Capacity);
                if (name.ToString() != "UIRibbonCommandBarDock" || !NativeMethods.GetWindowRect(child, out var ribbon)) continue;
                int gap = ribbon.Top - root.Top;
                if (gap <= 0 || gap > limit || ribbon.Bottom <= ribbon.Top || ribbon.Left != root.Left ||
                    ribbon.Right != root.Right || ribbon.Bottom >= root.Bottom) continue;
                // Horizontal top dock must contain the actual Ribbon command bar, not a side pane.
                bool commandBar = false; int nestedCount = 0;
                for (IntPtr nested = NativeMethods.GetWindow(child, 5); nested != IntPtr.Zero && nestedCount++ < 64;
                    nested = NativeMethods.GetWindow(nested, 2))
                {
                    NativeMethods.GetClassName(nested, name, name.Capacity);
                    if (name.ToString() == "UIRibbonCommandBar") { commandBar = true; break; }
                }
                if (commandBar && (inset == 0 || gap < inset)) inset = gap;
            }
            return inset;
        }

        public void Show(WindowSnapshot snapshot, bool visible, bool raise)
        {
            RequireMatch(snapshot.Identity);
            uint flags = NativeMethods.SwpNoMove | NativeMethods.SwpNoSize | NativeMethods.SwpNoActivate |
                (visible ? NativeMethods.SwpShowWindow : NativeMethods.SwpHideWindow);
            if (!raise || !visible) flags |= NativeMethods.SwpNoZOrder;
            Check(NativeMethods.SetWindowPos(snapshot.Identity.Hwnd, NativeMethods.HwndTop, 0, 0, 0, 0, flags), "SetWindowPos(show/hide)");
        }

        public void Restore(WindowSnapshot snapshot)
        {
            var clock = Stopwatch.StartNew();
            var phaseClock = Stopwatch.StartNew();
            string stage = "identity";
            try
            {
                RequireMatch(snapshot.Identity);
                TraceState("restore.before", snapshot, true);
                if (snapshot.Mode == HostingMode.Embedded)
                {
                    long currentStyle = ReadLong(snapshot.Identity.Hwnd, NativeMethods.GwlStyle);
                    long currentParent = (currentStyle & NativeMethods.WsChild) != 0
                        ? NativeMethods.GetParent(snapshot.Identity.Hwnd).ToInt64() : 0;
                    if (currentParent != snapshot.Parent && currentParent != snapshot.HostHandle)
                        throw new InvalidOperationException("窗口已属于其他容器，拒绝自动恢复。");
                }
                stage = "hide";
                phaseClock.Restart();
                HideForStructure(snapshot);
                TraceRestorePhase(snapshot, stage, phaseClock);
                TraceState("restore.hidden", snapshot);
                if (snapshot.Mode == HostingMode.Embedded)
                {
                    stage = "parent";
                    phaseClock.Restart();
                    RequireMatch(snapshot.Identity);
                    ChangeParent(snapshot.Identity.Hwnd, new IntPtr(snapshot.Parent));
                    TraceRestorePhase(snapshot, stage, phaseClock);
                    TraceState("restore.parent", snapshot);
                    stage = "styles-hidden";
                    phaseClock.Restart();
                    RequireMatch(snapshot.Identity);
                    // Restoring WS_VISIBLE here would bypass a real hidden -> shown transition.
                    WriteLong(snapshot.Identity.Hwnd, NativeMethods.GwlStyle, RestorePresentation.HiddenStyle(snapshot.Style));
                    WriteLong(snapshot.Identity.Hwnd, NativeMethods.GwlExStyle, snapshot.ExtendedStyle);
                    FrameChanged(snapshot.Identity.Hwnd);
                    TraceRestorePhase(snapshot, stage, phaseClock);
                    TraceState("restore.styles-hidden", snapshot);
                }
                stage = "placement";
                phaseClock.Restart();
                RequireMatch(snapshot.Identity);
                var placement = RestorePresentation.Placement(snapshot.Placement, snapshot.WasVisible);
                Check(NativeMethods.SetWindowPlacement(snapshot.Identity.Hwnd, ref placement), "SetWindowPlacement(restore)");
                TraceRestorePhase(snapshot, stage, phaseClock);
                TraceState("restore.placement", snapshot);
                stage = "show";
                phaseClock.Restart();
                RequireMatch(snapshot.Identity);
                // ShowWindow's return is PREVIOUS visibility, not an error code.
                bool previousVisible = NativeMethods.ShowWindow(snapshot.Identity.Hwnd, snapshot.WasVisible ? 8 : 0); // SW_SHOWNA / SW_HIDE
                Trace($"restore.ShowWindow HWND=0x{snapshot.Identity.Handle:X} command={(snapshot.WasVisible ? 8 : 0)} previousVisible={previousVisible}");
                TraceRestorePhase(snapshot, stage, phaseClock);
                TraceState("restore.shown", snapshot);
                stage = "verify";
                phaseClock.Restart();
                RequireMatch(snapshot.Identity);
                long actualParent = (ReadLong(snapshot.Identity.Hwnd, NativeMethods.GwlStyle) & NativeMethods.WsChild) != 0
                    ? NativeMethods.GetParent(snapshot.Identity.Hwnd).ToInt64() : 0;
                if (actualParent != snapshot.Parent) throw new InvalidOperationException("恢复后父窗口不符。");
                if (NativeMethods.GetWindow(snapshot.Identity.Hwnd, NativeMethods.GwOwner).ToInt64() != snapshot.Owner)
                    throw new InvalidOperationException("恢复后 owner 不符。");
                if (NativeMethods.IsWindowVisible(snapshot.Identity.Hwnd) != snapshot.WasVisible)
                    throw new InvalidOperationException("恢复后显隐状态不符。");
                if (snapshot.Mode == HostingMode.Embedded &&
                    ((ReadLong(snapshot.Identity.Hwnd, NativeMethods.GwlStyle) & ~NativeMethods.WsVisible) != (snapshot.Style & ~NativeMethods.WsVisible) ||
                    ReadLong(snapshot.Identity.Hwnd, NativeMethods.GwlExStyle) != snapshot.ExtendedStyle))
                    throw new InvalidOperationException("恢复后窗口样式或扩展样式不符。");
                TraceRestorePhase(snapshot, stage, phaseClock);
                stage = "presentation";
                phaseClock.Restart();
                if (snapshot.Mode == HostingMode.Embedded)
                {
                    RequestRepaint(snapshot);
                    if (snapshot.WasVisible) UpdateTaskbar(snapshot, true);
                }
                TraceRestorePhase(snapshot, stage, phaseClock);
                TraceState("restore.after", snapshot, true);
                Trace($"restore.complete HWND=0x{snapshot.Identity.Handle:X} elapsed-ms={clock.Elapsed.TotalMilliseconds:F2} visual-result=not-verified");
            }
            catch (Exception error)
            {
                Trace($"restore.failed HWND=0x{snapshot.Identity.Handle:X} stage={stage} phase-ms={phaseClock.Elapsed.TotalMilliseconds:F2} elapsed-ms={clock.Elapsed.TotalMilliseconds:F2} HRESULT=0x{error.HResult:X8} error={error.Message}");
                TraceState("restore.failed-state", snapshot, true);
                throw;
            }
        }

        private void TraceRestorePhase(WindowSnapshot snapshot, string stage, Stopwatch clock) =>
            Trace($"restore.phase HWND=0x{snapshot.Identity.Handle:X} stage={stage} phase-ms={clock.Elapsed.TotalMilliseconds:F2}");

        private void HideForStructure(WindowSnapshot snapshot)
        {
            RequireMatch(snapshot.Identity);
            bool previousVisible = NativeMethods.ShowWindow(snapshot.Identity.Hwnd, 0);
            Trace($"structure.ShowWindow HWND=0x{snapshot.Identity.Handle:X} command=0 previousVisible={previousVisible}");
            if (NativeMethods.IsWindowVisible(snapshot.Identity.Hwnd))
                throw new InvalidOperationException("窗口没有隐藏，停止修改 parent / style。");
        }

        private void RequestRepaint(WindowSnapshot snapshot)
        {
            try
            {
                RequireMatch(snapshot.Identity);
                repaint.Request(snapshot.Identity);
                Trace($"restore.repaint-request HWND=0x{snapshot.Identity.Handle:X} flags=0x485 accepted=true paint-complete=unknown");
            }
            catch (Exception error)
            {
                Trace($"restore.repaint-request HWND=0x{snapshot.Identity.Handle:X} accepted=false HRESULT=0x{error.HResult:X8} error={error.Message}");
                Warn("窗口结构已恢复，但重绘请求失败：" + snapshot.Identity.Title + "；" + error.Message);
            }
        }

        private void TraceState(string stage, WindowSnapshot snapshot, bool children = false)
        {
            if (trace == null) return;
            try
            {
                Trace($"{stage} mode={snapshot.Mode} expectedStyle=0x{snapshot.Style:X8} expectedExStyle=0x{snapshot.ExtendedStyle:X8} expectedVisible={snapshot.WasVisible} savedShowCmd={snapshot.Placement.ShowCommand} expectedParent=0x{snapshot.Parent:X} expectedOwner=0x{snapshot.Owner:X} host=0x{snapshot.HostHandle:X} " +
                    WindowDiagnostics.Capture(snapshot.Identity, children));
            }
            catch (Exception error) { Trace(stage + " diagnostic-query-failed: " + error.Message); }
        }
        private void Trace(string message) { try { trace?.Invoke(message); } catch { /* Diagnostics must not affect ownership. */ } }
        private void Warn(string message) { try { warning?.Invoke(message); } catch { /* Presentation warnings are nonfatal. */ } }

        private void UpdateTaskbar(WindowSnapshot snapshot, bool add)
        {
            // Shell UI failure must not leave an otherwise restored window marked as embedded.
            try
            {
                RequireMatch(snapshot.Identity);
                if (add) taskbar.Add(snapshot.Identity);
                else taskbar.Remove(snapshot.Identity);
                Trace($"taskbar.{(add ? "add" : "remove")} HWND=0x{snapshot.Identity.Handle:X} call-returned=true membership=not-verified");
            }
            catch (Exception error)
            {
                Trace($"taskbar.{(add ? "add" : "remove")} HWND=0x{snapshot.Identity.Handle:X} call-returned=false HRESULT=0x{error.HResult:X8} error={error.Message}");
                Warn("窗口结构已更新，但任务栏" + (add ? "恢复" : "移除") + "失败：" +
                    snapshot.Identity.Title + "；" + error.Message);
            }
        }

        public static void FocusAfterShortcut(WindowSnapshot snapshot)
        {
            if (!new NativeWindowApi().Matches(snapshot.Identity)) return;
            IntPtr root = NativeMethods.GetAncestor(NativeMethods.GetForegroundWindow(), 2);
            IntPtr hostRoot = NativeMethods.GetAncestor(new IntPtr(snapshot.HostHandle), 2);
            if (root == IntPtr.Zero || (root != hostRoot && root != snapshot.Identity.Hwnd)) return;
            if (snapshot.Mode == HostingMode.Embedded)
                NativeMethods.SetFocus(snapshot.Identity.Hwnd); // Best effort; do not force input-queue association.
            else NativeMethods.SetForegroundWindow(snapshot.Identity.Hwnd); // User-initiated only; may be denied.
        }

        public static string ReadTitle(IntPtr hwnd)
        {
            var text = new StringBuilder(1024);
            NativeMethods.GetWindowText(hwnd, text, text.Capacity);
            return text.ToString();
        }

        public static string DescribeDpi(IntPtr hwnd)
        {
            var context = NativeMethods.GetWindowDpiAwarenessContext(hwnd);
            foreach (var entry in new (int Value, string Name)[] { (-4, "PerMonitorV2"), (-3, "PerMonitor"), (-2, "System"), (-1, "Unaware"), (-5, "UnawareGdiScaled") })
                if (NativeMethods.AreDpiAwarenessContextsEqual(context, new IntPtr(entry.Value))) return entry.Name;
            return "Unknown";
        }

        public static void MatchThreadDpiToExplorer()
        {
            // Called on the STA thread BEFORE creating any WinForms HWNDs.
            var candidates = DiscoverExplorers();
            if (candidates.Count == 0) return;
            var context = NativeMethods.GetWindowDpiAwarenessContext(candidates[0].Hwnd);
            if (context == IntPtr.Zero || NativeMethods.SetThreadDpiAwarenessContext(context) == IntPtr.Zero)
                throw new Win32Exception(Marshal.GetLastWin32Error(), "无法匹配 Explorer 的 DPI awareness。");
        }

        public static NativeRect ReadRect(IntPtr hwnd)
        {
            Check(NativeMethods.GetWindowRect(hwnd, out var rect), "GetWindowRect");
            return rect;
        }

        private void RequireMatch(WindowIdentity identity)
        {
            if (!Matches(identity)) throw new InvalidOperationException("窗口身份已变化或窗口已消失，停止操作此 HWND。");
        }
        private static long ReadLong(IntPtr hwnd, int index)
        {
            NativeMethods.SetLastError(0);
            long value = NativeMethods.GetWindowLongPtr(hwnd, index).ToInt64();
            int error = Marshal.GetLastWin32Error();
            if (value == 0 && error != 0) throw new Win32Exception(error, "GetWindowLongPtr");
            return value & 0xFFFFFFFFL;
        }
        private static void WriteLong(IntPtr hwnd, int index, long value)
        {
            NativeMethods.SetLastError(0);
            var previous = NativeMethods.SetWindowLongPtr(hwnd, index, new IntPtr(value));
            int error = Marshal.GetLastWin32Error();
            if (previous == IntPtr.Zero && error != 0) throw new Win32Exception(error, "SetWindowLongPtr");
        }
        private static void ChangeParent(IntPtr hwnd, IntPtr parent)
        {
            NativeMethods.SetLastError(0);
            var previous = NativeMethods.SetParent(hwnd, parent);
            int error = Marshal.GetLastWin32Error();
            // A formerly unowned top-level window legitimately has a NULL previous parent.
            if (previous == IntPtr.Zero && error != 0) throw new Win32Exception(error, "SetParent");
        }
        private static void FrameChanged(IntPtr hwnd) => Check(NativeMethods.SetWindowPos(hwnd, IntPtr.Zero, 0, 0, 0, 0,
            NativeMethods.SwpNoMove | NativeMethods.SwpNoSize | NativeMethods.SwpNoZOrder |
            NativeMethods.SwpNoActivate | NativeMethods.SwpFrameChanged), "SetWindowPos(frame)");
        private static void Check(bool success, string operation)
        {
            if (!success) throw new Win32Exception(Marshal.GetLastWin32Error(), operation);
        }
    }
}
