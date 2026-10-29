using System;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;

namespace ExplorerTabs.Core
{
    public static class FolderTarget
    {
        /// <summary>Canonical virtual target for the Shell "This PC" folder.</summary>
        public const string ThisPc = "shell:MyComputerFolder";
        private const string ThisPcClsid = "::{20D04FE0-3AEA-1069-A2D8-08002B30309D}";
        public static bool IsThisPc(string? path)
        {
            if (path == null || string.IsNullOrWhiteSpace(path)) return false;
            path = path.Trim();
            return string.Equals(path, ThisPc, StringComparison.OrdinalIgnoreCase) ||
                string.Equals(path, ThisPcClsid, StringComparison.OrdinalIgnoreCase);
        }
        public static string Display(string path) => IsThisPc(path) ? "此电脑" : path;
        public static string Normalize(string path)
        {
            if (string.IsNullOrWhiteSpace(path)) throw new ArgumentException("请输入绝对文件夹路径。");
            if (path.Any(char.IsControl)) throw new ArgumentException("文件夹路径不能包含控制字符。");
            path = path.Trim();
            if (IsThisPc(path)) return ThisPc;
            if (path.Length > 4096 || path.IndexOfAny(new[] { '"', '<', '>', '|', '*', '?' }) >= 0 ||
                path.StartsWith(@"\\?\", StringComparison.Ordinal) || path.StartsWith(@"\\.\", StringComparison.Ordinal) ||
                !(Regex.IsMatch(path, @"^[A-Za-z]:[\\/]") || Regex.IsMatch(path, @"^\\\\[^\\]+\\[^\\]+")))
                throw new ArgumentException("仅支持本地盘符、UNC 的绝对文件夹路径，或“此电脑”；不接受命令、网址或其他 Shell 虚拟目录。");
            string tail = path.Length >= 2 && path[1] == ':' ? path.Substring(2) : path;
            if (tail.Contains(":")) throw new ArgumentException("路径不能包含文件流或其他冒号参数。");
            if (path.StartsWith(@"\\", StringComparison.Ordinal))
            {
                var parts = path.Substring(2).Split('\\');
                if (parts.Length < 2 || string.IsNullOrWhiteSpace(parts[0]) || string.IsNullOrWhiteSpace(parts[1]) ||
                    parts[0] == "." || parts[0] == ".." || parts[1] == "." || parts[1] == "..")
                    throw new ArgumentException("UNC 路径必须包含服务器和共享名称。");
            }
            string full = Path.GetFullPath(path);
            string root = Path.GetPathRoot(full);
            return full.Length > root.Length ? full.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar) : full;
        }
        public static string FromLocationUrl(string url)
        {
            if (!string.IsNullOrWhiteSpace(url) && IsThisPc(url)) return ThisPc;
            if (!Uri.TryCreate(url, UriKind.Absolute, out var uri) || !uri.IsFile ||
                !string.IsNullOrEmpty(uri.Query) || !string.IsNullOrEmpty(uri.Fragment))
                throw new InvalidOperationException("当前标签不是普通文件夹，暂不支持复制“此电脑”以外的搜索结果等虚拟位置。请使用新建标签。");
            return Normalize(uri.LocalPath);
        }
        public static bool Same(string first, string second) =>
            string.Equals(Normalize(first), Normalize(second), StringComparison.OrdinalIgnoreCase);
    }
}
