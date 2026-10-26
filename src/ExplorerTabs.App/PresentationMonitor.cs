using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Text;
using System.Windows.Forms;
using ExplorerTabs.Core;

namespace ExplorerTabs.App
{
    /// <summary>UI-thread-only, bounded and read-only. Never retries AddTab, redraws or focuses in the background.</summary>
    internal sealed class PresentationMonitor : IDisposable
    {
        private readonly AppLog log;
        private readonly Func<IReadOnlyList<WindowIdentity>> discover;
        private readonly Func<WindowIdentity, bool, string> capture;
        private readonly Timer timer = new Timer { Interval = 200 };
        private readonly List<Entry> entries = new List<Entry>();
        private readonly Stopwatch clock = Stopwatch.StartNew();
        public PresentationMonitor(AppLog log, Func<IReadOnlyList<WindowIdentity>>? discover = null,
            Func<WindowIdentity, bool, string>? capture = null)
        {
            this.log = log;
            this.discover = discover ?? NativeWindowApi.DiscoverExplorers;
            this.capture = capture ?? WindowDiagnostics.Capture;
            timer.Tick += (_, __) => Poll();
        }
        public void Start() => timer.Start();
        public void Track(WindowIdentity identity, string operation)
        {
            entries.RemoveAll(e => e.Identity.Handle == identity.Handle);
            entries.Add(new Entry(identity, operation, clock.ElapsedMilliseconds));
            if (entries.Count > 100) entries.RemoveAt(0);
        }
        private void Poll()
        {
            foreach (var entry in entries.ToArray())
            {
                long age = clock.ElapsedMilliseconds - entry.Start;
                int due = age >= 3000 ? 3 : age >= 1000 ? 2 : age >= 200 ? 1 : 0;
                if (due <= entry.Sample) continue;
                entry.Sample = due;
                log.Write($"Presentation observed operation={entry.Operation} age-ms={age} sample={due} " + Capture(entry.Identity, due == 3));
            }
        }
        public string Snapshot(string reason, bool discoverStandalone = false)
        {
            var text = new StringBuilder("ExplorerTabs " + typeof(PresentationMonitor).Assembly.GetName().Version +
                "\n" + DateTimeOffset.Now.ToString("O") + "\nOS=" + Environment.OSVersion + " x64=" + Environment.Is64BitProcess +
                "\nreason=" + reason + "\nTaskbar membership and actual rendered pixels are not verified by these queries.\n");
            foreach (var entry in entries)
                text.AppendLine("operation=" + entry.Operation + "\n" + Capture(entry.Identity, true));
            // Explicit user exports also capture still-visible standalone Explorer windows. This
            // works after reopening an empty host following an exit failure, without reattaching.
            // Never run discovery in background samples or while closing.
            if (discoverStandalone)
            {
                try
                {
                    var tracked = new HashSet<long>(entries.Select(e => e.Identity.Handle));
                    foreach (var identity in discover().Where(i => !tracked.Contains(i.Handle)).Take(100))
                        text.AppendLine("operation=standalone-read-only title=" + identity.Title + "\n" + Capture(identity, true));
                }
                catch (Exception error) { text.AppendLine("standalone-discovery=unknown error=" + error.Message); }
            }
            log.Write("Presentation snapshot: " + text);
            return text.ToString();
        }
        private string Capture(WindowIdentity identity, bool children)
        {
            try { return capture(identity, children); }
            catch (Exception error) { return $"HWND=0x{identity.Handle:X} identity/query=unknown error={error.Message}"; }
        }
        public void Dispose() => timer.Dispose();
        private sealed class Entry
        {
            public WindowIdentity Identity { get; }
            public string Operation { get; }
            public long Start { get; }
            public int Sample { get; set; }
            public Entry(WindowIdentity identity, string operation, long start)
            { Identity = identity; Operation = operation; Start = start; }
        }
    }
}
