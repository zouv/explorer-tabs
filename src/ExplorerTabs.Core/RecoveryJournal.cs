using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Runtime.Serialization;
using System.Runtime.Serialization.Json;

namespace ExplorerTabs.Core
{
    [DataContract]
    public sealed class RecoveryDocument
    {
        [DataMember] public int Version { get; set; } = 1;
        [DataMember] public int HostProcessId { get; set; }
        [DataMember] public long HostStartedUtcTicks { get; set; }
        [DataMember] public List<WindowSnapshot> Windows { get; set; } = new List<WindowSnapshot>();
    }

    public sealed class RecoveryReport
    {
        public List<string> Restored { get; } = new List<string>();
        public List<string> Missing { get; } = new List<string>();
        public List<string> Errors { get; } = new List<string>();
        public string Summary => $"恢复完成：{Restored.Count} 个原窗口已恢复；{Missing.Count} 个原窗口已消失；{Errors.Count} 个恢复失败。";
    }

    public sealed class RecoveryJournal : ISnapshotStore
    {
        private readonly int ownerPid;
        private readonly long ownerStarted;
        public string FilePath { get; }

        public RecoveryJournal(string filePath)
        {
            FilePath = Path.GetFullPath(filePath);
            using (var process = Process.GetCurrentProcess())
            {
                ownerPid = process.Id;
                ownerStarted = process.StartTime.ToUniversalTime().Ticks;
            }
        }

        public void Save(IReadOnlyCollection<WindowSnapshot> snapshots)
        {
            var document = new RecoveryDocument
            {
                HostProcessId = ownerPid, HostStartedUtcTicks = ownerStarted,
                Windows = snapshots.ToList()
            };
            Write(document);
        }

        public RecoveryDocument? Read()
        {
            if (!File.Exists(FilePath)) return null;
            using (var stream = File.OpenRead(FilePath))
            {
                var document = (RecoveryDocument?)new DataContractJsonSerializer(typeof(RecoveryDocument)).ReadObject(stream);
                if (document == null || document.Version != 1 || document.Windows == null || document.Windows.Count > 100)
                    throw new InvalidDataException("恢复日志格式 / 版本无效。为安全起见没有接管窗口。");
                foreach (var snapshot in document.Windows)
                {
                    if (snapshot == null || snapshot.Identity == null || !NativeWindowApi.IsExplorer(snapshot.Identity) ||
                        snapshot.Identity.Handle == 0 || snapshot.Identity.ProcessId <= 0 || snapshot.Identity.ThreadId == 0 ||
                        snapshot.Identity.ProcessStartedUtcTicks <= 0 || snapshot.HostHandle == 0 ||
                        snapshot.Placement.Length != 44 || snapshot.Parent != 0 ||
                        snapshot.Owner != 0 || (snapshot.Style & 0x40000000L) != 0 ||
                        !Enum.IsDefined(typeof(HostingMode), snapshot.Mode))
                        throw new InvalidDataException("恢复日志含有不符合接管条件的窗口。");
                }
                return document;
            }
        }

        public static bool OwnerIsRunning(RecoveryDocument document)
        {
            try
            {
                using (var process = Process.GetProcessById(document.HostProcessId))
                    return process.StartTime.ToUniversalTime().Ticks == document.HostStartedUtcTicks && !process.HasExited;
            }
            catch (ArgumentException) { return false; }
            catch (InvalidOperationException) { return false; }
            catch (System.ComponentModel.Win32Exception) { return true; } // Unknown => do not touch live state.
        }

        public RecoveryReport Recover(RecoveryDocument document, IWindowApi windows)
        {
            if (OwnerIsRunning(document)) throw new InvalidOperationException("恢复日志的原宿主仍在运行，拒绝争夺窗口。");
            var pending = new List<WindowSnapshot>();
            var report = new RecoveryReport();
            foreach (var snapshot in document.Windows)
            {
                try
                {
                    if (!windows.Matches(snapshot.Identity))
                    {
                        report.Missing.Add(snapshot.Identity.Title);
                        continue; // Never manipulate a recycled HWND or imply it was resurrected.
                    }
                    windows.Restore(snapshot);
                    report.Restored.Add(snapshot.Identity.Title);
                }
                catch (Exception error)
                {
                    pending.Add(snapshot);
                    report.Errors.Add(snapshot.Identity.Title + "：" + error.Message);
                }
            }
            // Retain the ORIGINAL owner while retryable entries remain. The new app must not
            // turn a dead owner's journal into an apparently live one before recovery is done.
            document.Windows = pending;
            Write(document);
            return report;
        }

        private void Write(RecoveryDocument document)
        {
            Directory.CreateDirectory(Path.GetDirectoryName(FilePath)!);
            string temporary = FilePath + "." + Guid.NewGuid().ToString("N") + ".tmp";
            try
            {
                using (var stream = new FileStream(temporary, FileMode.CreateNew, FileAccess.Write, FileShare.None))
                {
                    new DataContractJsonSerializer(typeof(RecoveryDocument)).WriteObject(stream, document);
                    stream.Flush(true);
                }
                if (File.Exists(FilePath)) File.Replace(temporary, FilePath, null);
                else File.Move(temporary, FilePath);
            }
            finally { if (File.Exists(temporary)) File.Delete(temporary); }
        }
    }
}
