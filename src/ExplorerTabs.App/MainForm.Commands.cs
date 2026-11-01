using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Forms;
using ExplorerTabs.Core;
namespace ExplorerTabs.App
{
    internal sealed partial class MainForm
    {
        private readonly System.Windows.Forms.Timer closeMonitor = new System.Windows.Forms.Timer { Interval = 75 };

        private async Task CloseTabAsync(WindowSession session)
        {
            if (closing || exitPreparing || creation != null || !controller.Sessions.Contains(session) || session.State != SessionState.Attached || session.CloseRequestedUtc != null || workspaceOperation != null || closeReading.ContainsKey(session)) return;
            var token = new CancellationTokenSource(); closeReading.Add(session, token);
            var elapsed = System.Diagnostics.Stopwatch.StartNew();
            tabs.SetCloseReading(session, true); RefreshTabs();
            try
            {
                WorkspaceTab? reopen = null;
                try
                {
                    var read = ReadTabDirectoryAsync(session, token.Token);
                    if (await Task.WhenAny(read, Task.Delay(150)) == read)
                        reopen = new WorkspaceTab { Directory = await read, Alias = session.CustomTitle };
                    else
                    {
                        token.Cancel(); _ = ObserveCloseReadAsync(read);
                        log.Write("Close: directory read exceeded 150ms; no stale cached directory added to reopen stack.");
                    }
                }
                catch (Exception error) { log.Write("Close: reopen directory unavailable; still close only explicit identity", error); }
                if (closing || IsDisposed || exitPreparing || workspaceOperation != null || !controller.Sessions.Contains(session)) return;
                controller.RequestClose(session); if (reopen != null) pendingClosed[session] = reopen;
                workspaceTouched = true;
                log.Write($"Close requested HWND=0x{session.Identity.Handle:X}; elapsedMs={elapsed.ElapsedMilliseconds}; journal retained until disappearance; no detach/process kill");
                // Switch content now, but retain the closing tab/journal until identity disappearance.
                if (controller.Active == session)
                {
                    var available = controller.Sessions.Where(s => s != session && s.State == SessionState.Attached && s.CloseRequestedUtc == null).ToArray();
                    var next = TabManagementPolicy.CloseSuccessor(controller.Sessions, session, session, available);
                    if (next != null)
                        try { controller.Activate(next, CurrentBounds(), CanPresentExplorer, CanPresentExplorer); }
                        catch (Exception error) { log.Write("Close successor activation failed; closing session retained", error); }
                }
                closeMonitor.Start();
                status.Text = "已请求关闭；正在快速确认目标窗口，不强制结束进程。";
            }
            finally
            {
                closeReading.Remove(session); token.Dispose();
                if (!tabs.IsDisposed) tabs.SetCloseReading(session, false);
                if (!closing && !IsDisposed) RefreshTabs();
            }
        }

        private async Task ObserveCloseReadAsync(Task<string> read)
        { try { await read; } catch (Exception error) { log.Write("Close: late directory read discarded", error); } }

        private void PollClosingWindows()
        {
            if (closing || disposingUi || refreshing || modalDepth > 0 || tabs.IsInteracting) return;
            var targets = controller.Sessions.Where(s => s.CloseRequestedUtc != null).ToArray();
            if (targets.Length == 0) { closeMonitor.Stop(); return; }
            try
            {
                var before = controller.Sessions.ToArray(); var active = controller.Active;
                foreach (string error in controller.PruneTargets(targets)) log.Write("Fast close prune failed: " + error);
                foreach (var target in targets.Where(s => !controller.Sessions.Contains(s)))
                    log.Write($"Close confirmed HWND=0x{target.Identity.Handle:X}; elapsedMs={(DateTime.UtcNow - target.CloseRequestedUtc!.Value).TotalMilliseconds:F0}");
                CompleteClosedTabs(targets);
                string closeStatus = status.Text;
                bool refused = targets.Any(s => s.CloseRequestedUtc == null);
                var preferred = active != null && controller.Active == null ? TabManagementPolicy.CloseSuccessor(before, active, active, controller.Sessions) : null;
                EnsureActive(preferred: preferred);
                if (!before.SequenceEqual(controller.Sessions) || targets.Any(s => s.CloseRequestedUtc == null)) RefreshTabs();
                if (refused) status.Text = closeStatus;
                if (!controller.Sessions.Any(s => s.CloseRequestedUtc != null)) closeMonitor.Stop();
            }
            catch (Exception error) { log.Write("Fast close confirmation failed; journal retained", error); }
        }

        private void CompleteClosedTabs(IEnumerable<WindowSession> before)
        {
            foreach (var session in before)
            {
                if (!controller.Sessions.Contains(session))
                {
                    if (pendingClosed.TryGetValue(session, out var entry))
                    {
                        if (closedTabs.Count >= 20) { var recent = closedTabs.Take(19).Reverse().ToArray(); closedTabs.Clear(); foreach (var item in recent) closedTabs.Push(item); }
                        closedTabs.Push(entry); pendingClosed.Remove(session);
                    }
                    directoryCache.Remove(session); workspaceTouched = true;
                }
                else if (session.State == SessionState.Attached && session.CloseRequestedUtc != null && DateTime.UtcNow - session.CloseRequestedUtc.Value > TimeSpan.FromSeconds(6))
                {
                    controller.RetryClose(session); pendingClosed.Remove(session);
                    log.Write($"Close still pending/refused HWND=0x{session.Identity.Handle:X}; kept managed, no forced kill");
                    status.Text = "Explorer 尚未关闭（可能有对话框），标签仍保留；可再次关闭或分离。";
                }
            }
        }
        private async Task ReopenTabAsync()
        {
            if (closedTabs.Count == 0 || workspaceOperation != null) return;
            var entry = closedTabs.Peek(); int count = controller.Sessions.Count;
            await OpenWorkspaceAsync(new SavedWorkspace { Name = "关闭的标签", Tabs = new List<WorkspaceTab> { entry.Copy() } });
            if (controller.Sessions.Count > count && closedTabs.Count > 0 && closedTabs.Peek() == entry) closedTabs.Pop();
        }
        private async Task ShowTabPickerAsync()
        {
            if (workspaceOperation != null || creation != null || closing || exitPreparing) return;
            var operation = new CancellationTokenSource(2500); workspaceOperation = operation; SetCreationControls(false);
            try
            {
                foreach (var session in controller.Sessions.Where(s => s.State == SessionState.Attached).ToArray())
                {
                    try { await ReadTabDirectoryAsync(session, operation.Token); }
                    catch (Exception error) { log.Write("Tab search: current directory unavailable (cached label only)", error); }
                    if (operation.IsCancellationRequested || DirectoryReader?.IsReading == true) break;
                }
            }
            finally { workspaceOperation = null; operation.Dispose(); if (!IsDisposed && !closing) SetCreationControls(false); }
            if (IsDisposed || closing || exitPreparing) return;
            var entries = controller.Sessions.Where(s => s.State == SessionState.Attached).Select((s, i) => new PickerEntry((i + 1) + "  " + s.DisplayTitle, directoryCache.TryGetValue(s, out var path) ? path : s.Title, s)).ToArray();
            WindowSession? chosen;
            using (EnterModal()) using (var picker = new QuickPickerDialog("搜索标签", entries)) { if (picker.ShowDialog(this) != DialogResult.OK) return; chosen = picker.SelectedValue as WindowSession; }
            if (chosen != null && controller.Sessions.Contains(chosen)) { SelectTab(chosen); NativeWindowApi.FocusAfterShortcut(chosen.Snapshot); }
        }
        private bool CanExecuteMenuCommand(HostCommand command)
        {
            if (!MenuIdle) return false;
            switch (command)
            {
                case HostCommand.CloseTab: return CanUseTab(controller.Active);
                case HostCommand.DuplicateTab: return CanUseTab(controller.Active) && controller.Sessions.Count < 100;
                case HostCommand.RenameTab: return CanUseTab(controller.Active);
                case HostCommand.ReopenTab: return closedTabs.Count > 0 && controller.Sessions.Count < 100;
                case HostCommand.SaveSession: return CanCaptureWorkspace;
                case HostCommand.NewTab: return controller.Sessions.Count < 100;
                case HostCommand.SearchTabs: return controller.Sessions.Any(s => s.State == SessionState.Attached);
                default: return true;
            }
        }
        private IReadOnlyList<PickerEntry> CreateCommandEntries()
        {
            var entries = new List<PickerEntry>();
            foreach (var command in new[] { HostCommand.NewTab, HostCommand.CloseTab, HostCommand.SearchTabs, HostCommand.ReopenTab, HostCommand.DuplicateTab, HostCommand.RenameTab, HostCommand.SaveSession, HostCommand.Settings })
                if (CanExecuteMenuCommand(command)) entries.Add(new PickerEntry(HostShortcutCatalog.Name(command), ShortcutChord(command), command));
            foreach (var record in library.Favorites)
            {
                var favorite = record.Copy();
                if (MenuIdle && controller.Sessions.Count < 100)
                    entries.Add(new PickerEntry("打开收藏目录 · " + favorite.Name, FolderTarget.Display(favorite.Directory), favorite));
            }
            foreach (var record in library.Groups)
            {
                var group = record.Copy();
                if (CanOpenWorkspace(group)) entries.Add(new PickerEntry("追加打开标签组 · " + group.Name, group.Tabs.Count + " 个标签", group));
            }
            if (MenuIdle && libraryStore != null)
                entries.Add(new PickerEntry("管理收藏与标签组…", "重命名、移除收藏或删除标签组，不关闭标签", new Action(ShowLibraryManager)));
            return entries;
        }
        private void ShowCommandPicker()
        {
            if (!MenuIdle) return;
            var entries = CreateCommandEntries();
            object? chosen;
            using (EnterModal()) using (var picker = new QuickPickerDialog("命令面板", entries)) { if (picker.ShowDialog(this) != DialogResult.OK) return; chosen = picker.SelectedValue; }
            if (chosen is HostCommand command && CanExecuteMenuCommand(command)) QueueUi(() => ExecuteCommandAsync(command));
            else if (chosen is FolderBookmark f) QueueUi(() => OpenWorkspaceAsync(new SavedWorkspace { Name = f.Name, Tabs = new List<WorkspaceTab> { new WorkspaceTab { Directory = f.Directory } } }));
            else if (chosen is SavedWorkspace group) QueueUi(() => OpenWorkspaceAsync(group.Copy()));
            else if (chosen is Action action) QueueMenuAction(action);
        }
        private void QueueCommand(HostCommand command)
        {
            if (Interlocked.CompareExchange(ref queuedShortcut, 1, 0) != 0) return;
            try
            {
                BeginInvoke(new Action(async () =>
                {
                    Interlocked.Exchange(ref queuedShortcut, 0);
                    if (closing || exitPreparing || IsDisposed || modalDepth > 0 || tabs.IsInteracting || !Visible || WindowState == FormWindowState.Minimized || !shortcuts.Checked || !TabKeyboardHook.IsTargetForeground(Handle, controller.Active)) return;
                    try { await ExecuteCommandAsync(command); } catch (Exception error) { ReportError("快捷键操作未完成", error); }
                }));
            }
            catch (InvalidOperationException) { Interlocked.Exchange(ref queuedShortcut, 0); }
        }
        private async Task ExecuteCommandAsync(HostCommand command)
        {
            if (workspaceOperation != null || exitPreparing || closing) return;
            var active = controller.Active;
            switch (command)
            {
                case HostCommand.NewTab: await CreateTabAsync(null); break;
                case HostCommand.CloseTab: if (active != null) await CloseTabAsync(active); break;
                case HostCommand.SearchTabs: await ShowTabPickerAsync(); break;
                case HostCommand.Commands: ShowCommandPicker(); break;
                case HostCommand.ReopenTab: await ReopenTabAsync(); break;
                case HostCommand.DuplicateTab: if (active != null) await CreateTabAsync(active); break;
                case HostCommand.Settings: ShowSettings(); break;
                case HostCommand.RenameTab: if (active != null) RenameTab(active); break;
                case HostCommand.SaveSession: await SaveSessionAsync(false); break;
                default:
                    var available = controller.Sessions.Where(s => s.State == SessionState.Attached).ToArray(); WindowSession? target = null;
                    if (command >= HostCommand.Tab1) { int index = command == HostCommand.Tab9 ? available.Length - 1 : (int)command - (int)HostCommand.Tab1; if (index >= 0 && index < available.Length) target = available[index]; }
                    else target = controller.Adjacent(command == HostCommand.NextTab || command == HostCommand.NextPage ? 1 : -1);
                    if (target != null) { SelectTab(target); NativeWindowApi.FocusAfterShortcut(target.Snapshot); }
                    break;
            }
        }
    }
}
