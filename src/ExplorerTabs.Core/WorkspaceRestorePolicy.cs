using System;
using System.Runtime.InteropServices;

namespace ExplorerTabs.Core
{
    /// <summary>The Shell creation call failed before a window could be handed back. The
    /// reported error can be a generic COM failure even while the LocalServer is still
    /// starting, so restore retries it instead of ending the batch.</summary>
    public sealed class ExplorerCreationTransientException : Exception
    {
        public ExplorerCreationTransientException(Exception inner) : base(inner.Message, inner) { }
    }
    /// <summary>
    /// Classifies failures while reopening a saved session. The saved session stores only
    /// directory strings, but every restored tab needs a freshly created ShellBrowserWindow.
    /// Right after sign-in the Shell COM local server may still be starting; those failures
    /// are transient and worth waiting for instead of reporting the session as broken.
    /// </summary>
    public static class WorkspaceRestorePolicy
    {
        // Shell activation errors observed right after sign-in / reboot: the ShellBrowserWindow
        // local server can be missing, starting, or still initializing. Retry these.
        private static readonly int[] TransientShellHResults =
        {
            unchecked((int)0x80004005), // E_FAIL
            unchecked((int)0x80080005), // CO_E_SERVER_EXEC_FAILURE
            unchecked((int)0x80040154), // REGDB_E_CLASSNOTREG
            unchecked((int)0x80010105), // RPC_E_SERVERFAULT
            unchecked((int)0x80010100), // RPC_E_SYS_CALL_FAILED
            unchecked((int)0x80010001), // RPC_E_CALL_REJECTED
            unchecked((int)0x80010108), // RPC_E_DISCONNECTED
            unchecked((int)0x800706BA), // RPC_S_SERVER_UNAVAILABLE
            unchecked((int)0x800706BE), // RPC_S_CALL_FAILED
            unchecked((int)0x800706BF), // RPC_S_CALL_FAILED_DNE
        };

        public static bool IsRetryable(Exception? error)
        {
            if (error == null) return false;
            if (error is ExplorerCreationTransientException) return true;
            // The caller filters user cancellation before classifying; a remaining
            // OperationCanceledException is the per-item 15 s Shell timeout.
            if (error is OperationCanceledException) return true;
            return error is COMException com && Array.IndexOf(TransientShellHResults, com.HResult) >= 0;
        }

        public static TimeSpan RetryDelay(int attempt)
        {
            if (attempt < 1) attempt = 1;
            return TimeSpan.FromSeconds(attempt >= 4 ? 5 : attempt);
        }

        public static string Describe(Exception error)
        {
            if (error is ExplorerCreationTransientException transient) return Describe(transient.InnerException!);
            // Callers prefix the saved directory themselves; keep this reason short.
            if (error is ExplorerDirectoryUnavailableException) return "文件夹不存在或无法访问";
            if (error is COMException com)
                return IsRetryable(error)
                    ? $"Shell 尚未就绪（HRESULT 0x{com.HResult:X8}）"
                    : $"Shell 调用失败（HRESULT 0x{com.HResult:X8}）";
            if (error is OperationCanceledException) return "Shell 响应超时";
            return error.Message;
        }
    }
}
