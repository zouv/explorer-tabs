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
        private async Task<string> ReadTabDirectoryAsync(WindowSession session, CancellationToken token)
        {
            string path;
            if (DirectoryReader != null)
            {
                for (int wait = 0; DirectoryReader.IsReading && wait < 20; wait++) await Task.Delay(25, token);
                if (DirectoryReader.IsReading) throw new InvalidOperationException("目录读取仍在执行，请稍后重试。");
            }
            if (DirectoryReader != null) path = await DirectoryReader.ReadDirectoryAsync(session.Identity, token);
            else if (!directoryCache.TryGetValue(session, out path!)) throw new InvalidOperationException("目录读取服务不可用。");
            token.ThrowIfCancellationRequested();
            if (!controller.Sessions.Contains(session) || session.State != SessionState.Attached) throw new InvalidOperationException("源标签已失效。");
            path = FolderTarget.Normalize(path); directoryCache[session] = path; return path;
        }
        private async Task<SavedWorkspace?> CaptureWorkspaceAsync(CancellationToken token)
        {
            var sessions = controller.Sessions.Where(s => s.State == SessionState.Attached && s.CloseRequestedUtc == null).ToArray();
            if (sessions.Length != controller.Sessions.Count || closeReading.Count > 0) throw new InvalidOperationException("标签正在关闭或需要恢复，旧会话保留。");
            var aliases = sessions.Select(s => s.CustomTitle).ToArray();
            var active = controller.Active; var saved = new SavedWorkspace(); var failures = new List<string>();
            foreach (var session in sessions)
            {
                token.ThrowIfCancellationRequested();
                try
                {
                    string path;
                    using (var item = CancellationTokenSource.CreateLinkedTokenSource(token)) { item.CancelAfter(2500); path = await ReadTabDirectoryAsync(session, item.Token); }
                    if (session == active) saved.ActiveIndex = saved.Tabs.Count;
                    saved.Tabs.Add(new WorkspaceTab { Directory = path, Alias = aliases[Array.IndexOf(sessions, session)] });
                }
                catch (Exception error) when (!(error is OperationCanceledException && token.IsCancellationRequested))
                {
                    failures.Add(session.DisplayTitle + ": " + error.Message);
                    if (DirectoryReader?.IsReading == true) break;
                }
            }
            token.ThrowIfCancellationRequested();
            if (!controller.Sessions.SequenceEqual(sessions) || controller.Active != active || sessions.Where((s, i) => s.State != SessionState.Attached || s.CloseRequestedUtc != null || s.CustomTitle != aliases[i]).Any())
                throw new InvalidOperationException("读取期间标签已变化，旧会话保留，请重试。");
            if (failures.Count > 0)
            {
                log.Write("Workspace capture incomplete; prior file retained: " + string.Join(" | ", failures));
                throw new InvalidOperationException("部分标签目录无法读取，未覆盖旧会话 / 标签组。除“此电脑”外的虚拟目录暂不支持（详见日志）。");
            }
            if (sessions.Length > 0 && saved.Tabs.Count == 0) throw new InvalidOperationException("没有可保存的目录，旧会话保留。除“此电脑”外的虚拟目录暂不支持。");
            return saved.Tabs.Count == 0 ? null : saved;
        }
        private async Task SaveSessionAsync(bool automatic)
        {
            if (workspaceOperation != null || libraryStore == null || creation != null) return;
            var operation = new CancellationTokenSource(automatic ? 7000 : 30000); workspaceOperation = operation; workspaceOperationSilent = automatic; SetCreationControls(false);
            try
            {
                var saved = await CaptureWorkspaceAsync(operation.Token);
                if (IsDisposed || closing) return;
                var copy = library.Copy(); copy.LastSession = saved;
                if (!WorkspaceLibrary.SameSession(library.LastSession, saved)) CommitLibrary(copy);
                sessionOffer.Visible = false;
                if (!automatic) status.Text = $"会话已保存：{saved?.Tabs.Count ?? 0} 个普通目录；下次启动可主动恢复。";
            }
            catch (Exception error)
            {
                if (automatic) { log.Write("Session auto-save failed; prior file retained", error); if (!IsDisposed) status.Text = "会话自动保存未完成，旧会话保留（详见日志）。"; }
                else throw;
            }
            finally { workspaceOperation = null; workspaceOperationSilent = false; operation.Dispose(); if (!IsDisposed && !closing) SetCreationControls(creation != null); }
        }
        private async Task SaveGroupAsync()
        {
            if (workspaceOperation != null || creation != null) return;
            string? name = AskName("保存当前标签组", "我的工作空间"); if (name == null) return;
            var operation = new CancellationTokenSource(30000); workspaceOperation = operation; SetCreationControls(false);
            try
            {
                var group = await CaptureWorkspaceAsync(operation.Token); if (group == null) throw new InvalidOperationException("当前没有可保存的目录。");
                group.Name = name; var copy = library.Copy(); var old = copy.Groups.FirstOrDefault(g => string.Equals(g.Name, name, StringComparison.OrdinalIgnoreCase));
                if (old != null)
                {
                    using (EnterModal()) if (MessageBox.Show(this, "同名标签组已存在，是否替换保存内容？（不关闭窗口）", "保存标签组", MessageBoxButtons.YesNo, MessageBoxIcon.Question) != DialogResult.Yes) return;
                    group.Id = old.Id; copy.Groups.Remove(old);
                }
                copy.Groups.Add(group); CommitLibrary(copy); status.Text = "标签组已保存：" + name;
            }
            finally { workspaceOperation = null; operation.Dispose(); if (!IsDisposed && !closing) SetCreationControls(creation != null); }
        }
        private async Task OpenWorkspaceAsync(SavedWorkspace saved)
        {
            if (closing || exitPreparing || creation != null || workspaceOperation != null || explorerTabs.IsBusy) return;
            WorkspaceLibrary.Validate(new WorkspaceLibrary { LastSession = saved });
            if (saved.Tabs.Count + controller.Sessions.Count > 100) throw new InvalidOperationException("标签总数将超过 100，请先关闭或分离部分标签。");
            var operation = new CancellationTokenSource(); workspaceOperation = operation; SetCreationControls(false);
            sessionOffer.Visible = false; var failures = new List<string>(); WindowSession? first = null, desired = null;
            try
            {
                for (int i = 0; i < saved.Tabs.Count; i++)
                {
                    operation.Token.ThrowIfCancellationRequested(); status.Text = $"正在打开 {saved.Name}：{i + 1} / {saved.Tabs.Count} · 可取消";
                    try
                    {
                        var entry = saved.Tabs[i]; CreatedExplorer? result = null;
                        try
                        {
                            using (var item = CancellationTokenSource.CreateLinkedTokenSource(operation.Token))
                            {
                                item.CancelAfter(15000);
                                result = await explorerTabs.CreateAsync(entry.Directory, null, controller.Sessions.Select(s => s.Identity.Handle).ToArray(), item.Token);
                                while (modalDepth > 0 || tabs.IsInteracting) await Task.Delay(50, item.Token); item.Token.ThrowIfCancellationRequested();
                            }
                            operation.Token.ThrowIfCancellationRequested(); if (closing || IsDisposed || exitPreparing) return;
                            AttachCreated(result, SelectedMode); var session = controller.Active!;
                            if (entry.Alias != null) controller.RenameTab(session, entry.Alias);
                            if (first == null) first = session; if (i == saved.ActiveIndex) desired = session;
                        }
                        finally { result?.Dispose(); }
                    }
                    catch (Exception error) when (!(error is OperationCanceledException && operation.IsCancellationRequested)) { failures.Add(saved.Tabs[i].Directory + ": " + error.Message); }
                    for (int wait = 0; explorerTabs.IsBusy && wait < 20; wait++) await Task.Delay(25, operation.Token);
                    if (explorerTabs.IsBusy) { failures.Add("Shell 调用尚未退出，剩余目录未创建，请稍后重试。"); break; }
                }
                if (desired != null || first != null) SelectTab(desired ?? first!);
                RefreshTabs(); status.Text = $"{saved.Name} 已打开；失败 / 跳过 {failures.Count} 项。";
                if (failures.Count > 0) { log.Write("Workspace restore partial: " + string.Join(" | ", failures)); using (EnterModal()) MessageBox.Show(this, string.Join("\n", failures), "部分目录未打开（成功标签保留）", MessageBoxButtons.OK, MessageBoxIcon.Warning); }
            }
            catch (OperationCanceledException) { if (!IsDisposed) status.Text = "打开已取消，已完成的标签保留；迟到窗口不接管。"; }
            finally { workspaceOperation = null; operation.Dispose(); if (!IsDisposed && !closing) SetCreationControls(creation != null); }
        }
        private void MaybeAutosave()
        {
            if (!settings.RememberSession || !workspaceTouched || libraryStore == null || exitPreparing || creation != null || workspaceOperation != null || DirectoryReader?.IsReading == true || DateTime.UtcNow < nextAutosave) return;
            nextAutosave = DateTime.UtcNow.AddSeconds(20); _ = SaveSessionAsync(true);
        }
        private async void OnClosingWithSession(object? sender, FormClosingEventArgs e)
        {
            if (!exitByClosingSessions && !exitCaptureDone && settings.RememberSession && workspaceTouched && libraryStore != null)
            {
                e.Cancel = true; if (exitPreparing) return; exitPreparing = true; SyncShortcutScope(); creation?.Cancel(); workspaceOperation?.Cancel();
                foreach (var close in closeReading.Values.ToArray()) close.Cancel();
                try { while (workspaceOperation != null || creation != null) { if (IsDisposed) return; await Task.Delay(25); } await SaveSessionAsync(true); }
                catch (Exception error) { log.Write("Exit session capture failed; prior file retained", error); }
                finally { exitCaptureDone = true; exitPreparing = false; }
                if (!IsDisposed) BeginInvoke(new Action(Close)); return;
            }
            OnClosing(sender, e);
            if (e.Cancel) exitCaptureDone = false;
        }
    }
}
