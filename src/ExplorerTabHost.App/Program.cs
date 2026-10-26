using System;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Security.Principal;
using System.Text;
using System.Threading;
using System.Windows.Forms;
using ExplorerTabHost.Core;

namespace ExplorerTabHost.App
{
    internal static class Program
    {
        [STAThread]
        private static int Main(string[] args)
        {
            string stateDirectory = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "ExplorerTabHost");
            var log = new AppLog(Path.Combine(stateDirectory, "host.log"));
            if (args.Length > 0 && args[0] == "--diagnose")
            {
                try
                {
                    string output = args.Length > 1 ? Path.GetFullPath(args[1]) : Path.Combine(stateDirectory, "diagnostics.txt");
                    var text = new StringBuilder();
                    text.AppendLine("ExplorerTabHost read-only diagnostics " + DateTimeOffset.Now.ToString("O"));
                    text.AppendLine("OS " + Environment.OSVersion + "; x64=" + Environment.Is64BitProcess);
                    foreach (var window in NativeWindowApi.DiscoverExplorers())
                    {
                        text.AppendLine(window.ToString());
                        text.AppendLine(WindowDiagnostics.Capture(window, true));
                    }
                    Directory.CreateDirectory(Path.GetDirectoryName(output)!);
                    File.WriteAllText(output, text.ToString(), Encoding.UTF8);
                    return 0;
                }
                catch (Exception error) { log.Write("Diagnostics failed", error); return 1; }
            }
            if (args.Length > 0)
            {
                MessageBox.Show("用法：ExplorerTabHost.exe [--diagnose [输出文件]]", "ExplorerTabHost");
                return 1;
            }

            string user = WindowsIdentity.GetCurrent().User?.Value ?? Environment.UserName;
            using (var mutex = new Mutex(false, "Local\\ExplorerTabHost." + user))
            {
                bool acquired;
                try { acquired = mutex.WaitOne(0); }
                catch (AbandonedMutexException) { acquired = true; }
                if (!acquired)
                {
                    MessageBox.Show("已有 ExplorerTabHost 在运行。请先在该程序中分离窗口并退出。", "ExplorerTabHost");
                    return 1;
                }
                MainForm? form = null;
                bool messageLoopStarted = false;
                try
                {
                    // Match the first existing Explorer's context. Every attach still validates its own context.
                    NativeWindowApi.MatchThreadDpiToExplorer();
                    Application.EnableVisualStyles();
                    Application.SetCompatibleTextRenderingDefault(false);
                    Application.SetUnhandledExceptionMode(UnhandledExceptionMode.CatchException);
                    var journal = new RecoveryJournal(Path.Combine(stateDirectory, "recovery.json"));
                    if (!RecoverBeforeStartup(journal, log)) return 1;
                    IAppSettingsStore? preferences = new AppSettingsStore(Path.Combine(stateDirectory, "settings.json"));
                    var settings = new AppSettings();
                    try { settings = preferences.Read() ?? settings; }
                    catch (Exception error)
                    {
                        log.Write("Settings invalid/unreadable; preserved without automatic overwrite", error);
                        preferences = null;
                        MessageBox.Show("设置文件无法读取，将使用默认设置且本次不自动覆盖原文件。\n" +
                            "窗口恢复日志不受影响。请备份并检查 settings.json 后重启。\n\n" + error.Message,
                            "ExplorerTabHost 设置", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                    }
                    form = new MainForm(new HostController(CreateWindowApi(log), journal), log, settings, preferences,
                        new StaExplorerTabService(() => new ShellExplorerBackend(message => log.Write(message))));
                    IWorkspaceLibraryStore? workspaceStore = new WorkspaceLibraryStore(Path.Combine(stateDirectory, "workspace.json"));
                    var workspace = new WorkspaceLibrary();
                    try { workspace = workspaceStore.Read() ?? workspace; }
                    catch (Exception error)
                    {
                        log.Write("Workspace invalid/unreadable; original preserved, persistence disabled", error); workspaceStore = null;
                        MessageBox.Show("会话库无法读取，原文件保留且本次不自动覆盖。常用目录 / 会话保存暂不可用。\n请备份并检查 workspace.json 后重启。\n\n" + error.Message,
                            "ExplorerTabHost 会话库", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                    }
                    form.ConfigureLibrary(workspaceStore, workspace);
                    Application.ThreadException += (_, e) =>
                    {
                        log.Write("Unhandled UI exception", e.Exception);
                        form.ReportError("发生异常，程序未退出。请先分离所有窗口。", e.Exception);
                    };
                    messageLoopStarted = true;
                    Application.Run(form);
                    return 0;
                }
                catch (Exception error)
                {
                    log.Write(messageLoopStarted ? "Run failed" : "Startup failed", error);
                    // This is only a best effort, not a crash recovery guarantee.
                    form?.EmergencyDetach();
                    MessageBox.Show(error.Message + "\n日志：" + log.FilePath, messageLoopStarted ? "ExplorerTabHost 运行异常" : "ExplorerTabHost 启动失败", MessageBoxButtons.OK, MessageBoxIcon.Error);
                    return 1;
                }
                finally { mutex.ReleaseMutex(); }
            }
        }

        private static NativeWindowApi CreateWindowApi(AppLog log) => new NativeWindowApi(
            warning: message => log.Write("Presentation warning: " + message), trace: message => log.Write("Native: " + message));

        private static bool RecoverBeforeStartup(RecoveryJournal journal, AppLog log)
        {
            var previous = journal.Read();
            if (previous == null || previous.Windows.Count == 0) return true;
            if (RecoveryJournal.OwnerIsRunning(previous))
                throw new InvalidOperationException("恢复日志的原宿主仍在运行。为安全起见不启动第二个宿主。");
            var consent = MessageBox.Show(
                "检测到上次未完成的窗口恢复记录（" + previous.Windows.Count + " 个）。\n\n" +
                "是否尝试将仍存活的窗口恢复到接管前的位置和状态？\n" +
                "已经销毁的 Explorer 窗口无法救回。选择“否”将保留记录并退出，不会接管新窗口。\n\n" + journal.FilePath,
                "ExplorerTabHost 恢复", MessageBoxButtons.YesNo, MessageBoxIcon.Warning, MessageBoxDefaultButton.Button2);
            if (consent != DialogResult.Yes) return false;
            var report = journal.Recover(previous, CreateWindowApi(log));
            log.Write(report.Summary + " Missing=" + string.Join(" | ", report.Missing) + "; Errors=" + string.Join(" | ", report.Errors));
            string details = report.Summary;
            if (report.Missing.Count > 0)
                details += "\n\n以下原窗口已不存在：\n" + string.Join("\n", report.Missing) +
                    "\n\n嵌入后强杀宿主可能连带销毁 Explorer 窗口。本次没有重新打开目录，也不能恢复已销毁窗口的选择、滚动或导航历史。";
            if (report.Errors.Count > 0)
                details += "\n\n以下窗口恢复失败，记录已保留。请不要删除恢复日志：\n" + string.Join("\n", report.Errors);
            MessageBox.Show(details, "ExplorerTabHost 恢复结果", MessageBoxButtons.OK,
                report.Errors.Count > 0 ? MessageBoxIcon.Error : report.Missing.Count > 0 ? MessageBoxIcon.Warning : MessageBoxIcon.Information);
            return report.Errors.Count == 0;
        }
    }
}
