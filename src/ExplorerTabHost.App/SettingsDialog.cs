using System;
using System.Drawing;
using System.Windows.Forms;
using ExplorerTabHost.Core;

namespace ExplorerTabHost.App
{
    internal sealed class SettingsDialog : Form
    {
        private readonly ComboBox mode = new ComboBox { DropDownStyle = ComboBoxStyle.DropDownList, Dock = DockStyle.Fill };
        private readonly CheckBox shortcuts = new CheckBox { Text = "启用 Ctrl+Tab / Ctrl+Shift+Tab / Ctrl+T", AutoSize = true };
        private readonly TextBox directory = new TextBox { Dock = DockStyle.Fill, MaxLength = 4096 };
        private readonly Label error = new Label { Dock = DockStyle.Fill, ForeColor = Color.Firebrick };
        private readonly AppSettings original;
        public AppSettings Result { get; private set; }
        public SettingsDialog(AppSettings current, bool hasTabs, bool persistent)
        {
            original = current.Copy(); Result = current.Copy();
            Text = "基础设置"; Font = new Font("Microsoft YaHei UI", 9F);
            FormBorderStyle = FormBorderStyle.FixedDialog; StartPosition = FormStartPosition.CenterParent;
            MinimizeBox = false; MaximizeBox = false; ShowInTaskbar = false; ClientSize = new Size(510, 290);
            mode.Items.AddRange(new object[] { "嵌入窗口（实验性）", "顶层窗口分组（兼容）" });
            mode.SelectedIndex = current.Mode == HostingMode.Embedded ? 0 : 1; mode.Enabled = !hasTabs;
            shortcuts.Checked = current.ShortcutsEnabled; directory.Text = current.NewTabDirectory;
            var layout = new TableLayoutPanel { Dock = DockStyle.Fill, Padding = new Padding(14), ColumnCount = 2, RowCount = 6 };
            layout.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 105)); layout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
            foreach (int height in new[] { 54, 34, 34, 40, 58, 36 }) layout.RowStyles.Add(new RowStyle(SizeType.Absolute, height));
            var note = new Label { Dock = DockStyle.Fill, Text = (persistent ? "设置保存在本机；正常退出时记住窗口尺寸和位置。" : "设置存储不可用；本次修改只在当前运行中生效。") +
                "\n" + (hasTabs ? "已有标签时不能修改挂载模式，请先分离全部。" : "不会自动捕获窗口，也不会重开上次标签。") };
            layout.Controls.Add(note, 0, 0); layout.SetColumnSpan(note, 2);
            layout.Controls.Add(new Label { Text = "挂载模式", Dock = DockStyle.Fill, TextAlign = ContentAlignment.MiddleLeft }, 0, 1); layout.Controls.Add(mode, 1, 1);
            layout.Controls.Add(shortcuts, 0, 2); layout.SetColumnSpan(shortcuts, 2);
            layout.Controls.Add(new Label { Text = "新建标签目录", Dock = DockStyle.Fill, TextAlign = ContentAlignment.MiddleLeft }, 0, 3);
            var pathRow = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 2, RowCount = 1 };
            pathRow.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100)); pathRow.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 70));
            var browse = new Button { Text = "浏览…", Dock = DockStyle.Fill };
            browse.Click += (_, __) =>
            {
                using (var picker = new FolderBrowserDialog { Description = "选择新建标签默认目录", ShowNewFolderButton = false })
                { if (picker.ShowDialog(this) == DialogResult.OK) directory.Text = picker.SelectedPath; }
            };
            pathRow.Controls.Add(directory, 0, 0); pathRow.Controls.Add(browse, 1, 0); layout.Controls.Add(pathRow, 1, 3);
            layout.Controls.Add(error, 0, 4); layout.SetColumnSpan(error, 2);
            var buttons = new FlowLayoutPanel { Dock = DockStyle.Fill, FlowDirection = FlowDirection.RightToLeft, WrapContents = false };
            var cancel = new Button { Text = "取消", DialogResult = DialogResult.Cancel, AutoSize = true };
            var save = new Button { Text = "保存", AutoSize = true };
            save.Click += (_, __) =>
            {
                try
                {
                    var result = original.Copy(); result.Mode = mode.SelectedIndex == 0 ? HostingMode.Embedded : HostingMode.Grouped;
                    result.ShortcutsEnabled = shortcuts.Checked; result.NewTabDirectory = FolderTarget.Normalize(directory.Text);
                    Result = result; DialogResult = DialogResult.OK;
                }
                catch (ArgumentException issue) { error.Text = issue.Message; directory.Focus(); }
            };
            buttons.Controls.Add(cancel); buttons.Controls.Add(save); layout.Controls.Add(buttons, 0, 5); layout.SetColumnSpan(buttons, 2);
            Controls.Add(layout); AcceptButton = save; CancelButton = cancel;
        }
    }
}
