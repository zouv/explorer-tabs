using System;
using System.Collections.Generic;
using System.Drawing;
using System.IO;
using System.Linq;
using System.Runtime.Serialization;
using System.Runtime.Serialization.Json;

namespace ExplorerTabHost.Core
{
    [DataContract]
    public sealed class AppSettings
    {
        [DataMember] public int Version { get; set; } = 1;
        [DataMember] public HostingMode Mode { get; set; } = HostingMode.Embedded;
        [DataMember] public bool ShortcutsEnabled { get; set; } = true;
        [DataMember] public string NewTabDirectory { get; set; } = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
        [DataMember] public NativeRect Bounds { get; set; }
        [DataMember] public bool Maximized { get; set; }
        [DataMember] public bool WrapTabs { get; set; }
        [DataMember] public int MaximumTabRows { get; set; } = 3;
        [DataMember] public bool RememberSession { get; set; } = true;
        [DataMember] public List<HostShortcut> KeyBindings { get; set; } = HostShortcutCatalog.Defaults();
        [OnDeserializing] private void Defaults(StreamingContext context)
        { Version = 1; Mode = HostingMode.Embedded; ShortcutsEnabled = true; NewTabDirectory = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile); MaximumTabRows = 3; RememberSession = true; KeyBindings = HostShortcutCatalog.Defaults(); }
        public AppSettings Copy() => new AppSettings { Version = Version, Mode = Mode, ShortcutsEnabled = ShortcutsEnabled,
            NewTabDirectory = NewTabDirectory, Bounds = Bounds, Maximized = Maximized, WrapTabs = WrapTabs, MaximumTabRows = MaximumTabRows,
            RememberSession = RememberSession, KeyBindings = KeyBindings.Select(k => k.Copy()).ToList() };
    }
    public interface IAppSettingsStore
    {
        AppSettings? Read();
        void Save(AppSettings settings);
    }
    public sealed class AppSettingsStore : IAppSettingsStore
    {
        public string FilePath { get; }
        public AppSettingsStore(string path) { FilePath = Path.GetFullPath(path); }
        public AppSettings? Read()
        {
            if (!File.Exists(FilePath)) return null;
            using (var stream = File.OpenRead(FilePath))
            {
                if (stream.Length > 32768) throw new InvalidDataException("设置文件过大，未读取或覆盖。");
                var settings = (AppSettings?)new DataContractJsonSerializer(typeof(AppSettings)).ReadObject(stream);
                if (settings == null) throw new InvalidDataException("设置文件为空。");
                Validate(settings); return settings;
            }
        }
        public void Save(AppSettings settings)
        {
            Validate(settings);
            Directory.CreateDirectory(Path.GetDirectoryName(FilePath)!);
            string temporary = FilePath + "." + Guid.NewGuid().ToString("N") + ".tmp";
            try
            {
                using (var stream = new FileStream(temporary, FileMode.CreateNew, FileAccess.Write, FileShare.None))
                { new DataContractJsonSerializer(typeof(AppSettings)).WriteObject(stream, settings); stream.Flush(true); }
                if (File.Exists(FilePath)) File.Replace(temporary, FilePath, null); else File.Move(temporary, FilePath);
            }
            finally { if (File.Exists(temporary)) File.Delete(temporary); }
        }
        private static void Validate(AppSettings settings)
        {
            if (settings.Version != 1 || !Enum.IsDefined(typeof(HostingMode), settings.Mode))
                throw new InvalidDataException("设置文件版本或挂载模式无效。");
            FolderTarget.Normalize(settings.NewTabDirectory);
            if (settings.MaximumTabRows < 2 || settings.MaximumTabRows > 8) throw new InvalidDataException("多行标签上限必须为 2–8 行。");
            HostShortcutCatalog.Validate(settings.KeyBindings);
        }
    }
    public static class SettingsLayoutPolicy
    {
        public static Rectangle Restore(NativeRect saved, IReadOnlyList<Rectangle> workingAreas, Rectangle fallback)
        {
            if (workingAreas.Count == 0) return fallback;
            long w = (long)saved.Right - saved.Left, h = (long)saved.Bottom - saved.Top;
            Rectangle requested = w >= 720 && w <= 16000 && h >= 500 && h <= 16000 &&
                Math.Abs((long)saved.Left) < 1000000 && Math.Abs((long)saved.Top) < 1000000
                ? new Rectangle(saved.Left, saved.Top, (int)w, (int)h) : fallback;
            Rectangle area = workingAreas.OrderByDescending(a =>
            { var intersection = Rectangle.Intersect(a, requested); return (long)Math.Max(0, intersection.Width) * Math.Max(0, intersection.Height); }).First();
            int width = Math.Min(requested.Width, area.Width), height = Math.Min(requested.Height, area.Height);
            return new Rectangle(Math.Max(area.Left, Math.Min(requested.Left, area.Right - width)),
                Math.Max(area.Top, Math.Min(requested.Top, area.Bottom - height)), width, height);
        }
        public static NativeRect Save(Rectangle bounds) => new NativeRect
        { Left = bounds.Left, Top = bounds.Top, Right = bounds.Right, Bottom = bounds.Bottom };
    }
}
