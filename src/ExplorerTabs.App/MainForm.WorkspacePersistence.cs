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
                    using (var item = CancellationTokenSource.CreateLinkedTokenSource(token)) { item.CancelAfter(WorkspaceCapturePolicy.ItemTimeoutMilliseconds); path = await ReadTabDirectoryAsync(session, item.Token); }
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
            var operation = new CancellationTokenSource(WorkspaceCapturePolicy.TimeoutMilliseconds(controller.Sessions.Count, automatic, exitPreparing)); workspaceOperation = operation; workspaceOperationSilent = automatic; SetCreationControls(false);
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
            if (!CanCaptureWorkspace) return;
            string? name = AskName("保存当前标签组", "我的标签组"); if (name == null) return;
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
        // Tests shorten this to keep retry paths bounded; production uses the policy backoff.
        internal Func<int, TimeSpan> RestoreRetryDelay { get; set; } = WorkspaceRestorePolicy.RetryDelay;
        private async Task OpenWorkspaceAsync(SavedWorkspace saved)
        {
            if (closing || exitPreparing || creation != null || workspaceOperation != null || explorerTabs.IsBusy) return;
            WorkspaceLibrary.Validate(new WorkspaceLibrary { LastSession = saved });
            if (saved.Tabs.Count + controller.Sessions.Count > 100) throw new InvalidOperationException("标签总数将超过 100，请先关闭或分离部分标签。");
            var operation = new CancellationTokenSource(); workspaceOperation = operation; SetCreationControls(false);
            sessionOffer.Visible = false; sessionOfferDismissed = true;
            var failures = new List<string>(); WindowSession? first = null, desired = null;
            int restored = 0; bool stopped = false;
            try
            {
                BeginRestoreBanner(saved);
                status.Text = $"正在恢复「{saved.Name}」；可随时停止，已打开的标签会保留。";
                log.Write($"Workspace restore started; session={saved.Name}; tabs={saved.Tabs.Count}");
                for (int i = 0; i < saved.Tabs.Count; i++)
                {
                    var entry = saved.Tabs[i]; int attempt = 0;
                    while (true)
                    {
                        operation.Token.ThrowIfCancellationRequested();
                        ShowRestoreProgress(saved, i, attempt);
                        try
                        {
                            await WaitForShellIdleAsync(saved, i, operation.Token);
                            var session = await CreateRestoredTabAsync(entry, operation.Token);
                            if (session == null) { stopped = true; break; }
                            restored++;
                            if (first == null) first = session;
                            if (i == saved.ActiveIndex) desired = session;
                            break;
                        }
                        catch (Exception error) when (!(error is OperationCanceledException && operation.IsCancellationRequested))
                        {
                            attempt++;
                            if (!WorkspaceRestorePolicy.IsRetryable(error))
                            {
                                string reason = entry.Directory + "：" + WorkspaceRestorePolicy.Describe(error);
                                failures.Add(reason);
                                log.Write("Workspace restore item failed; " + reason);
                                break;
                            }
                            var delay = RestoreRetryDelay(attempt);
                            var cause = (error as ExplorerCreationTransientException)?.InnerException ?? error;
                            log.Write($"Workspace restore waiting; directory={entry.Directory}; attempt={attempt}; delayMs={delay.TotalMilliseconds:F0}; {cause.GetType().Name} HRESULT=0x{cause.HResult:X8}; {cause.Message}");
                            ShowRestoreWaiting(saved, i, attempt, error, delay);
                            await Task.Delay(delay, operation.Token);
                        }
                    }
                    if (stopped) break;
                }
                if (!IsDisposed && !closing)
                {
                    if (desired != null || first != null) SelectTab(desired ?? first!);
                    RefreshTabs(); CompleteRestoreBanner(restored, saved.Tabs.Count, failures, stopped);
                    status.Text = stopped
                        ? $"已停止恢复：已打开 {restored} / {saved.Tabs.Count} 个标签；其余可手动新建。"
                        : failures.Count == 0
                            ? $"恢复完成：已打开 {restored} 个标签。"
                            : $"恢复完成：已打开 {restored} 个；{failures.Count} 个未打开（详见提示条与日志）。";
                }
                log.Write($"Workspace restore finished; session={saved.Name}; restored={restored}; failed={failures.Count}; stopped={stopped}");
            }
            catch (OperationCanceledException)
            {
                stopped = true;
                if (!IsDisposed && !closing)
                {
                    CompleteRestoreBanner(restored, saved.Tabs.Count, failures, true);
                    status.Text = $"已停止恢复：已打开 {restored} / {saved.Tabs.Count} 个标签；其余可手动新建。";
                }
                log.Write($"Workspace restore stopped by user; restored={restored}; failed={failures.Count}");
            }
            finally
            {
                AbandonRestoreBanner();
                workspaceOperation = null; operation.Dispose(); if (!IsDisposed && !closing) SetCreationControls(creation != null);
            }
        }
        private async Task<WindowSession?> CreateRestoredTabAsync(WorkspaceTab entry, CancellationToken operationToken)
        {
            CreatedExplorer? result = null;
            try
            {
                using (var item = CancellationTokenSource.CreateLinkedTokenSource(operationToken))
                {
                    item.CancelAfter(15000);
                    try
                    {
                        result = await explorerTabs.CreateAsync(entry.Directory, null, controller.Sessions.Select(s => s.Identity.Handle).ToArray(), item.Token);
                    }
                    catch (Exception error) when (!(error is OperationCanceledException && operationToken.IsCancellationRequested) &&
                                                   !(error is ExplorerDirectoryUnavailableException))
                    {
                        // Any failure while asking the Shell for a window is retryable: the
                        // LocalServer may still be starting even when the error is generic.
                        throw new ExplorerCreationTransientException(error);
                    }
                    while (modalDepth > 0 || tabs.IsInteracting) await Task.Delay(50, item.Token); item.Token.ThrowIfCancellationRequested();
                }
                operationToken.ThrowIfCancellationRequested(); if (closing || IsDisposed || exitPreparing) return null;
                AttachCreated(result, SelectedMode); var session = controller.Active!;
                if (entry.Alias != null) controller.RenameTab(session, entry.Alias);
                return session;
            }
            finally { result?.Dispose(); }
        }
        // A stalled COM activation keeps the single-flight gate held until its worker returns.
        // Wait visibly instead of reporting an "already running" failure, so the batch can resume.
        private async Task WaitForShellIdleAsync(SavedWorkspace saved, int index, CancellationToken token)
        {
            if (!explorerTabs.IsBusy) return;
            log.Write("Workspace restore waiting for the previous Shell call to exit; no concurrent creation.");
            while (explorerTabs.IsBusy) { ShowRestoreBusy(saved, index); await Task.Delay(100, token); }
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
