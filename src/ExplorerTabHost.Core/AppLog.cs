using System;
using System.IO;
using System.Text;

namespace ExplorerTabHost.Core
{
    public sealed class AppLog
    {
        private readonly object gate = new object();
        public string FilePath { get; }
        public AppLog(string path) { FilePath = path; }
        public void Write(string message, Exception? error = null)
        {
            try
            {
                lock (gate)
                {
                    Directory.CreateDirectory(Path.GetDirectoryName(FilePath)!);
                    if (File.Exists(FilePath) && new FileInfo(FilePath).Length > 2 * 1024 * 1024)
                    {
                        string previous = FilePath + ".old";
                        if (File.Exists(previous)) File.Delete(previous);
                        File.Move(FilePath, previous);
                    }
                    File.AppendAllText(FilePath, DateTimeOffset.Now.ToString("O") + " " + message.Replace("\r", " ").Replace("\n", " ") +
                        (error == null ? "" : Environment.NewLine + error) + Environment.NewLine, Encoding.UTF8);
                }
            }
            catch (IOException) { }
            catch (UnauthorizedAccessException) { }
        }
    }
}
