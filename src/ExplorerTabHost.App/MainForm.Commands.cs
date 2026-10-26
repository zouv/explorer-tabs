using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Forms;
using ExplorerTabHost.Core;
namespace ExplorerTabHost.App
{
    internal sealed partial class MainForm
    {
        private async Task CloseTabAsync(WindowSession session)
        {
            if (closing || exitPreparing || creation != null || !controller.Sessions.Contains(session) || session.State != SessionState.Attached || session.CloseRequestedUtc != null || workspaceOperation != null || closeReading.ContainsKey(session)) return;
            var token = new CancellationTokenSource(1200); closeReading.Add(session, token);
            try
            {
                WorkspaceTab? reopen = null;
                try { reopen = new WorkspaceTab { Directory = await ReadTabDirectoryAsync(session, token.Token), Alias = session.CustomTitle }; }
                catch (Exception error) { log.Write("Close: reopen directory unavailable; still close only explicit identity", error); }
                if (closing || IsDisposed || exitPreparing || workspaceOperation != null || !controller.Sessions.Contains(session)) return;
                controller.RequestClose(session); if (reopen != null) pendingClosed[session] = reopen;
                workspaceTouched = true; log.Write($"Close requested HWND=0x{session.Identity.Handle:X}; journal retained until disappearance; no detach/process kill");
                RefreshTabs(); status.Text = "已向目标 Explorer 请求关闭，等待窗口消失；不强制结束进程。";
            }
            finally { closeReading.Remove(session); token.Dispose(); }
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
            await OpenWorkspaceAsync(new SavedWorkspace { Name = "刚关闭的目录", Tabs = new List<WorkspaceTab> { entry.Copy() } });
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
            using (EnterModal()) using (var picker = new QuickPickerDialog("搜索标签 · Ctrl+P", entries)) { if (picker.ShowDialog(this) != DialogResult.OK) return; chosen = picker.SelectedValue as WindowSession; }
            if (chosen != null && controller.Sessions.Contains(chosen)) { SelectTab(chosen); NativeWindowApi.FocusAfterShortcut(chosen.Snapshot); }
        }
        private void ShowCommandPicker()
        {
            var entries = new List<PickerEntry>();
            foreach (var c in new[] { HostCommand.NewTab, HostCommand.CloseTab, HostCommand.SearchTabs, HostCommand.ReopenTab, HostCommand.DuplicateTab, HostCommand.RenameTab, HostCommand.SaveSession, HostCommand.Settings })
                entries.Add(new PickerEntry(HostShortcutCatalog.Name(c), settings.KeyBindings.First(b => b.Command == c).Chord, c));
            foreach (var favorite in library.Favorites) entries.Add(new PickerEntry("打开常用目录 · " + favorite.Name, favorite.Directory, favorite));
            foreach (var group in library.Groups) entries.Add(new PickerEntry("打开标签组 · " + group.Name, group.Tabs.Count + " 个目录", group));
            object? chosen;
            using (EnterModal()) using (var picker = new QuickPickerDialog("命令面板 · Ctrl+Shift+P", entries)) { if (picker.ShowDialog(this) != DialogResult.OK) return; chosen = picker.SelectedValue; }
            if (chosen is HostCommand command) QueueUi(() => ExecuteCommandAsync(command));
            else if (chosen is FolderBookmark f) QueueUi(() => OpenWorkspaceAsync(new SavedWorkspace { Name = f.Name, Tabs = new List<WorkspaceTab> { new WorkspaceTab { Directory = f.Directory } } }));
            else if (chosen is SavedWorkspace group) QueueUi(() => OpenWorkspaceAsync(group.Copy()));
        }
        private void QueueCommand(HostCommand command)
        {
            if (Interlocked.CompareExchange(ref queuedShortcut, 1, 0) != 0) return;
            try
            {
                BeginInvoke(new Action(async () =>
                {
                    Interlocked.Exchange(ref queuedShortcut, 0);
                    if (closing || exitPreparing || IsDisposed || modalDepth > 0 || tabs.IsInteracting || WindowState == FormWindowState.Minimized || !shortcuts.Checked || !TabKeyboardHook.IsTargetForeground(Handle, controller.Active)) return;
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
