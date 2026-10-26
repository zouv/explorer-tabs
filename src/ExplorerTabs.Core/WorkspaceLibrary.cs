using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Runtime.Serialization;
using System.Runtime.Serialization.Json;

namespace ExplorerTabs.Core
{
    [DataContract] public sealed class WorkspaceTab
    {
        [DataMember] public string Directory { get; set; } = "";
        [DataMember] public string? Alias { get; set; }
        public WorkspaceTab Copy() => new WorkspaceTab { Directory = Directory, Alias = Alias };
    }
    [DataContract] public sealed class SavedWorkspace
    {
        [DataMember] public string Id { get; set; } = Guid.NewGuid().ToString("N");
        [DataMember] public string Name { get; set; } = "上次会话";
        [DataMember] public List<WorkspaceTab> Tabs { get; set; } = new List<WorkspaceTab>();
        [DataMember] public int ActiveIndex { get; set; }
        public SavedWorkspace Copy() => new SavedWorkspace { Id = Id, Name = Name, ActiveIndex = ActiveIndex, Tabs = Tabs.Select(t => t.Copy()).ToList() };
    }
    [DataContract] public sealed class FolderBookmark
    {
        [DataMember] public string Id { get; set; } = Guid.NewGuid().ToString("N");
        [DataMember] public string Name { get; set; } = "";
        [DataMember] public string Directory { get; set; } = "";
        public FolderBookmark Copy() => new FolderBookmark { Id = Id, Name = Name, Directory = Directory };
    }
    [DataContract] public sealed class WorkspaceLibrary
    {
        [DataMember] public int Version { get; set; } = 1;
        [DataMember] public SavedWorkspace? LastSession { get; set; }
        [DataMember] public List<FolderBookmark> Favorites { get; set; } = new List<FolderBookmark>();
        [DataMember] public List<SavedWorkspace> Groups { get; set; } = new List<SavedWorkspace>();
        public WorkspaceLibrary Copy() => new WorkspaceLibrary { Version = Version, LastSession = LastSession?.Copy(),
            Favorites = Favorites.Select(f => f.Copy()).ToList(), Groups = Groups.Select(g => g.Copy()).ToList() };
        public static bool SameSession(SavedWorkspace? left, SavedWorkspace? right)
        {
            if (left == null || right == null) return left == right;
            return left.ActiveIndex == right.ActiveIndex && left.Tabs.Count == right.Tabs.Count &&
                left.Tabs.Zip(right.Tabs, (a, b) => FolderTarget.Same(a.Directory, b.Directory) && a.Alias == b.Alias).All(equal => equal);
        }
        public static void Validate(WorkspaceLibrary library)
        {
            if (library.Version != 1 || library.Favorites == null || library.Groups == null || library.Favorites.Count > 100 || library.Groups.Count > 50)
                throw new InvalidDataException("会话库版本或容量无效，原文件保留。");
            var ids = new HashSet<string>(StringComparer.Ordinal);
            foreach (var favorite in library.Favorites)
            {
                if (favorite == null || !ids.Add(favorite.Id) || !Guid.TryParseExact(favorite.Id, "N", out _)) throw new InvalidDataException("常用目录身份无效或重复。");
                TabManagementPolicy.NormalizeTitle(favorite.Name); FolderTarget.Normalize(favorite.Directory);
            }
            ids.Clear(); var names = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (var group in library.Groups)
            {
                ValidateWorkspace(group);
                if (!ids.Add(group.Id) || !names.Add(group.Name)) throw new InvalidDataException("标签组身份或名称重复。");
            }
            if (library.LastSession != null) ValidateWorkspace(library.LastSession);
        }
        private static void ValidateWorkspace(SavedWorkspace workspace)
        {
            if (workspace == null || !Guid.TryParseExact(workspace.Id, "N", out _) || workspace.Tabs == null || workspace.Tabs.Count == 0 || workspace.Tabs.Count > 100 ||
                workspace.ActiveIndex < 0 || workspace.ActiveIndex >= workspace.Tabs.Count) throw new InvalidDataException("会话标签或激活索引无效。");
            TabManagementPolicy.NormalizeTitle(workspace.Name);
            foreach (var tab in workspace.Tabs)
            {
                if (tab == null) throw new InvalidDataException("空标签记录。");
                FolderTarget.Normalize(tab.Directory); if (tab.Alias != null) TabManagementPolicy.NormalizeTitle(tab.Alias);
            }
        }
    }
    public interface IWorkspaceLibraryStore { WorkspaceLibrary? Read(); void Save(WorkspaceLibrary library); }
    public sealed class WorkspaceLibraryStore : IWorkspaceLibraryStore
    {
        public string FilePath { get; }
        public WorkspaceLibraryStore(string file) { FilePath = Path.GetFullPath(file); }
        public WorkspaceLibrary? Read()
        {
            if (!File.Exists(FilePath)) return null;
            using (var stream = File.OpenRead(FilePath))
            {
                if (stream.Length > 1024 * 1024) throw new InvalidDataException("会话库超过 1 MB，未读取或覆盖。");
                var result = (WorkspaceLibrary?)new DataContractJsonSerializer(typeof(WorkspaceLibrary)).ReadObject(stream);
                if (result == null) throw new InvalidDataException("会话库为空。");
                WorkspaceLibrary.Validate(result); return result;
            }
        }
        public void Save(WorkspaceLibrary library)
        {
            WorkspaceLibrary.Validate(library);
            System.IO.Directory.CreateDirectory(Path.GetDirectoryName(FilePath)!);
            string temp = FilePath + "." + Guid.NewGuid().ToString("N") + ".tmp";
            try
            {
                using (var stream = new FileStream(temp, FileMode.CreateNew, FileAccess.Write, FileShare.None))
                { new DataContractJsonSerializer(typeof(WorkspaceLibrary)).WriteObject(stream, library);
                    if (stream.Length > 1024 * 1024) throw new InvalidDataException("会话库超过 1 MB，旧文件保留。");
                    stream.Flush(true); }
                if (File.Exists(FilePath)) File.Replace(temp, FilePath, null); else File.Move(temp, FilePath);
            }
            finally { if (File.Exists(temp)) File.Delete(temp); }
        }
    }
    public static class TabSearchPolicy
    {
        public static bool Matches(string title, string directory, string query) => query.Split(new[] { ' ', '\t' }, StringSplitOptions.RemoveEmptyEntries)
            .All(word => title.IndexOf(word, StringComparison.OrdinalIgnoreCase) >= 0 || directory.IndexOf(word, StringComparison.OrdinalIgnoreCase) >= 0);
    }
    public static class TabLayoutPolicy
    {
        public static int Columns(int width, int itemWidth) => Math.Max(1, width / Math.Max(1, itemWidth));
        public static int Rows(int count, int width, int itemWidth) => Math.Max(1, (Math.Max(0, count) + Columns(width, itemWidth) - 1) / Columns(width, itemWidth));
    }
}
