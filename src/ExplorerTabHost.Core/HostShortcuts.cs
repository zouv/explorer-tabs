using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Runtime.Serialization;

namespace ExplorerTabHost.Core
{
    public enum HostCommand { None, NewTab, CloseTab, SearchTabs, Commands, ReopenTab, DuplicateTab, NextTab, PreviousTab,
        NextPage, PreviousPage, Settings, RenameTab, SaveSession, Tab1, Tab2, Tab3, Tab4, Tab5, Tab6, Tab7, Tab8, Tab9 }
    [DataContract] public sealed class HostShortcut
    {
        [DataMember] public HostCommand Command { get; set; }
        [DataMember] public string Chord { get; set; } = "";
        public HostShortcut Copy() => new HostShortcut { Command = Command, Chord = Chord };
    }
    public static class HostShortcutCatalog
    {
        public static List<HostShortcut> Defaults()
        {
            var list = new List<HostShortcut>();
            void Add(HostCommand c, string k) => list.Add(new HostShortcut { Command = c, Chord = k });
            Add(HostCommand.NewTab, "Ctrl+T"); Add(HostCommand.CloseTab, "Ctrl+W");
            Add(HostCommand.SearchTabs, "Ctrl+P"); Add(HostCommand.Commands, "Ctrl+Shift+P");
            Add(HostCommand.ReopenTab, "Ctrl+Shift+T"); Add(HostCommand.DuplicateTab, "Ctrl+Shift+D");
            Add(HostCommand.NextTab, "Ctrl+Tab"); Add(HostCommand.PreviousTab, "Ctrl+Shift+Tab");
            Add(HostCommand.NextPage, "Ctrl+PageDown"); Add(HostCommand.PreviousPage, "Ctrl+PageUp");
            Add(HostCommand.Settings, "Ctrl+,"); Add(HostCommand.RenameTab, "Ctrl+Shift+R"); Add(HostCommand.SaveSession, "Ctrl+Shift+S");
            for (int i = 1; i <= 9; i++) Add((HostCommand)((int)HostCommand.Tab1 + i - 1), "Ctrl+" + i);
            return list;
        }
        public static string Name(HostCommand c)
        {
            switch (c)
            {
                case HostCommand.NewTab: return "新建标签"; case HostCommand.CloseTab: return "关闭当前 Explorer";
                case HostCommand.SearchTabs: return "搜索 / 列出标签"; case HostCommand.Commands: return "命令面板";
                case HostCommand.ReopenTab: return "重新打开刚关闭的目录"; case HostCommand.DuplicateTab: return "复制当前标签目录";
                case HostCommand.NextTab: return "下一个标签"; case HostCommand.PreviousTab: return "上一个标签";
                case HostCommand.NextPage: return "下一个标签（备用）"; case HostCommand.PreviousPage: return "上一个标签（备用）";
                case HostCommand.Settings: return "打开设置"; case HostCommand.RenameTab: return "重命名当前标签";
                case HostCommand.SaveSession: return "保存当前会话"; case HostCommand.Tab9: return "跳到最后一个标签";
                default: return "跳到第 " + ((int)c - (int)HostCommand.Tab1 + 1) + " 个标签";
            }
        }
        public static readonly string[] Choices = new[] { "" }.Concat(Defaults().Select(s => s.Chord))
            .Concat(new[] { "Ctrl+Shift+W", "Ctrl+Alt+W", "Ctrl+Alt+P", "Ctrl+Alt+T", "Ctrl+Alt+D", "Ctrl+S", "Ctrl+Alt+S", "Ctrl+Shift+,", "Ctrl+Alt+R" }).Distinct().ToArray();
        public static void Validate(IReadOnlyList<HostShortcut> list)
        {
            if (list == null || list.Count != Defaults().Count) throw new InvalidDataException("快捷键配置缺少操作。");
            var commands = new HashSet<HostCommand>(); var chords = new HashSet<string>(StringComparer.Ordinal);
            foreach (var item in list)
            {
                if (item == null || item.Command == HostCommand.None || !Enum.IsDefined(typeof(HostCommand), item.Command) || !commands.Add(item.Command) ||
                    item.Chord == null || !Choices.Contains(item.Chord) || (item.Chord != "" && !chords.Add(item.Chord)))
                    throw new InvalidDataException("快捷键无效或重复，请为每个操作选择不同组合（或禁用）。");
            }
        }
        public static int Key(string chord)
        {
            string key = chord.Split('+').Last();
            if (key.Length == 1) return key == "," ? 0xBC : char.ToUpperInvariant(key[0]);
            switch (key) { case "Tab": return 9; case "PageDown": return 0x22; case "PageUp": return 0x21; case "F2": return 0x71; default: return 0; }
        }
        public static int Modifiers(string chord) => (chord.Contains("Ctrl+") ? 1 : 0) | (chord.Contains("Shift+") ? 2 : 0) | (chord.Contains("Alt+") ? 4 : 0);
        public static bool NeedsCreate(HostCommand c) => c == HostCommand.NewTab || c == HostCommand.ReopenTab || c == HostCommand.DuplicateTab;
    }
    public readonly struct HostKeyDecision
    {
        public bool Suppress { get; }
        public HostCommand Command { get; }
        public HostKeyDecision(bool suppress, HostCommand command = HostCommand.None) { Suppress = suppress; Command = command; }
    }
    /// <summary>Physical key-pair state for ONLY the supported action keys; no ordinary text/clipboard key capture.</summary>
    public sealed class HostShortcutPolicy
    {
        private readonly HashSet<int> down = new HashSet<int>();
        private readonly HashSet<int> consumed = new HashSet<int>();
        public static readonly int[] SupportedKeys = HostShortcutCatalog.Choices.Select(HostShortcutCatalog.Key).Where(k => k != 0).Distinct().ToArray();
        public HostShortcutPolicy(IEnumerable<int>? initiallyDown = null) { if (initiallyDown != null) down.UnionWith(initiallyDown); }
        public HostKeyDecision Handle(int key, bool isDown, bool injected, int modifiers, bool eligible, bool canCreate, bool canSwitch, bool hasActive, IReadOnlyList<HostShortcut> bindings)
        {
            if (injected || !SupportedKeys.Contains(key)) return new HostKeyDecision(false);
            if (!isDown) { bool suppress = consumed.Remove(key); down.Remove(key); return new HostKeyDecision(suppress); }
            if (!down.Add(key)) return new HostKeyDecision(consumed.Contains(key));
            if (!eligible) return new HostKeyDecision(false);
            var binding = bindings.FirstOrDefault(b => b.Chord != "" && HostShortcutCatalog.Key(b.Chord) == key && HostShortcutCatalog.Modifiers(b.Chord) == modifiers);
            if (binding == null || (HostShortcutCatalog.NeedsCreate(binding.Command) && !canCreate)) return new HostKeyDecision(false);
            var c = binding.Command;
            if ((c == HostCommand.NextTab || c == HostCommand.PreviousTab || c == HostCommand.NextPage || c == HostCommand.PreviousPage) && !canSwitch) return new HostKeyDecision(false);
            if ((c == HostCommand.CloseTab || c == HostCommand.RenameTab || c == HostCommand.DuplicateTab || c >= HostCommand.Tab1) && !hasActive) return new HostKeyDecision(false);
            consumed.Add(key); return new HostKeyDecision(true, c);
        }
    }
}
