using System;
using System.Drawing;
using System.Linq;
using System.Windows.Forms;
using ExplorerTabHost.Core;
namespace ExplorerTabHost.App
{
    internal sealed class SettingsDialog : Form
    {
        private readonly ComboBox mode = new ComboBox { DropDownStyle = ComboBoxStyle.DropDownList, Dock = DockStyle.Fill };
        private readonly CheckBox shortcuts = new CheckBox { Text = "启用宿主快捷键（仅当前宿主 / 标签前台生效）", AutoSize = true };
        private readonly CheckBox wrap = new CheckBox { Text = "多行标签栏（Wrap Tabs）", AutoSize = true };
        private readonly NumericUpDown maximumRows = new NumericUpDown { Minimum = 2, Maximum = 8, Width = 80 };
        private readonly CheckBox remember = new CheckBox { Text = "自动保存会话，启动时提供恢复入口（不自动重开窗口）", AutoSize = true };
        private readonly TextBox directory = new TextBox { Dock = DockStyle.Fill, MaxLength = 4096 };
        private readonly Label error = new Label { Dock = DockStyle.Fill, ForeColor = Color.Firebrick };
        private readonly DataGridView bindings = new DataGridView { Dock = DockStyle.Fill, AllowUserToAddRows = false, AllowUserToDeleteRows = false,
            RowHeadersVisible = false, AutoSizeColumnsMode = DataGridViewAutoSizeColumnsMode.Fill, BackgroundColor = Color.White, BorderStyle = BorderStyle.None,
            SelectionMode = DataGridViewSelectionMode.CellSelect, MultiSelect = false };
        private readonly AppSettings original;
        public AppSettings Result { get; private set; }
        public SettingsDialog(AppSettings current, bool hasTabs, bool persistent)
        {
            original = current.Copy(); Result = current.Copy(); Text = "设置 · ExplorerTabHost"; Font = new Font("Microsoft YaHei UI", 9F);
            FormBorderStyle = FormBorderStyle.FixedDialog; StartPosition = FormStartPosition.CenterParent;
            MinimizeBox = false; MaximizeBox = false; ShowInTaskbar = false; ClientSize = new Size(660, 620); BackColor = Color.White;
            mode.Items.AddRange(new object[] { "嵌入窗口（实验性）", "顶层窗口分组（兼容）" });
            mode.SelectedIndex = current.Mode == HostingMode.Embedded ? 0 : 1; mode.Enabled = !hasTabs;
            shortcuts.Checked = current.ShortcutsEnabled; directory.Text = current.NewTabDirectory;
            wrap.Checked = current.WrapTabs; maximumRows.Value = current.MaximumTabRows; maximumRows.Enabled = wrap.Checked; wrap.CheckedChanged += (_, __) => maximumRows.Enabled = wrap.Checked;
            remember.Checked = current.RememberSession;
            var pages = new TabControl { Dock = DockStyle.Fill }; var general = new TabPage("常规 / 标签栏") { Padding = new Padding(12), BackColor = Color.White }; var keys = new TabPage("快捷键") { Padding = new Padding(12), BackColor = Color.White };
            pages.TabPages.Add(general); pages.TabPages.Add(keys);
            var layout = new TableLayoutPanel { Dock = DockStyle.Top, Height = 440, ColumnCount = 2, RowCount = 9 };
            layout.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 115)); layout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
            foreach (int h in new[] { 70, 42, 38, 38, 42, 54, 44, 80, 30 }) layout.RowStyles.Add(new RowStyle(SizeType.Absolute, h));
            var note = new Label { Dock = DockStyle.Fill, ForeColor = Color.FromArgb(91, 104, 123), Text =
                (persistent ? "设置保存在本机，正常退出时记住窗口布局。" : "设置存储不可用，本次修改只在当前运行生效。") + "\n" +
                (hasTabs ? "挂载模式需先分离全部标签才能更改。" : "不会自动捕获窗口。会话恢复需您主动触发。") };
            layout.Controls.Add(note, 0, 0); layout.SetColumnSpan(note, 2);
            layout.Controls.Add(new Label { Text = "挂载模式", Dock = DockStyle.Fill, TextAlign = ContentAlignment.MiddleLeft }, 0, 1); layout.Controls.Add(mode, 1, 1);
            layout.Controls.Add(shortcuts, 0, 2); layout.SetColumnSpan(shortcuts, 2);
            layout.Controls.Add(wrap, 0, 3); layout.SetColumnSpan(wrap, 2);
            layout.Controls.Add(new Label { Text = "最多可见行数", Dock = DockStyle.Fill, TextAlign = ContentAlignment.MiddleLeft }, 0, 4); layout.Controls.Add(maximumRows, 1, 4);
            layout.Controls.Add(remember, 0, 5); layout.SetColumnSpan(remember, 2);
            layout.Controls.Add(new Label { Text = "新建标签目录", Dock = DockStyle.Fill, TextAlign = ContentAlignment.MiddleLeft }, 0, 6);
            var pathRow = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 2, RowCount = 1 }; pathRow.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100)); pathRow.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 72));
            var browse = new ActionButton("浏览…") { Dock = DockStyle.Fill };
            browse.Click += (_, __) => { using (var picker = new FolderBrowserDialog { Description = "选择新建标签默认目录", ShowNewFolderButton = false }) if (picker.ShowDialog(this) == DialogResult.OK) directory.Text = picker.SelectedPath; };
            pathRow.Controls.Add(directory, 0, 0); pathRow.Controls.Add(browse, 1, 0); layout.Controls.Add(pathRow, 1, 6);
            var safety = new Label { Dock = DockStyle.Fill, ForeColor = Color.FromArgb(91, 104, 123), Text = "多行默认关闭；超过可见行数可滚动。\n关闭标签会关闭该 Explorer；分离与普通退出不会关闭窗口。\n会话仅保存普通目录、顺序、名称和激活标签，不保存浏览历史。" };
            layout.Controls.Add(safety, 0, 7); layout.SetColumnSpan(safety, 2); general.Controls.Add(layout);
            bindings.Columns.Add(new DataGridViewTextBoxColumn { Name = "Action", HeaderText = "操作", ReadOnly = true, FillWeight = 65, SortMode = DataGridViewColumnSortMode.NotSortable });
            var chordColumn = new DataGridViewComboBoxColumn { Name = "Chord", HeaderText = "快捷键", FillWeight = 35, SortMode = DataGridViewColumnSortMode.NotSortable, FlatStyle = FlatStyle.Flat }; chordColumn.Items.AddRange(HostShortcutCatalog.Choices.Cast<object>().ToArray()); bindings.Columns.Add(chordColumn);
            bindings.RowTemplate.Height = 28; bindings.DataError += (_, e) => { error.Text = "请选择有效快捷键。"; e.ThrowException = false; };
            void Fill(System.Collections.Generic.IReadOnlyList<HostShortcut> values) { bindings.Rows.Clear(); foreach (var binding in values) { int row = bindings.Rows.Add(HostShortcutCatalog.Name(binding.Command), binding.Chord); bindings.Rows[row].Tag = binding.Command; } }
            Fill(current.KeyBindings);
            var keyNote = new Label { Dock = DockStyle.Top, Height = 70, Text = "点击右侧组合可修改；空白表示禁用。重复组合不能保存。\nCtrl+P 列出标签，Ctrl+Shift+P 打开命令面板。\n默认保留 Explorer 的 F2 文件重命名；Ctrl+Shift+R 重命名标签。", ForeColor = Color.FromArgb(91, 104, 123) };
            var reset = new ActionButton("恢复默认快捷键") { Dock = DockStyle.Bottom, Height = 32 }; reset.Click += (_, __) => Fill(HostShortcutCatalog.Defaults());
            keys.Controls.Add(bindings); keys.Controls.Add(keyNote); keys.Controls.Add(reset);
            var outer = new TableLayoutPanel { Dock = DockStyle.Fill, Padding = new Padding(12), ColumnCount = 1, RowCount = 3 };
            outer.RowStyles.Add(new RowStyle(SizeType.Percent, 100)); outer.RowStyles.Add(new RowStyle(SizeType.Absolute, 42)); outer.RowStyles.Add(new RowStyle(SizeType.Absolute, 36));
            outer.Controls.Add(pages, 0, 0); outer.Controls.Add(error, 0, 1);
            var buttons = new FlowLayoutPanel { Dock = DockStyle.Fill, FlowDirection = FlowDirection.RightToLeft, WrapContents = false };
            var cancel = new ActionButton("取消") { DialogResult = DialogResult.Cancel, Width = 90, Height = 30 }; var save = new ActionButton("保存", true) { Width = 90, Height = 30 };
            save.Click += (_, __) =>
            {
                try
                {
                    bindings.EndEdit(); var result = original.Copy(); result.Mode = mode.SelectedIndex == 0 ? HostingMode.Embedded : HostingMode.Grouped;
                    result.ShortcutsEnabled = shortcuts.Checked; result.NewTabDirectory = FolderTarget.Normalize(directory.Text);
                    result.WrapTabs = wrap.Checked; result.MaximumTabRows = (int)maximumRows.Value; result.RememberSession = remember.Checked;
                    result.KeyBindings = bindings.Rows.Cast<DataGridViewRow>().Select(row => new HostShortcut { Command = (HostCommand)row.Tag, Chord = (string)row.Cells[1].Value }).ToList();
                    HostShortcutCatalog.Validate(result.KeyBindings); Result = result; DialogResult = DialogResult.OK;
                }
                catch (Exception issue) when (issue is ArgumentException || issue is System.IO.InvalidDataException) { error.Text = issue.Message; }
            };
            buttons.Controls.Add(cancel); buttons.Controls.Add(save); outer.Controls.Add(buttons, 0, 2); Controls.Add(outer); AcceptButton = save; CancelButton = cancel;
        }
    }
}
