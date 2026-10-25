using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Text;

namespace ExplorerTabHost.Core
{
    public static class DiagnosticArchive
    {
        public static void Create(string output, string stateDirectory, string windowState)
        {
            // Only package known local diagnostics. No screenshots, file contents, recursive scans or uploads.
            // Never overwrite another release, recovery record, existing ZIP or arbitrary user file.
            string destination = Path.GetFullPath(output);
            string temporary = destination + "." + Guid.NewGuid().ToString("N") + ".tmp";
            var errors = new List<string>();
            try
            {
                using (var file = new FileStream(temporary, FileMode.CreateNew, FileAccess.Write, FileShare.None))
                using (var zip = new ZipArchive(file, ZipArchiveMode.Create))
                {
                    Text(zip, "window-state.txt", windowState);
                    foreach (string name in new[] { "host.log", "host.log.old", "recovery.json" })
                    {
                        string source = Path.Combine(stateDirectory, name);
                        if (!File.Exists(source)) continue;
                        try
                        {
                            using (var input = new FileStream(source, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete))
                            using (var entry = zip.CreateEntry(name).Open()) input.CopyTo(entry);
                        }
                        catch (Exception error) when (error is IOException || error is UnauthorizedAccessException)
                        { errors.Add(name + ": " + error.Message); }
                    }
                    Text(zip, "collection-info.txt", "ExplorerTabHost diagnostics " + DateTimeOffset.Now.ToString("O") +
                        "\nLocal diagnostics only; may contain folder/window titles and paths. No automatic upload.\n" +
                        "Collection problems: " + (errors.Count == 0 ? "none" : string.Join("\n", errors)));
                }
                File.Move(temporary, destination); // Fails rather than overwriting any existing destination.
            }
            finally { if (File.Exists(temporary)) File.Delete(temporary); }
        }
        private static void Text(ZipArchive zip, string name, string text)
        {
            using (var entry = zip.CreateEntry(name).Open())
            using (var writer = new StreamWriter(entry, new UTF8Encoding(false))) writer.Write(text);
        }
    }
}
