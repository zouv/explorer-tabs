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
using ExplorerTabs.Core;

namespace ExplorerTabs.App
{
    internal sealed partial class MainForm : Form
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
        private DateTime closeAllStarted;
        private bool restoringOnExit;
        private bool exitByClosingSessions;
        private int modalDepth;
        private AppSettings settings;
        private readonly IAppSettingsStore? settingsStore;
        private readonly IExplorerTabService explorerTabs;
        private CancellationTokenSource? creation;
        private Guid? creationRequestId;
        private readonly GlobalHotkeyRegistration globalHotkeys = new GlobalHotkeyRegistration();
        private NotifyIcon? tray;
        private readonly IconButton addWindows = new IconButton(UiGlyph.AddWindow, "加入窗口…");
        private readonly IconButton newTab = new IconButton(UiGlyph.NewTab, "新建标签") { Primary = true };
        private readonly IconButton cancelCreation = new IconButton(UiGlyph.Cancel, "取消操作") { Visible = false };
        private readonly System.Windows.Forms.Timer cancelRevealTimer = new System.Windows.Forms.Timer { Interval = 450 };
        private bool cancelWanted;

        public MainForm(HostController controller, AppLog log)
            : this(controller, log, new AppSettings(), null, new StaExplorerTabService(() => new ShellExplorerBackend())) { }

        public MainForm(HostController controller, AppLog log, AppSettings settings, IAppSettingsStore? settingsStore, IExplorerTabService explorerTabs)
        {
            this.settings = settings.Copy(); this.settingsStore = settingsStore; this.explorerTabs = explorerTabs;
            this.controller = controller;
            this.log = log;
            presentation = new PresentationMonitor(log);
            Text = ProductIdentity.Name + " " + typeof(MainForm).Assembly.GetName().Version!.ToString(3);
            Font = new Font("Microsoft YaHei UI", 9F);
            StartPosition = FormStartPosition.CenterScreen;
            Size = new Size(1100, 800);
            // The native frame is now part of the window rect; keep the old usable content width
            // at the smallest window (720 x 500 client plus the frame).
            MinimumSize = new Size(736, 516);
            // Left group: actions that open menus. Right group: commands that open dialogs.
            var toolbarHost = new Panel { Dock = DockStyle.Top, Height = 36, BackColor = HostTheme.Canvas };
            var toolbar = new FlowLayoutPanel { Dock = DockStyle.Fill, Height = 36, AutoSize = false, Padding = new Padding(8, 2, 8, 2), WrapContents = false };
            var toolbarRight = new FlowLayoutPanel { Dock = DockStyle.Right, Width = 48, Height = 36, AutoSize = false, Padding = new Padding(0, 2, 8, 2), WrapContents = false, FlowDirection = FlowDirection.LeftToRight };
            toolbarHost.Controls.Add(toolbar);
            toolbarHost.Controls.Add(toolbarRight);
            var add = addWindows;
            mode.DropDownStyle = ComboBoxStyle.DropDownList;
            mode.Width = 200;
            mode.Items.AddRange(new object[] { "嵌入窗口（实验性）", "顶层窗口分组（兼容）" });
            mode.SelectedIndex = settings.Mode == HostingMode.Embedded ? 0 : 1;
            mode.TabStop = false; shortcuts.TabStop = false;
            shortcuts.Text = "快捷键"; shortcuts.Checked = settings.ShortcutsEnabled;
            add.Click += (_, __) => ShowAddWindowsMenu();
            newTab.Click += async (_, __) => await CreateTabAsync(null);
            cancelCreation.Click += (_, __) => { creation?.Cancel(); workspaceOperation?.Cancel(); foreach (var close in closeReading.Values.ToArray()) close.Cancel(); };
            cancelRevealTimer.Tick += (_, __) => CancelRevealTick();

            tabs.Padding = new Padding(2, 0, 2, 0);
            tabs.Selected += SelectTab;
            tabs.MiddleClose += session => QueueUi(() => CloseTabAsync(session));
            tabs.MoveRequested += MoveTab;
            tabs.InteractionChanged += _ => SyncShortcutScope();
            content.Dock = DockStyle.Fill;
            content.BackColor = Color.FromArgb(249, 250, 252);
            content.Controls.Add(new WelcomePanel(() => newTab.PerformClick(), () => addWindows.PerformClick()));
            var footer = new Panel { Dock = DockStyle.Bottom, Height = 24, BackColor = HostTheme.Canvas };
            var diagnostic = new IconButton(UiGlyph.Diagnostics, "保存诊断…") { Dock = DockStyle.Right, Width = 32 };
            chromeTips.SetToolTip(diagnostic, "保存诊断日志");
            diagnostic.Click += (_, __) => ExportDiagnostics();
            status.Dock = DockStyle.Fill; status.AutoEllipsis = true; status.Padding = new Padding(10, 2, 0, 0); status.ForeColor = Color.FromArgb(100, 113, 133);
            footer.Controls.Add(status); footer.Controls.Add(diagnostic);
            Controls.Add(content); Controls.Add(toolbarHost); Controls.Add(footer);
            InitializeChrome();
            content.Resize += (_, __) => LayoutCurrent();
            LocationChanged += (_, __) => { CaptureWindowPlacement(); LayoutCurrent(); };
            Resize += (_, __) =>
            {
                CaptureWindowPlacement();
                SyncTopResizeBand();
                SyncShortcutScope(); // Disable immediately when minimised, not at the next monitor tick.
                if (closing || modalDepth > 0) return;
                RunSafely("同步窗口大小失败", () =>
                {
                    controller.SetActiveVisible(CanPresentExplorer);
                    if (CanPresentExplorer) controller.LayoutActive(CurrentBounds());
                });
            };
            Activated += (_, __) =>
            {
                if (!CanPresentExplorer || OwnedForms.Any(f => f.Visible)) return;
                RunSafely("激活窗口失败", () => controller.LayoutActive(CurrentBounds(), true));
            };
            FormClosing += OnClosingWithSession;
            Shown += (_, __) =>
            {
                SyncTopResizeBand();
                using (var process = Process.GetCurrentProcess())
                    log.Write($"Host started; version={typeof(MainForm).Assembly.GetName().Version}; PID={process.Id}; OS={Environment.OSVersion}; x64={Environment.Is64BitProcess}; executable={process.MainModule?.FileName}; DPI={NativeWindowApi.DescribeDpi(content.Handle)}");
                try
                {
                    keyboard = new TabKeyboardHook(QueueShortcut, QueueNewTab, QueueCommand);

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
                hotkeyRegistrationActive = true;
                ApplyGlobalHotkey(settings.GlobalHotkey);
                ShowSessionOffer();
            };
            monitor.Interval = 1500;
            monitor.Tick += (_, __) => PollWindows();
            closeMonitor.Tick += (_, __) => PollClosingWindows();
            mode.SelectedIndexChanged += (_, __) => UpdateStatus();
            shortcuts.CheckedChanged += (_, __) => SyncShortcutScope();
            InitializeWindowPlacement();
            InitializeWorkspaceUi(toolbar, toolbarRight);
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
            if (closing || IsDisposed || creation != null || workspaceOperation != null || exitPreparing || explorerTabs.IsBusy || modalDepth > 0 || tabs.IsInteracting) return;
            if (source != null && (!controller.Sessions.Contains(source) || source.State != SessionState.Attached)) return;
            var request = new CancellationTokenSource(); creation = request;
            request.CancelAfter(15000);
            var requestId = Guid.NewGuid(); creationRequestId = requestId;
            var elapsed = Stopwatch.StartNew();
            tabs.ShowPendingCreation(requestId, source);
            HostingMode requestedMode = SelectedMode;
            SetCreationControls(true);
            status.Text = source == null ? "正在后台准备新标签；可取消…" : "正在读取并复制此标签的目录…";
            log.Write($"Create requested; duplicate={source != null}; mode={requestedMode}; source=0x{source?.Identity.Handle ?? 0:X}");
            try
            {
                using (var result = await explorerTabs.CreateAsync(settings.NewTabDirectory, source?.Identity,
                    controller.Sessions.Select(s => s.Identity.Handle).ToArray(), request.Token))
                {
                // Do not rebuild a popup/drag or race a modal dialog when the asynchronous result arrives.
                while (modalDepth > 0 || tabs.IsInteracting) await Task.Delay(50, request.Token);
                request.Token.ThrowIfCancellationRequested();
                if (closing || IsDisposed) return;
                if (SelectedMode != requestedMode) throw new InvalidOperationException("挂载模式已改变，新建窗口保持独立。");
                log.Write($"Create Shell ready; elapsedMs={elapsed.ElapsedMilliseconds}");
                AttachCreatedCore(result, requestedMode, source);
                log.Write($"Create attach/activate/UI ready; elapsedMs={elapsed.ElapsedMilliseconds}");
                log.Write($"Create attached HWND=0x{result.Identity.Handle:X}; directory={result.Directory}");
                }
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
                if (creation == request) { creation = null; creationRequestId = null; }
                if (!tabs.IsDisposed) tabs.RemovePendingCreation(requestId);
                request.Dispose();
                if (!closing && !IsDisposed) { SetCreationControls(false); SyncShortcutScope(); }
            }
        }

        private void AttachCreated(CreatedExplorer result, HostingMode requestedMode) =>
            AttachCreatedCore(result, requestedMode, null);

        private void AttachCreatedCore(CreatedExplorer result, HostingMode requestedMode, WindowSession? source)
        {
            var elapsed = Stopwatch.StartNew();
            WindowSession session;
            try { session = controller.Attach(result.Identity, content.Handle, requestedMode, CurrentBounds(), result.DeferredPresentation); }
            catch { RefreshTabs(); throw; } // Partial rollback may leave a recovery entry that must stay visible.
            log.Write($"Create durable attach finished; elapsedMs={elapsed.ElapsedMilliseconds}");
            try { controller.Activate(session, CurrentBounds(), visible: CanPresentExplorer); }
            catch
            {
                try { controller.Detach(session); }
                catch (Exception rollback) { log.Write("New tab activation rollback failed; recovery retained", rollback); }
                RefreshTabs(); throw;
            }
            log.Write($"Create activation finished; elapsedMs={elapsed.ElapsedMilliseconds}; visible={CanPresentExplorer}");
            result.MarkAttached();
            directoryCache[session] = result.Directory;
            workspaceTouched = true;
            presentation.Track(session.Identity, "explicit-create");
            // Attach appends; persist the duplicate's position in the model, not only in the strip.
            // Resolve the source by reference now so moves during creation follow its latest order.
            int sourceIndex = source == null ? -1 : controller.Sessions.ToList().IndexOf(source);
            if (sourceIndex >= 0) controller.MoveTab(session, sourceIndex + 1);
            if (creationRequestId.HasValue) tabs.CompletePendingCreation(creationRequestId.Value, session);
            RefreshTabs(); tabs.Reveal(session);
        }

        private void SetCreationControls(bool pending)
        {
            newTab.Enabled = !pending && workspaceOperation == null && !explorerTabs.IsBusy;
            addWindows.Enabled = !pending && workspaceOperation == null;
            // Automatic session saving shares workspaceOperation but is invisible background
            // work; only user-facing operations (and new-tab creation) offer cancellation.
            UpdateCancelAffordance(pending || (workspaceOperation != null && !workspaceOperationSilent));
            foreach (WelcomePanel welcome in content.Controls) welcome.SetPending(pending || workspaceOperation != null || explorerTabs.IsBusy);
            mode.Enabled = !pending && workspaceOperation == null && controller.Sessions.Count == 0;
            SyncShortcutScope();
            RefreshSessionOfferControls();
        }

        private void UpdateCancelAffordance(bool wanted)
        {
            if (!wanted)
            {
                cancelWanted = false;
                cancelRevealTimer.Stop();
                cancelCreation.Visible = false;
                return;
            }
            // Keep the glyph hidden for quick operations; PollWindows must not restart this delay.
            if (cancelWanted) return;
            cancelWanted = true;
            cancelRevealTimer.Stop();
            cancelRevealTimer.Start();
        }

        private void CancelRevealTick()
        {
            cancelRevealTimer.Stop();
            if (cancelWanted && !IsDisposed && !closing) cancelCreation.Visible = true;
        }

        private void QueueNewTab()
        {
            if (Interlocked.CompareExchange(ref queuedShortcut, 1, 0) != 0) return;
            try
            {
                BeginInvoke(new Action(async () =>
                {
                    Interlocked.Exchange(ref queuedShortcut, 0);
                    if (closing || IsDisposed || modalDepth > 0 || tabs.IsInteracting || !Visible || WindowState == FormWindowState.Minimized ||
                        !shortcuts.Checked || !TabKeyboardHook.IsTargetForeground(Handle, controller.Active)) return;
                    await CreateTabAsync(null);
                }));
            }
            catch (InvalidOperationException) { Interlocked.Exchange(ref queuedShortcut, 0); }
        }

        private AppSettings CurrentSettings()
        {
            var candidate = settings.Copy(); candidate.Mode = SelectedMode; candidate.ShortcutsEnabled = shortcuts.Checked;
            CaptureWindowPlacement(true);
            candidate.Bounds = SettingsLayoutPolicy.Save(windowPlacement.NormalBounds);
            candidate.Maximized = windowPlacement.Maximized;
            return candidate;
        }
        private void ShowSettings()
        {
            if (workspaceOperation != null || exitPreparing || creation != null || explorerTabs.IsBusy) return;
            using (EnterModal())
            using (var dialog = new SettingsDialog(CurrentSettings(), controller.Sessions.Count > 0, settingsStore != null))
            {
                dialog.GlobalHotkeyCheck = CheckGlobalHotkey;
                if (dialog.ShowDialog(this) != DialogResult.OK) return;
                RunSafely("保存设置失败，旧设置保持不变", () => ApplySettings(dialog.Result));
            }
        }
        private void ShowAbout()
        {
            if (exitPreparing) return;
            using (EnterModal())
            using (var dialog = new AboutDialog())
                dialog.ShowDialog(this);
        }
        private void ApplySettings(AppSettings candidate)
        {
            if (controller.Sessions.Count > 0 && candidate.Mode != SelectedMode)
                throw new InvalidOperationException("先分离全部窗口再修改挂载模式。");
            settingsStore?.Save(candidate); // Commit storage first; failed saves never partially update live UI.
            settings = candidate.Copy(); mode.SelectedIndex = settings.Mode == HostingMode.Embedded ? 0 : 1;
            shortcuts.Checked = settings.ShortcutsEnabled && shortcuts.Enabled; tabs.ConfigureLayout(settings.WrapTabs, settings.MaximumTabRows); UpdateToolbarHints(); ShowSessionOffer(); SyncShortcutScope(); UpdateStatus();
            ApplyGlobalHotkey(settings.GlobalHotkey);
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
                    "ExplorerTabs", MessageBoxButtons.OK, MessageBoxIcon.Warning);
            }
        }

        private void OpenWindowPicker()
        {
            if (workspaceOperation != null || exitPreparing || creation != null) return;
            using (EnterModal())
            using (var picker = new WindowPicker(controller.Sessions.Select(s => s.Identity.Handle)))
            {
                if (picker.ShowDialog(this) != DialogResult.OK) return;
                AttachSelectedWindows(picker.SelectedWindows);
            }
        }

        private void AttachSelectedWindows(IEnumerable<WindowIdentity> selectedWindows)
        {
            if (workspaceOperation != null || exitPreparing || creation != null) return;
            foreach (var identity in selectedWindows)
            {
                if (!NativeWindowApi.IsExplorer(identity)) continue;
                try
                {
                    var session = controller.Attach(identity, content.Handle, SelectedMode, CurrentBounds());
                    controller.Activate(session, CurrentBounds(), visible: CanPresentExplorer);
                    workspaceTouched = true;
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
                controller.Activate(session, CurrentBounds(), visible: CanPresentExplorer);
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
            if (!CanDetachTabs || !controller.Sessions.Contains(session)) return;
            RunSafely("分离失败，窗口恢复记录仍保留", () =>
            {
                var preferred = TabManagementPolicy.Successor(controller.Sessions, controller.Active, new[] { session });
                controller.Detach(session);
                workspaceTouched = true;
                log.Write("Detached " + session.Identity);
                presentation.Track(session.Identity, "detach");
                EnsureActive(true, preferred);
            });
            RefreshTabs();
        }

        private void DetachAll(bool forExit)
        {
            if (!forExit && !CanDetachTabs) return;
            var identities = controller.Sessions.Select(s => s.Identity).ToArray();
            workspaceTouched |= identities.Length > 0;
            var errors = controller.DetachAll();
            foreach (var identity in identities) presentation.Track(identity, "detach-all");
            RefreshTabs();
            if (errors.Count > 0 && !forExit)
                ReportError("部分窗口尚未恢复。请重试分离，不要强杀程序。", new InvalidOperationException(string.Join("\n", errors)));
        }

        private bool CanPresentExplorer => !closing && !disposingUi && Visible && WindowState != FormWindowState.Minimized && modalDepth == 0;

        private void EnsureActive(bool raise = false, WindowSession? preferred = null)
        {
            if (controller.Active != null) return;
            var next = preferred != null && controller.Sessions.Contains(preferred) && preferred.State == SessionState.Attached
                ? preferred : controller.Sessions.FirstOrDefault(s => s.State == SessionState.Attached);
            if (next != null) controller.Activate(next, CurrentBounds(), raise && CanPresentExplorer, CanPresentExplorer);
        }

        private void LayoutCurrent()
        {
            if (closing || modalDepth > 0 || !Visible || WindowState == FormWindowState.Minimized || !content.IsHandleCreated) return;
            RunSafely("同步位置失败", () => controller.LayoutActive(CurrentBounds()));
        }

        private void PollWindows()
        {
            if (closing || modalDepth > 0 || refreshing || tabs.IsInteracting) return;
            SetCreationControls(creation != null);
            try
            {
                var beforePrune = controller.Sessions.ToArray();
                var pendingCloseBefore = beforePrune.Where(s => s.CloseRequestedUtc != null).ToArray();
                var activeBefore = controller.Active;
                int previousCount = controller.Sessions.Count;
                var errors = controller.PruneLost();
                foreach (string error in errors) log.Write("Prune failed: " + error);
                // A user-closed active window is pruned here. Prefer its left neighbour instead
                // of the first tab; closing a non-active tab leaves the active tab untouched.
                WindowSession? preferred = controller.Active == null && activeBefore != null
                    ? TabManagementPolicy.CloseSuccessor(beforePrune, activeBefore, activeBefore, controller.Sessions) : null;
                CompleteClosedTabs(beforePrune);
                string closeStatus = status.Text;
                bool refused = beforePrune.Any(s => s.CloseRequestedUtc == null && pendingCloseBefore.Contains(s));
                bool changed = previousCount != controller.Sessions.Count;
                foreach (var session in controller.Sessions)
                {
                    string title = NativeWindowApi.ReadTitle(session.Identity.Hwnd);
                    if (!string.IsNullOrEmpty(title) && title != session.Title)
                    { session.Title = title; changed = true; }
                }
                EnsureActive(preferred: preferred);
                SyncShortcutScope();
                if (changed || refused) RefreshTabs();
                if (refused) status.Text = closeStatus;
                if (closeAllStarted != default)
                {
                    if (controller.Sessions.Count == 0 && !closing)
                    {
                        closeAllStarted = default;
                        BeginInvoke(new Action(Close));
                    }
                    else if (DateTime.UtcNow - closeAllStarted > TimeSpan.FromSeconds(10))
                    {
                        closeAllStarted = default;
                        exitByClosingSessions = false;
                        exitPreparing = false;
                        RefreshTabs();
                        status.Text = "仍有会话窗口未关闭，已取消退出；请关闭阻挡的对话框后重试。";
                        log.Write("Exit by closing session windows timed out; host kept open.");
                    }
                }
                MaybeAutosave();
            }
            catch (Exception error) { log.Write("Monitor failed", error); }
        }

        private void RefreshTabs()
        {
            if (refreshing) return;
            refreshing = true;
            try
            {
                tabs.Render(controller.Sessions, controller.Active, CreateTabMenu,
                    session => directoryCache.TryGetValue(session, out var directory) ? directory : null);
                foreach (WelcomePanel welcome in content.Controls) welcome.Visible = controller.Sessions.Count == 0;
                mode.Enabled = controller.Sessions.Count == 0 && creation == null && workspaceOperation == null;
                UpdateStatus();
                SyncShortcutScope();
            }
            finally { refreshing = false; }
        }

        private void MoveTab(WindowSession session, int index)
        {
            if (workspaceOperation != null || exitPreparing) return;
            RunSafely("标签排序失败", () =>
            {
                controller.MoveTab(session, index);
                workspaceTouched = true;
                log.Write($"Tab reordered HWND=0x{session.Identity.Handle:X}; index={index}; native-mutations=none");
                RefreshTabs();
            });
        }

        private ContextMenuStrip CreateTabMenu(WindowSession session)
        {
            var menu = new ContextMenuStrip();
            HostTheme.StyleMenu(menu);
            PopulateTabMenu(menu, session);
            menu.Opening += (_, e) =>
            {
                if (workspaceOperation != null || !controller.Sessions.Contains(session)) { e.Cancel = true; return; }
                PopulateTabMenu(menu, session);
            };
            RegisterMenuLifetime(menu, false);
            return menu;
        }

        private void PopulateTabMenu(ContextMenuStrip menu, WindowSession session)
        {
            foreach (ToolStripItem item in menu.Items.Cast<ToolStripItem>().ToArray()) item.Dispose();
            menu.Items.Clear();
            var close = menu.Items.Add("关闭标签", null, (_, __) => QueueUi(() => CloseTabAsync(session)));
            close.Enabled = CanUseTab(session);
            close.ToolTipText = "关闭此标签对应的 Explorer 窗口；窗口未关闭时保留标签。";
            close.ForeColor = Color.FromArgb(166, 49, 49);
            ShowShortcut(close, ShortcutChord(HostCommand.CloseTab));
            var duplicate = menu.Items.Add("复制标签", null, (_, __) => QueueCreateFromMenu(session));
            duplicate.Enabled = CanUseTab(session) && controller.Sessions.Count < 100;
            duplicate.ToolTipText = "在此标签右侧打开当前目录的新标签，不复制文件。";
            ShowShortcut(duplicate, ShortcutChord(HostCommand.DuplicateTab));
            menu.Items.Add(new ToolStripSeparator());
            var favorite = menu.Items.Add("收藏此目录…", null, (_, __) => QueueUi(() => AddFavoriteAsync(session)));
            favorite.Enabled = CanUseTab(session) && CanReadDirectory(session) && libraryStore != null;
            favorite.ToolTipText = "为此标签的目录命名并保存到收藏。";
            var rename = menu.Items.Add("重命名标签…", null, (_, __) => QueueMenuAction(() => RenameTab(session)));
            rename.Enabled = CanUseTab(session);
            rename.ToolTipText = "只修改标签名称，不修改目录或文件名称。";
            ShowShortcut(rename, ShortcutChord(HostCommand.RenameTab));
            var reset = menu.Items.Add("恢复默认名称", null, (_, __) =>
                QueueMenuAction(() => RunSafely("恢复标签名称失败", () => { if (!CanUseTab(session)) return; controller.ResetTabTitle(session); workspaceTouched = true; RefreshTabs(); })));
            reset.Enabled = CanUseTab(session) && session.CustomTitle != null;
            reset.ToolTipText = "清除自定义标签名称，重新显示 Explorer 窗口标题。";
            menu.Items.Add(new ToolStripSeparator());
            var detach = menu.Items.Add("分离此标签", null, (_, __) => QueueMenuAction(() => Detach(session)));
            detach.Enabled = CanDetachTabs && controller.Sessions.Contains(session);
            detach.ToolTipText = "将此标签恢复为独立 Explorer 窗口，目录保持打开。";
            var others = menu.Items.Add("分离其他标签", null, (_, __) => QueueMenuAction(() => DetachTabs(session, TabDetachScope.Others)));
            var toRight = menu.Items.Add("分离右侧标签", null, (_, __) => QueueMenuAction(() => DetachTabs(session, TabDetachScope.ToRight)));
            others.Enabled = CanDetachTabs && controller.Sessions.Contains(session) && TabManagementPolicy.Select(controller.Sessions, session, TabDetachScope.Others).Count > 0;
            toRight.Enabled = CanDetachTabs && controller.Sessions.Contains(session) && TabManagementPolicy.Select(controller.Sessions, session, TabDetachScope.ToRight).Count > 0;
            others.ToolTipText = "保留此标签，将其他标签恢复为独立 Explorer 窗口。";
            toRight.ToolTipText = "将此标签右侧的标签恢复为独立 Explorer 窗口。";
            var all = menu.Items.Add("分离所有标签", null, (_, __) => QueueMenuAction(() => DetachAll(false)));
            all.Enabled = CanDetachTabs;
            all.ToolTipText = "将所有标签恢复为独立 Explorer 窗口；ExplorerTabs 保持运行。";
            menu.ShowItemToolTips = true;
        }

        private void RenameTab(WindowSession session)
        {
            if (!CanUseTab(session)) return;
            using (EnterModal())
            using (var dialog = new RenameTabDialog(session.DisplayTitle))
            {
                if (dialog.ShowDialog(this) != DialogResult.OK) return;
                RunSafely("标签重命名失败", () =>
                {
                    controller.RenameTab(session, dialog.TabTitle);
                    workspaceTouched = true;
                    log.Write($"Tab renamed HWND=0x{session.Identity.Handle:X}; native-mutations=none");
                    RefreshTabs();
                });
            }
        }

        private void DetachTabs(WindowSession anchor, TabDetachScope scope)
        {
            if (!CanDetachTabs || !controller.Sessions.Contains(anchor)) return;
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
            controller.Active, Visible && shortcuts.Checked && !closing && !exitPreparing && workspaceOperation == null && modalDepth == 0 && !tabs.IsInteracting && WindowState != FormWindowState.Minimized,
            controller.Sessions.Count(s => s.State == SessionState.Attached) >= 2, creation == null && workspaceOperation == null && !explorerTabs.IsBusy, settings.KeyBindings);

        private void QueueShortcut(int direction)
        {
            // Dedicated hook thread. Bound queued work while the UI is busy in a native call.
            if (Interlocked.CompareExchange(ref queuedShortcut, 1, 0) != 0) return;
            try
            {
                BeginInvoke(new Action(() =>
                {
                    Interlocked.Exchange(ref queuedShortcut, 0);
                    if (closing || IsDisposed || modalDepth > 0 || tabs.IsInteracting || !Visible || WindowState == FormWindowState.Minimized ||
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
            if (creation != null || workspaceOperation != null || closeReading.Count > 0 || controller.Sessions.Any(s => s.CloseRequestedUtc != null)) return;
            status.Text =
            $"{controller.Sessions.Count} 个标签 · {(SelectedMode == HostingMode.Embedded ? "实验性嵌入" : "顶层分组：保留 Explorer 任务栏项")}" +
            " · 关闭会结束会话窗口；分离会恢复窗口";
        }

        private void OnClosing(object? sender, FormClosingEventArgs e)
        {
            if (!restoringOnExit && e.CloseReason == CloseReason.UserClosing)
            {
                CloseSessionsForExit(e);
                return;
            }
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
                closing = false; exitCaptureDone = false; restoringOnExit = false; modalDepth--; monitor.Start();
                RefreshTabs();
                ReportError("还有窗口没有完成恢复，已阻止退出。请重试“分离所有标签”。", new InvalidOperationException(string.Join("\n", errors)));
            }
            else
            {
                SaveSettingsOnExit();
                log.Write("All windows detached; host closing.");
            }
        }

        /// <summary>X / Alt+F4 / 应用菜单“关闭所有标签并退出”：请求精确关闭所有会话窗口，全部消失后退出。
        /// 任一窗口未消失时保持宿主运行，不 kill、不恢复、不猜测其他窗口。</summary>
        private void CloseSessionsForExit(FormClosingEventArgs e)
        {
            if (controller.Sessions.Count == 0)
            {
                CompleteExit("All session windows closed; host closing.");
                return;
            }
            e.Cancel = true;
            if (closeAllStarted != default) return;
            closeAllStarted = DateTime.UtcNow;
            exitByClosingSessions = true;
            exitPreparing = true;
            creation?.Cancel(); workspaceOperation?.Cancel();
            SyncShortcutScope();
            int requested = 0;
            foreach (var session in controller.Sessions.Where(s => s.State == SessionState.Attached).ToArray())
            {
                try { controller.RequestClose(session); requested++; }
                catch (Exception error) { log.Write($"Exit close request failed HWND=0x{session.Identity.Handle:X}", error); }
            }
            status.Text = requested > 0 ? "正在关闭所有会话窗口并退出…" : "没有可关闭的标签窗口；请使用“退出并保留窗口”恢复后退出。";
            log.Write($"Exit by closing session windows; requested={requested}; total={controller.Sessions.Count}");
        }

        private void CompleteExit(string reason)
        {
            closeAllStarted = default; restoringOnExit = false;
            closing = true; modalDepth++; SyncShortcutScope(); monitor.Stop();
            presentation.Snapshot("closing-after-close-all");
            SaveSettingsOnExit();
            log.Write(reason);
        }

        /// <summary>User close (X / Alt+F4 / menu) closes the tab Explorer windows.</summary>
        private void RequestExitByClosingSessions() { if (closing || exitPreparing || disposingUi || IsDisposed) return; restoringOnExit = false; Close(); }

        /// <summary>Explicit restore-and-exit path from the detach menu.</summary>
        private void CloseRestoringWindows() { if (closing || exitPreparing || disposingUi || IsDisposed) return; exitByClosingSessions = false; restoringOnExit = true; Close(); }

        public void EmergencyDetach()
        {
            foreach (string error in controller.DetachAll()) log.Write("Emergency detach failed: " + error);
        }

        public void ReportError(string message, Exception error)
        {
            log.Write(message, error);
            using (EnterModal())
                MessageBox.Show(this, message + "\n\n" + error.Message + "\n\n日志：" + log.FilePath,
                    "ExplorerTabs", MessageBoxButtons.OK, MessageBoxIcon.Error);
        }

        private void ExportDiagnostics()
        {
            // Capture before a modal dialog changes visibility in grouped mode.
            string windowState = presentation.Snapshot("user-export-before-dialog", discoverStandalone: true) +
                Environment.NewLine + $"Host UI: version={typeof(MainForm).Assembly.GetName().Version} dpi={DeviceDpi} state={WindowState} bounds={Bounds} client={ClientRectangle} padding={Padding} chrome={chrome?.Bounds} toolbar={commandToolbar?.Bounds} content={content.Bounds} sessionOfferVisible={sessionOffer.Visible} tabWrap={tabs.WrapContents} tabHeight={tabs.Height} horizontalScroll={tabs.HasHorizontalScrollbar()} verticalScroll={tabs.HasVerticalScrollbar()} scroll={tabs.ScrollPosition()}";
            using (EnterModal())
            using (var dialog = new SaveFileDialog
            {
                Title = "保存诊断包（可能含目录标题与路径，不会自动上传）",
                Filter = "诊断包 (*.zip)|*.zip", DefaultExt = "zip", AddExtension = true,
                FileName = "ExplorerTabs-" + DateTime.Now.ToString("yyyyMMdd-HHmmss-fff") + ".zip",
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
                if (disposingUi || Disposing || IsDisposed) return;
                SyncShortcutScope();
                if (modalDepth != 0 || closing) return;
                try
                {
                    EnsureActive(true);
                    if (CanPresentExplorer)
                    { controller.SetActiveVisible(true); controller.LayoutActive(CurrentBounds(), true); }
                }
                catch (Exception error) { log.Write("Restore after dialog failed", error); }
            });
        }

        protected override void Dispose(bool disposing)
        {
            if (disposing)
            {
                disposingUi = true;
                nativeMenuScope?.Dispose(); nativeMenuScope = null;
                foreach (var close in closeReading.Values.ToArray()) close.Cancel();
                workspaceOperation?.Cancel(); creation?.Cancel(); keyboard?.Dispose(); monitor.Dispose(); closeMonitor.Stop(); closeMonitor.Dispose();
                cancelRevealTimer.Stop(); cancelRevealTimer.Dispose();
                DisposeManagedMenus(); presentation.Dispose(); chromeTips.Dispose();
                globalHotkeys.Unregister(); hotkeyRegistrationActive = false; DisposeTray();
            }
            base.Dispose(disposing);
            if (disposing) { applicationIcon?.Dispose(); applicationIcon = null; }
        }
        private sealed class ModalScope : IDisposable
        {
            private Action? cleanup;
            public ModalScope(Action cleanup) { this.cleanup = cleanup; }
            public void Dispose() { var action = cleanup; cleanup = null; action?.Invoke(); }
        }
    }
}
