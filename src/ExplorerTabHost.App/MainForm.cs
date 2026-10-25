using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.Linq;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using Timer = System.Windows.Forms.Timer;
using System.Windows.Forms;
using ExplorerTabHost.Core;

namespace ExplorerTabHost.App
{
    internal sealed class MainForm : Form
    {
        private readonly HostController controller;
        private readonly AppLog log;
        private readonly PresentationMonitor presentation;
        private readonly Panel content = new Panel();
        private readonly TabStrip tabs = new TabStrip();
        private readonly ComboBox mode = new ComboBox();
        private readonly Label status = new Label();
        private readonly Timer monitor = new Timer();
        private readonly CheckBox shortcuts = new CheckBox { Text = "Ctrl+Tab", AutoSize = true, Checked = true };
        private TabKeyboardHook? keyboard;
        private int queuedShortcut;
        private bool refreshing;
        private bool closing;
        private int modalDepth;
        private AppSettings settings;
        private readonly IAppSettingsStore? settingsStore;
        private readonly IExplorerTabService explorerTabs;
        private CancellationTokenSource? creation;
        private readonly Button addWindows = new Button { Text = "加入窗口…", AutoSize = true };
        private readonly Button preferences = new Button { Text = "设置…", AutoSize = true };
        private readonly Button newTab = new Button { Text = "＋ 新建标签", AutoSize = true };
        private readonly Button cancelCreation = new Button { Text = "取消新建", AutoSize = true, Visible = false };

        public MainForm(HostController controller, AppLog log)
            : this(controller, log, new AppSettings(), null, new StaExplorerTabService(() => new ShellExplorerBackend())) { }

        public MainForm(HostController controller, AppLog log, AppSettings settings, IAppSettingsStore? settingsStore, IExplorerTabService explorerTabs)
        {
            this.settings = settings.Copy(); this.settingsStore = settingsStore; this.explorerTabs = explorerTabs;
            this.controller = controller;
            this.log = log;
            presentation = new PresentationMonitor(log);
            Text = "ExplorerTabHost 0.4.1 · Windows 10";
            Font = new Font("Microsoft YaHei UI", 9F);
            StartPosition = FormStartPosition.CenterScreen;
            Size = new Size(1100, 800);
            MinimumSize = new Size(720, 500);
            var toolbar = new FlowLayoutPanel { Dock = DockStyle.Top, AutoSize = true, AutoSizeMode = AutoSizeMode.GrowAndShrink, Padding = new Padding(8), WrapContents = true };
            var add = addWindows;
            var detach = new Button { Text = "分离当前", AutoSize = true };
            var detachAll = new Button { Text = "分离全部", AutoSize = true };
            var exit = new Button { Text = "恢复并退出", AutoSize = true };
            mode.DropDownStyle = ComboBoxStyle.DropDownList;
            mode.Width = 200;
            mode.Items.AddRange(new object[] { "嵌入窗口（实验性）", "顶层窗口分组（兼容）" });
            mode.SelectedIndex = settings.Mode == HostingMode.Embedded ? 0 : 1;
            shortcuts.Text = "快捷键"; shortcuts.Checked = settings.ShortcutsEnabled;
            add.Click += (_, __) => AddWindows();
            detach.Click += (_, __) => { if (controller.Active != null) Detach(controller.Active); };
            detachAll.Click += (_, __) => DetachAll(false);
            exit.Click += (_, __) => Close();
            newTab.Click += async (_, __) => await CreateTabAsync(null);
            cancelCreation.Click += (_, __) => creation?.Cancel();

            preferences.Click += (_, __) => ShowSettings();
            toolbar.Controls.AddRange(new Control[] { newTab, add, detach, detachAll, mode, shortcuts, preferences, exit, cancelCreation });
            // Reserve a full horizontal scrollbar row so overflow never clips buttons or triggers vertical scrolling.
            tabs.Dock = DockStyle.Top; tabs.Height = 47 + SystemInformation.HorizontalScrollBarHeight; tabs.Padding = new Padding(8, 4, 8, 4);
            tabs.WrapContents = false; tabs.AutoScroll = true; tabs.BackColor = Color.FromArgb(235, 238, 243);
            tabs.Selected += SelectTab;
            tabs.MiddleDetach += Detach;
            tabs.MoveRequested += MoveTab;
            tabs.InteractionChanged += _ => SyncShortcutScope();
            content.Dock = DockStyle.Fill;
            content.BackColor = Color.FromArgb(249, 250, 252);
            content.Controls.Add(new Label
            {
                Dock = DockStyle.Fill, TextAlign = ContentAlignment.MiddleCenter,
                Text = "点“＋ 新建标签”创建窗口，或用“加入窗口”管理已有窗口\n\n" +
                    "每个标签保留真实窗口，不重新导航目录。\n" +
                    "拖动标签排序；右键可重命名、分离其他或右侧标签。\n" +
                    "Ctrl+T 新建标签；复制当前目录可使用标签右键菜单。\n\n" +
                    "分离不关闭 Explorer。退出前会恢复所有窗口。\n" +
                    "实验性嵌入尚不保证强杀 / 崩溃安全，请先用无重要操作的窗口测试。"
            });
            var footer = new Panel { Dock = DockStyle.Bottom, Height = 30 };
            var diagnostic = new Button { Text = "保存诊断…", Dock = DockStyle.Right, Width = 110 };
            diagnostic.Click += (_, __) => ExportDiagnostics();
            status.Dock = DockStyle.Fill; status.AutoEllipsis = true; status.Padding = new Padding(8, 4, 0, 0);
            footer.Controls.Add(status); footer.Controls.Add(diagnostic);
            Controls.Add(content); Controls.Add(tabs); Controls.Add(toolbar); Controls.Add(footer);
            content.Resize += (_, __) => LayoutCurrent();
            LocationChanged += (_, __) => LayoutCurrent();
            Resize += (_, __) =>
            {
                SyncShortcutScope(); // Disable immediately when minimised, not at the next monitor tick.
                if (closing || modalDepth > 0) return;
                RunSafely("同步窗口大小失败", () =>
                {
                    controller.SetActiveVisible(WindowState != FormWindowState.Minimized);
                    if (WindowState != FormWindowState.Minimized) controller.LayoutActive(CurrentBounds());
                });
            };
            Activated += (_, __) =>
            {
                if (closing || modalDepth > 0 || OwnedForms.Any(f => f.Visible)) return;
                RunSafely("激活窗口失败", () => controller.LayoutActive(CurrentBounds(), true));
            };
            FormClosing += OnClosing;
            Shown += (_, __) =>
            {
                using (var process = Process.GetCurrentProcess())
                    log.Write($"Host started; version={typeof(MainForm).Assembly.GetName().Version}; PID={process.Id}; OS={Environment.OSVersion}; x64={Environment.Is64BitProcess}; executable={process.MainModule?.FileName}; DPI={NativeWindowApi.DescribeDpi(content.Handle)}");
                try
                {
                    keyboard = new TabKeyboardHook(QueueShortcut, QueueNewTab);

                    SyncShortcutScope();
                }
                catch (Exception error)
                {
                    shortcuts.Checked = false; shortcuts.Enabled = false;
                    log.Write("Keyboard hook unavailable", error);
                    ReportError("快捷键未注册，仍可使用鼠标切换标签。", error);
                }
                monitor.Start();
                presentation.Start();
            };
            monitor.Interval = 1500;
            monitor.Tick += (_, __) => PollWindows();
            mode.SelectedIndexChanged += (_, __) => UpdateStatus();
            shortcuts.CheckedChanged += (_, __) => SyncShortcutScope();
            if (settings.Bounds.Right != settings.Bounds.Left)
            {
                StartPosition = FormStartPosition.Manual;
                Bounds = SettingsLayoutPolicy.Restore(settings.Bounds, Screen.AllScreens.Select(s => s.WorkingArea).ToArray(), Bounds);
            }
            if (settings.Maximized) WindowState = FormWindowState.Maximized;
            UpdateStatus();
        }

        private HostingMode SelectedMode => mode.SelectedIndex == 0 ? HostingMode.Embedded : HostingMode.Grouped;
        private Rectangle CurrentBounds()
        {
            var bounds = new Rectangle(Point.Empty, content.ClientSize);
            return SelectedMode == HostingMode.Embedded ? bounds : content.RectangleToScreen(bounds);
        }

        private void QueueCreateFromMenu(WindowSession session)
        {
            if (!IsDisposed && IsHandleCreated)
                BeginInvoke(new Action(async () => { if (!IsDisposed && !closing) await CreateTabAsync(session); }));
        }

        private async Task CreateTabAsync(WindowSession? source)
        {
            if (closing || IsDisposed || creation != null || explorerTabs.IsBusy || modalDepth > 0 || tabs.IsInteracting) return;
            if (source != null && (!controller.Sessions.Contains(source) || source.State != SessionState.Attached)) return;
            var request = new CancellationTokenSource(); creation = request;
            request.CancelAfter(15000);
            HostingMode requestedMode = SelectedMode;
            SetCreationControls(true);
            status.Text = source == null ? "正在新建 Explorer；可取消，失败保留独立窗口…" : "正在读取并复制此标签的目录…";
            log.Write($"Create requested; duplicate={source != null}; mode={requestedMode}; source=0x{source?.Identity.Handle ?? 0:X}");
            try
            {
                var result = await explorerTabs.CreateAsync(settings.NewTabDirectory, source?.Identity,
                    controller.Sessions.Select(s => s.Identity.Handle).ToArray(), request.Token);
                // Do not rebuild a popup/drag or race a modal dialog when the asynchronous result arrives.
                while (modalDepth > 0 || tabs.IsInteracting) await Task.Delay(50, request.Token);
                request.Token.ThrowIfCancellationRequested();
                if (closing || IsDisposed) return;
                if (SelectedMode != requestedMode) throw new InvalidOperationException("挂载模式已改变，新建窗口保持独立。");
                AttachCreated(result, requestedMode);
                log.Write($"Create attached HWND=0x{result.Identity.Handle:X}; directory={result.Directory}");
            }
            catch (OperationCanceledException)
            {
                log.Write("Create cancelled/timeout; no late attachment or fallback; any created window stays independent.");
                if (!closing && !IsDisposed) status.Text = "新建已取消或超时；若有新窗口，保持独立。Shell 尚未返回时请稍后重试。";
            }
            catch (Exception error)
            {
                if (!closing && !IsDisposed) ReportError("新建标签未完成；未关闭窗口，现有标签保留。", error);
                else log.Write("Create failed during exit", error);
            }
            finally
            {
                if (creation == request) creation = null;
                request.Dispose();
                if (!closing && !IsDisposed) { SetCreationControls(false); SyncShortcutScope(); }
            }
        }

        private void AttachCreated(CreatedExplorer result, HostingMode requestedMode)
        {
            WindowSession session;
            try { session = controller.Attach(result.Identity, content.Handle, requestedMode, CurrentBounds()); }
            catch { RefreshTabs(); throw; } // Partial rollback may leave a recovery entry that must stay visible.
            try { controller.Activate(session, CurrentBounds()); }
            catch
            {
                try { controller.Detach(session); }
                catch (Exception rollback) { log.Write("New tab activation rollback failed; recovery retained", rollback); }
                RefreshTabs(); throw;
            }
            presentation.Track(session.Identity, "explicit-create");
            RefreshTabs(); tabs.Reveal(session);
        }

        private void SetCreationControls(bool pending)
        {
            newTab.Enabled = !pending && !explorerTabs.IsBusy;
            addWindows.Enabled = !pending; preferences.Enabled = !pending;
            cancelCreation.Visible = pending;
            mode.Enabled = !pending && controller.Sessions.Count == 0;
            SyncShortcutScope();
        }

        private void QueueNewTab()
        {
            if (Interlocked.CompareExchange(ref queuedShortcut, 1, 0) != 0) return;
            try
            {
                BeginInvoke(new Action(async () =>
                {
                    Interlocked.Exchange(ref queuedShortcut, 0);
                    if (closing || IsDisposed || modalDepth > 0 || tabs.IsInteracting || WindowState == FormWindowState.Minimized ||
                        !shortcuts.Checked || !TabKeyboardHook.IsTargetForeground(Handle, controller.Active)) return;
                    await CreateTabAsync(null);
                }));
            }
            catch (InvalidOperationException) { Interlocked.Exchange(ref queuedShortcut, 0); }
        }

        private AppSettings CurrentSettings()
        {
            var candidate = settings.Copy(); candidate.Mode = SelectedMode; candidate.ShortcutsEnabled = shortcuts.Checked;
            candidate.Bounds = SettingsLayoutPolicy.Save(WindowState == FormWindowState.Normal ? Bounds : RestoreBounds);
            candidate.Maximized = WindowState == FormWindowState.Maximized;
            return candidate;
        }
        private void ShowSettings()
        {
            using (EnterModal())
            using (var dialog = new SettingsDialog(CurrentSettings(), controller.Sessions.Count > 0, settingsStore != null))
            {
                if (dialog.ShowDialog(this) != DialogResult.OK) return;
                RunSafely("保存设置失败，旧设置保持不变", () => ApplySettings(dialog.Result));
            }
        }
        private void ApplySettings(AppSettings candidate)
        {
            if (controller.Sessions.Count > 0 && candidate.Mode != SelectedMode)
                throw new InvalidOperationException("先分离全部窗口再修改挂载模式。");
            settingsStore?.Save(candidate); // Commit storage first; failed saves never partially update live UI.
            settings = candidate.Copy(); mode.SelectedIndex = settings.Mode == HostingMode.Embedded ? 0 : 1;
            shortcuts.Checked = settings.ShortcutsEnabled && shortcuts.Enabled; SyncShortcutScope(); UpdateStatus();
            log.Write("Settings applied; persistent=" + (settingsStore != null));
        }
        private void SaveSettingsOnExit()
        {
            if (settingsStore == null) return;
            try { settingsStore.Save(CurrentSettings()); log.Write("Settings saved after normal detach."); }
            catch (Exception error)
            {
                log.Write("Settings save on exit failed; windows already restored", error);
                MessageBox.Show(this, "窗口已恢复，但本次设置保存失败。旧设置未主动清除。\n\n" + error.Message,
                    "ExplorerTabHost", MessageBoxButtons.OK, MessageBoxIcon.Warning);
            }
        }

        private void AddWindows()
        {
            using (EnterModal())
            using (var picker = new WindowPicker(controller.Sessions.Select(s => s.Identity.Handle)))
            {
                if (picker.ShowDialog(this) != DialogResult.OK) return;
                AttachSelectedWindows(picker.SelectedWindows);
            }
        }

        private void AttachSelectedWindows(IEnumerable<WindowIdentity> selectedWindows)
        {
            foreach (var identity in selectedWindows)
            {
                if (!NativeWindowApi.IsExplorer(identity)) continue;
                try
                {
                    var session = controller.Attach(identity, content.Handle, SelectedMode, CurrentBounds());
                    controller.Activate(session, CurrentBounds());
                    log.Write("Attached " + identity + "; mode=" + SelectedMode);
                    presentation.Track(identity, "attach");
                }
                catch (Exception error)
                {
                    log.Write("Attach failed " + identity, error);
                    MessageBox.Show(this, error.Message, "加入窗口失败", MessageBoxButtons.OK, MessageBoxIcon.Error);
                }
                RefreshTabs();
            }
        }

        private void SelectTab(WindowSession session)
        {
            RunSafely("切换标签失败", () =>
            {
                var watch = Stopwatch.StartNew();
                controller.Activate(session, CurrentBounds());
                watch.Stop();
                // This measures host Win32 calls, not Explorer paint completion or user-visible latency.
                log.Write($"Switch HWND=0x{session.Identity.Handle:X}; host-calls-ms={watch.Elapsed.TotalMilliseconds:F2}");
                RefreshTabs();
                tabs.Reveal(session);
                status.Text += $" · 宿主调用 {watch.Elapsed.TotalMilliseconds:F1} ms（非重绘完成时间）";
            });
            SyncShortcutScope();
        }

        private void Detach(WindowSession session)
        {
            RunSafely("分离失败，窗口恢复记录仍保留", () =>
            {
                var preferred = TabManagementPolicy.Successor(controller.Sessions, controller.Active, new[] { session });
                controller.Detach(session);
                log.Write("Detached " + session.Identity);
                presentation.Track(session.Identity, "detach");
                EnsureActive(true, preferred);
            });
            RefreshTabs();
        }

        private void DetachAll(bool forExit)
        {
            var identities = controller.Sessions.Select(s => s.Identity).ToArray();
            var errors = controller.DetachAll();
            foreach (var identity in identities) presentation.Track(identity, "detach-all");
            RefreshTabs();
            if (errors.Count > 0 && !forExit)
                ReportError("部分窗口尚未恢复。请重试分离，不要强杀程序。", new InvalidOperationException(string.Join("\n", errors)));
        }

        private void EnsureActive(bool raise = false, WindowSession? preferred = null)
        {
            if (controller.Active != null) return;
            var next = preferred != null && controller.Sessions.Contains(preferred) && preferred.State == SessionState.Attached
                ? preferred : controller.Sessions.FirstOrDefault(s => s.State == SessionState.Attached);
            if (next != null) controller.Activate(next, CurrentBounds(), raise && modalDepth == 0);
        }

        private void LayoutCurrent()
        {
            if (closing || modalDepth > 0 || WindowState == FormWindowState.Minimized || !content.IsHandleCreated) return;
            RunSafely("同步位置失败", () => controller.LayoutActive(CurrentBounds()));
        }

        private void PollWindows()
        {
            if (closing || modalDepth > 0 || refreshing || tabs.IsInteracting) return;
            SetCreationControls(creation != null);
            try
            {
                int previousCount = controller.Sessions.Count;
                var errors = controller.PruneLost();
                foreach (string error in errors) log.Write("Prune failed: " + error);
                bool changed = previousCount != controller.Sessions.Count;
                foreach (var session in controller.Sessions)
                {
                    string title = NativeWindowApi.ReadTitle(session.Identity.Hwnd);
                    if (!string.IsNullOrEmpty(title) && title != session.Title)
                    { session.Title = title; changed = true; }
                }
                EnsureActive();
                SyncShortcutScope();
                if (changed) RefreshTabs();
            }
            catch (Exception error) { log.Write("Monitor failed", error); }
        }

        private void RefreshTabs()
        {
            if (refreshing) return;
            refreshing = true;
            try
            {
                tabs.Render(controller.Sessions, controller.Active, CreateTabMenu);
                mode.Enabled = controller.Sessions.Count == 0 && creation == null;
                UpdateStatus();
                SyncShortcutScope();
            }
            finally { refreshing = false; }
        }

        private void MoveTab(WindowSession session, int index)
        {
            RunSafely("标签排序失败", () =>
            {
                controller.MoveTab(session, index);
                log.Write($"Tab reordered HWND=0x{session.Identity.Handle:X}; index={index}; native-mutations=none");
                RefreshTabs();
            });
        }

        private ContextMenuStrip CreateTabMenu(WindowSession session)
        {
            var menu = new ContextMenuStrip();
            int index = controller.Sessions.ToList().IndexOf(session);
            var duplicate = menu.Items.Add("复制此标签的目录…", null, (_, __) => QueueCreateFromMenu(session));
            duplicate.Enabled = creation == null && !explorerTabs.IsBusy && session.State == SessionState.Attached;
            menu.Items.Add(new ToolStripSeparator());
            menu.Items.Add("重命名标签…", null, (_, __) => RenameTab(session));
            var reset = menu.Items.Add("恢复目录名称", null, (_, __) =>
                RunSafely("恢复标签名称失败", () => { controller.ResetTabTitle(session); RefreshTabs(); }));
            reset.Enabled = session.CustomTitle != null;
            menu.Items.Add(new ToolStripSeparator());
            var left = menu.Items.Add("向左移动", null, (_, __) => MoveTab(session, controller.Sessions.ToList().IndexOf(session) - 1));
            var right = menu.Items.Add("向右移动", null, (_, __) => MoveTab(session, controller.Sessions.ToList().IndexOf(session) + 1));
            left.Enabled = index > 0; right.Enabled = index >= 0 && index < controller.Sessions.Count - 1;
            menu.Items.Add(new ToolStripSeparator());
            menu.Items.Add("分离为独立 Explorer（不关闭）", null, (_, __) => Detach(session));
            var others = menu.Items.Add("分离其他标签（不关闭）", null, (_, __) => DetachTabs(session, TabDetachScope.Others));
            var toRight = menu.Items.Add("分离右侧标签（不关闭）", null, (_, __) => DetachTabs(session, TabDetachScope.ToRight));
            others.Enabled = controller.Sessions.Count > 1;
            toRight.Enabled = index >= 0 && index < controller.Sessions.Count - 1;
            menu.Items.Add("分离全部标签（不关闭）", null, (_, __) => DetachAll(false));
            IDisposable? interaction = null;
            menu.Opening += (_, e) =>
            {
                if (closing || !controller.Sessions.Contains(session)) { e.Cancel = true; return; }
                interaction = EnterModal(); // Disable shortcuts; grouped windows must not cover the popup.
            };
            menu.Closed += (_, __) => { interaction?.Dispose(); interaction = null; SyncShortcutScope(); };
            menu.Disposed += (_, __) => { interaction?.Dispose(); interaction = null; };
            return menu;
        }

        private void RenameTab(WindowSession session)
        {
            using (EnterModal())
            using (var dialog = new RenameTabDialog(session.DisplayTitle))
            {
                if (dialog.ShowDialog(this) != DialogResult.OK) return;
                RunSafely("标签重命名失败", () =>
                {
                    controller.RenameTab(session, dialog.TabTitle);
                    log.Write($"Tab renamed HWND=0x{session.Identity.Handle:X}; native-mutations=none");
                    RefreshTabs();
                });
            }
        }

        private void DetachTabs(WindowSession anchor, TabDetachScope scope)
        {
            RunSafely("批量分离失败，窗口恢复记录仍保留", () =>
            {
                var targets = TabManagementPolicy.Select(controller.Sessions, anchor, scope);
                var preferred = TabManagementPolicy.Successor(controller.Sessions, controller.Active, targets);
                var errors = controller.DetachTabs(anchor, scope);
                foreach (var target in targets) presentation.Track(target.Identity, "detach-" + scope);
                log.Write($"Tab batch detach scope={scope}; requested={targets.Count}; errors={errors.Count}");
                EnsureActive(true, preferred);
                RefreshTabs();
                if (errors.Count > 0)
                    throw new InvalidOperationException("部分窗口尚未恢复，请重试分离，不要强杀程序。\n" + string.Join("\n", errors));
            });
            RefreshTabs();
        }

        private void SyncShortcutScope() => keyboard?.UpdateScope(IsHandleCreated ? Handle : IntPtr.Zero,
            controller.Active, shortcuts.Checked && !closing && modalDepth == 0 && !tabs.IsInteracting && WindowState != FormWindowState.Minimized,
            controller.Sessions.Count(s => s.State == SessionState.Attached) >= 2, creation == null && !explorerTabs.IsBusy);

        private void QueueShortcut(int direction)
        {
            // Dedicated hook thread. Bound queued work while the UI is busy in a native call.
            if (Interlocked.CompareExchange(ref queuedShortcut, 1, 0) != 0) return;
            try
            {
                BeginInvoke(new Action(() =>
                {
                    Interlocked.Exchange(ref queuedShortcut, 0);
                    if (closing || IsDisposed || modalDepth > 0 || tabs.IsInteracting || WindowState == FormWindowState.Minimized ||
                        !shortcuts.Checked || !TabKeyboardHook.IsTargetForeground(Handle, controller.Active)) return;
                    var next = controller.Adjacent(direction);
                    if (next == null) return;
                    SelectTab(next);
                    // Embedded focus transfer is best-effort via already-associated queues; no AttachThreadInput.
                    if (controller.Active == next && next.State == SessionState.Attached)
                        RunSafely("快捷键焦点切换失败", () => NativeWindowApi.FocusAfterShortcut(next.Snapshot));
                }));
            }
            catch (InvalidOperationException) { Interlocked.Exchange(ref queuedShortcut, 0); }
        }

        private void UpdateStatus()
        {
            if (creation != null) return;
            status.Text =
            $"{controller.Sessions.Count} 个标签 · {(SelectedMode == HostingMode.Embedded ? "实验性嵌入" : "顶层分组：保留 Explorer 任务栏项")}" +
            " · Ctrl+T 新建 / Ctrl+Tab 切换 · 右键复制目录 / 管理 · 分离不关闭";
        }

        private void OnClosing(object? sender, FormClosingEventArgs e)
        {
            creation?.Cancel();
            closing = true; modalDepth++; SyncShortcutScope(); monitor.Stop();
            var identities = controller.Sessions.Select(s => s.Identity).ToArray();
            var errors = controller.DetachAll();
            foreach (var identity in identities) presentation.Track(identity, "closing-detach");
            // Do not keep the app alive or pump foreign messages for deferred samples during exit.
            presentation.Snapshot("closing-after-detach");
            if (controller.Sessions.Count > 0)
            {
                e.Cancel = true;
                closing = false; modalDepth--; monitor.Start();
                RefreshTabs();
                ReportError("还有窗口没有完成恢复，已阻止退出。请重试“分离全部”。", new InvalidOperationException(string.Join("\n", errors)));
            }
            else
            {
                SaveSettingsOnExit();
                log.Write("All windows detached; host closing.");
            }
        }

        public void EmergencyDetach()
        {
            foreach (string error in controller.DetachAll()) log.Write("Emergency detach failed: " + error);
        }

        public void ReportError(string message, Exception error)
        {
            log.Write(message, error);
            using (EnterModal())
                MessageBox.Show(this, message + "\n\n" + error.Message + "\n\n日志：" + log.FilePath,
                    "ExplorerTabHost", MessageBoxButtons.OK, MessageBoxIcon.Error);
        }

        private void ExportDiagnostics()
        {
            // Capture before a modal dialog changes visibility in grouped mode.
            string windowState = presentation.Snapshot("user-export-before-dialog", discoverStandalone: true);
            using (EnterModal())
            using (var dialog = new SaveFileDialog
            {
                Title = "保存诊断包（可能含目录标题与路径，不会自动上传）",
                Filter = "诊断包 (*.zip)|*.zip", DefaultExt = "zip", AddExtension = true,
                FileName = "ExplorerTabHost-" + DateTime.Now.ToString("yyyyMMdd-HHmmss-fff") + ".zip",
                InitialDirectory = Environment.GetFolderPath(Environment.SpecialFolder.DesktopDirectory)
            })
            {
                if (dialog.ShowDialog(this) != DialogResult.OK) return;
                RunSafely("诊断包保存失败，请使用新的文件名", () =>
                {
                    DiagnosticArchive.Create(dialog.FileName, Path.GetDirectoryName(log.FilePath)!, windowState);
                    log.Write("Diagnostic archive saved: " + dialog.FileName);
                    status.Text = "诊断包已保存：" + dialog.FileName;
                });
            }
        }

        private void RunSafely(string message, Action action)
        {
            try { action(); }
            catch (Exception error) { ReportError(message, error); }
        }

        private IDisposable EnterModal()
        {
            modalDepth++;
            SyncShortcutScope();
            // Non-cooperating top-level Explorer windows must not cover the host's dialogs.
            if (modalDepth == 1 && SelectedMode == HostingMode.Grouped)
            {
                try { controller.SetActiveVisible(false); }
                catch (Exception error) { log.Write("Hide for dialog failed", error); }
            }
            return new ModalScope(() =>
            {
                modalDepth--;
                SyncShortcutScope();
                if (modalDepth != 0 || closing) return;
                try
                {
                    EnsureActive(true);
                    if (WindowState != FormWindowState.Minimized)
                    { controller.SetActiveVisible(true); controller.LayoutActive(CurrentBounds(), true); }
                }
                catch (Exception error) { log.Write("Restore after dialog failed", error); }
            });
        }

        protected override void Dispose(bool disposing)
        {
            if (disposing) { creation?.Cancel(); keyboard?.Dispose(); monitor.Dispose(); presentation.Dispose(); }
            base.Dispose(disposing);
        }
        private sealed class ModalScope : IDisposable
        {
            private Action? cleanup;
            public ModalScope(Action cleanup) { this.cleanup = cleanup; }
            public void Dispose() { var action = cleanup; cleanup = null; action?.Invoke(); }
        }
    }
}
