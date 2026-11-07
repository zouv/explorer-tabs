using System;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Security.Principal;
using System.Text;
using System.Threading;
using System.Windows.Forms;
using ExplorerTabs.Core;

namespace ExplorerTabs.App
{
    internal static class Program
    {
        [STAThread]
        private static int Main(string[] args)
        {
            string stateDirectory = ProductIdentity.GetStateDirectory(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData));
            var log = new AppLog(Path.Combine(stateDirectory, "host.log"));
            if (args.Length > 0 && args[0] == "--diagnose")
            {
                try
                {
                    string output = args.Length > 1 ? Path.GetFullPath(args[1]) : Path.Combine(stateDirectory, "diagnostics.txt");
                    var text = new StringBuilder();
                    text.AppendLine("ExplorerTabs read-only diagnostics " + DateTimeOffset.Now.ToString("O"));
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
                MessageBox.Show("用法：ExplorerTabs.exe [--diagnose [输出文件]]", "ExplorerTabs");
                return 1;
            }

            string user = WindowsIdentity.GetCurrent().User?.Value ?? Environment.UserName;
            using (var mutex = new Mutex(false, ProductIdentity.GetMutexName(user)))
            {
                bool acquired;
                try { acquired = mutex.WaitOne(0); }
                catch (AbandonedMutexException) { acquired = true; }
                if (!acquired)
                {
                    MessageBox.Show("已有宿主（当前版或旧版）在运行。请先在该程序中分离窗口并退出。", "ExplorerTabs");
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
                    if (!RecoverBeforeStartup(journal, log, out var startupReport)) return 1;
                    IAppSettingsStore? preferences = new AppSettingsStore(Path.Combine(stateDirectory, "settings.json"));
                    var settings = new AppSettings();
                    try { settings = preferences.Read() ?? settings; }
                    catch (Exception error)
                    {
                        log.Write("Settings invalid/unreadable; preserved without automatic overwrite", error);
                        preferences = null;
                        MessageBox.Show("设置文件无法读取，将使用默认设置且本次不自动覆盖原文件。\n" +
                            "窗口恢复日志不受影响。请备份并检查 settings.json 后重启。\n\n" + error.Message,
                            "ExplorerTabs 设置", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                    }
                    form = new MainForm(new HostController(CreateWindowApi(log), journal), log, settings, preferences,
                        new StaExplorerTabService(() => new ShellExplorerBackend(message => log.Write(message))));
                    IWorkspaceLibraryStore? workspaceStore = new WorkspaceLibraryStore(Path.Combine(stateDirectory, "workspace.json"));
                    var workspace = new WorkspaceLibrary();
                    try { workspace = workspaceStore.Read() ?? workspace; }
                    catch (Exception error)
                    {
                        log.Write("Workspace invalid/unreadable; original preserved, persistence disabled", error); workspaceStore = null;
                        MessageBox.Show("会话库无法读取，原文件保留且本次不自动覆盖。收藏 / 标签组 / 会话保存暂不可用。\n请备份并检查 workspace.json 后重启。\n\n" + error.Message,
                            "ExplorerTabs 会话库", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                    }
                    if (startupReport != null) form.ConfigureStartupRecovery(startupReport.Missing.Count);
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
                    MessageBox.Show(error.Message + "\n日志：" + log.FilePath, messageLoopStarted ? "ExplorerTabs 运行异常" : "ExplorerTabs 启动失败", MessageBoxButtons.OK, MessageBoxIcon.Error);
                    return 1;
                }
                finally { mutex.ReleaseMutex(); }
            }
        }

        private static NativeWindowApi CreateWindowApi(AppLog log) => new NativeWindowApi(
            warning: message => log.Write("Presentation warning: " + message), trace: message => log.Write("Native: " + message));

        private static bool RecoverBeforeStartup(RecoveryJournal journal, AppLog log, out RecoveryReport? startupReport)
        {
            return ExecuteStartupRecovery(journal, CreateWindowApi(log),
                previous => MessageBox.Show(
                    "检测到上次未完成的窗口恢复记录（" + previous.Windows.Count + " 个）。\n\n" +
                    "是否尝试将仍存活且身份匹配的窗口恢复到接管前的位置和状态？\n" +
                    "已消失或身份不匹配的原窗口不会被恢复。选择“否”将保留记录并退出，不会接管新窗口。\n\n" + journal.FilePath,
                    "ExplorerTabs 恢复", MessageBoxButtons.YesNo, MessageBoxIcon.Warning, MessageBoxDefaultButton.Button2) == DialogResult.Yes,
                report => MessageBox.Show(StartupRecoveryDetails(report), "ExplorerTabs 恢复结果", MessageBoxButtons.OK,
                    report.Errors.Count > 0 ? MessageBoxIcon.Error : report.Missing.Count > 0 ? MessageBoxIcon.Warning : MessageBoxIcon.Information),
                message => log.Write(message), out startupReport);
        }

        internal static bool ExecuteStartupRecovery(RecoveryJournal journal, IWindowApi windows,
            Func<RecoveryDocument, bool> confirm, Action<RecoveryReport> showReport, Action<string> log,
            out RecoveryReport? startupReport)
        {
            startupReport = null;
            var previous = journal.Read();
            if (previous == null || previous.Windows.Count == 0) return true;
            if (RecoveryJournal.OwnerIsRunning(previous))
                throw new InvalidOperationException("恢复日志的原宿主仍在运行。为安全起见不启动第二个宿主。");
            if (journal.TryClearAllMissing(previous, windows, out var fastReport))
            {
                startupReport = fastReport;
                log("startup.recovery.all-missing; " + fastReport!.Summary + " Missing=" + string.Join(" | ", fastReport.Missing));
                return true; // The main UI presents the notice; no confirmation or result MessageBox.
            }
            if (!confirm(previous)) return false;
            var report = journal.Recover(previous, windows);
            log(report.Summary + " Missing=" + string.Join(" | ", report.Missing) + "; Errors=" + string.Join(" | ", report.Errors));
            showReport(report);
            return report.Errors.Count == 0;
        }

        internal static string StartupRecoveryDetails(RecoveryReport report)
        {
            string details = report.Summary;
            if (report.Missing.Count > 0)
                details += "\n\n以下原窗口已失效（已消失或身份不匹配）：\n" + string.Join("\n", report.Missing) +
                    "\n\n本次没有重新打开目录，也不能恢复原窗口的选择、滚动或导航历史。" +
                    "如需继续，请在主界面的“会话与恢复”中显式重新打开已保存的目录；目录快照不等于原窗口状态。";
            if (report.Errors.Count > 0)
                details += "\n\n以下窗口恢复失败，记录已保留。请不要删除恢复日志：\n" + string.Join("\n", report.Errors);
            return details;
        }
    }
}
