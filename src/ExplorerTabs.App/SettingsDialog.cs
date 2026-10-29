using System;
using System.Drawing;
using System.IO;
using System.Linq;
using System.Windows.Forms;
using ExplorerTabs.Core;

namespace ExplorerTabs.App
{
    internal sealed class SettingsDialog : ShellDialog
    {
        private readonly ComboBox mode = new ComboBox { DropDownStyle = ComboBoxStyle.DropDownList, FlatStyle = FlatStyle.Flat };
        private readonly ThemedCheckBox shortcuts = new ThemedCheckBox("启用宿主快捷键（仅当前宿主 / 标签前台生效）");
        private readonly ThemedCheckBox wrap = new ThemedCheckBox("多行标签栏（Wrap Tabs）");
        private readonly NumericUpDown maximumRows = new NumericUpDown
        { Minimum = 2, Maximum = 8, Width = 80, BorderStyle = BorderStyle.None, TextAlign = HorizontalAlignment.Center };
        private readonly ThemedCheckBox remember = new ThemedCheckBox("自动保存会话，启动时提供恢复入口（不自动重开窗口）");
        private readonly TextBox directory = new TextBox { MaxLength = 4096, BorderStyle = BorderStyle.None };
        private readonly TextBox globalHotkey = new TextBox
        { ReadOnly = true, BorderStyle = BorderStyle.None, ShortcutsEnabled = false, Cursor = Cursors.Hand };
        private readonly Label error = new Label
        { Dock = DockStyle.Fill, ForeColor = Color.Firebrick, BackColor = Color.White, TextAlign = ContentAlignment.MiddleLeft, AutoEllipsis = true };
        private readonly Label globalHotkeyHint = new Label();
        private readonly Label directoryHint = new Label();
        private readonly DataGridView bindings = new DataGridView
        {
            Dock = DockStyle.Fill, AllowUserToAddRows = false, AllowUserToDeleteRows = false,
            RowHeadersVisible = false, AutoSizeColumnsMode = DataGridViewAutoSizeColumnsMode.Fill, BackgroundColor = Color.White,
            BorderStyle = BorderStyle.None, SelectionMode = DataGridViewSelectionMode.CellSelect, MultiSelect = false,
            EnableHeadersVisualStyles = false, GridColor = Color.FromArgb(232, 237, 244), CellBorderStyle = DataGridViewCellBorderStyle.SingleHorizontal
        };
        private readonly AppSettings original;
        private readonly PresetButton userPreset, thisPcPreset, documentsPreset, downloadsPreset;
        public AppSettings Result { get; private set; }
        /// <summary>Optional live registration check supplied by MainForm; returns an error message or null.</summary>
        public Func<string, string?>? GlobalHotkeyCheck { get; set; }

        public SettingsDialog(AppSettings current, bool hasTabs, bool persistent)
        {
            original = current.Copy(); Result = current.Copy();
            Text = "设置 · ExplorerTabs"; Font = new Font("Microsoft YaHei UI", 9F);
            StartPosition = FormStartPosition.CenterParent;
            MinimizeBox = false; MaximizeBox = false; ShowInTaskbar = false;
            ClientSize = new Size(880, 640); BackColor = Color.White;

            mode.Items.AddRange(new object[] { "嵌入窗口（实验性）", "顶层窗口分组（兼容）" });
            mode.SelectedIndex = current.Mode == HostingMode.Embedded ? 0 : 1; mode.Enabled = !hasTabs;
            shortcuts.Checked = current.ShortcutsEnabled;
            wrap.Checked = current.WrapTabs; maximumRows.Value = current.MaximumTabRows;
            maximumRows.Enabled = wrap.Checked; wrap.CheckedChanged += (_, __) => maximumRows.Enabled = wrap.Checked;
            remember.Checked = current.RememberSession;
            directory.Text = FolderTarget.Display(current.NewTabDirectory);
            globalHotkey.Text = current.GlobalHotkey ?? "";
            globalHotkey.Enter += (_, __) => { globalHotkeyHint.Text = "请按下新的组合键（Esc 清空，点击其它位置结束）。"; };
            globalHotkey.Leave += (_, __) => globalHotkeyHint.Text = "点击输入框后按下组合键（Ctrl / Alt / Shift + 字母、数字或 F1–F12）；Esc 清空停用。";
            styleHotkeyHint();
            directoryHint.Dock = DockStyle.Fill; directoryHint.ForeColor = HostTheme.MutedText; directoryHint.AutoSize = false;

            userPreset = new PresetButton("用户目录");
            thisPcPreset = new PresetButton("我的电脑");
            documentsPreset = new PresetButton("文档");
            downloadsPreset = new PresetButton("下载");
            var custom = new PresetButton("自定义…") { Width = 92 };
            foreach (var preset in new[] { userPreset, thisPcPreset, documentsPreset, downloadsPreset })
                preset.Click += (_, __) => ChooseDirectory(((PresetButton)preset).Text);
            custom.Click += (_, __) => BrowseDirectory();
            var presetRow = new FlowLayoutPanel
            { Dock = DockStyle.Fill, FlowDirection = FlowDirection.LeftToRight, WrapContents = false, BackColor = Color.White };
            presetRow.Controls.AddRange(new Control[] { userPreset, thisPcPreset, documentsPreset, downloadsPreset, custom });
            directory.TextChanged += (_, __) => RefreshDirectoryState();

            var pages = BuildPages(hasTabs, persistent, presetRow);
            var nav = BuildNavigation(pages);
            var footer = BuildFooter();
            var root = new Panel { Dock = DockStyle.Fill, BackColor = Color.White };
            root.Controls.Add(pages.Host); root.Controls.Add(nav); root.Controls.Add(footer);
            Controls.Add(root);
            RefreshDirectoryState();
            SelectPage(pages, 0, nav);
            DialogStyle.Apply(this);
        }

        private sealed class Pages
        {
            public Panel Host = null!;
            public Panel General = null!, Tabs = null!, Keys = null!;
            public SidebarButton GeneralNav = null!, TabsNav = null!, KeysNav = null!;
        }

        private Pages BuildPages(bool hasTabs, bool persistent, Control presetRow)
        {
            var pages = new Pages();
            pages.Host = new Panel { Dock = DockStyle.Fill, BackColor = Color.White, Padding = new Padding(26, 20, 26, 10) };

            // 常规
            var general = Page();
            Row(general, Note((persistent ? "设置保存在本机，正常退出时记住窗口布局。" : "设置存储不可用，本次修改只在当前运行生效。") + "\n" +
                (hasTabs ? "挂载模式需先分离全部标签才能更改。" : "不会自动捕获窗口。会话恢复需您主动触发。")), 64);
            Row(general, Section("启动与挂载"), 34);
            Row(general, FormRow("挂载模式", new FieldHost(mode)), 42);
            Row(general, shortcuts, 34);
            Row(general, remember, 34);
            Row(general, Section("新建标签目录"), 34);
            Row(general, presetRow, 38);
            var pathRow = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 2, RowCount = 1, BackColor = Color.White };
            pathRow.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
            pathRow.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 78));
            var pathField = new FieldHost(directory) { Dock = DockStyle.Top };
            var browse = new ActionButton("浏览…") { Dock = DockStyle.Top, Height = 30 };
            browse.Click += (_, __) => BrowseDirectory();
            pathRow.Controls.Add(pathField, 0, 0); pathRow.Controls.Add(browse, 1, 0);
            Row(general, pathRow, 42);
            Row(general, directoryHint, 26);
            Spacer(general);
            pages.General = general;

            // 标签栏
            var tabs = Page();
            Row(tabs, Section("多行标签栏"), 34);
            Row(tabs, wrap, 34);
            Row(tabs, FormRow("最多可见行数", new FieldHost(maximumRows)), 42);
            Row(tabs, Note("关闭多行时标签保持单行并可横向滚动；开启后按窗口宽度自动换行，超过可见行数可滚动。\n" +
                "多行默认关闭。"), 72);
            Spacer(tabs);
            pages.Tabs = tabs;

            // 快捷键
            var keys = Page();
            Row(keys, Section("全局快捷键"), 34);
            Row(keys, Note("在任意程序前台按下组合键，可呼出或隐藏 ExplorerTabs。留空表示停用。"), 44);
            var hotkeyRow = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 3, RowCount = 1, BackColor = Color.White };
            hotkeyRow.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 120));
            hotkeyRow.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
            hotkeyRow.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 78));
            hotkeyRow.Controls.Add(new Label { Text = "呼出 / 隐藏", Dock = DockStyle.Fill, TextAlign = ContentAlignment.MiddleLeft, ForeColor = HostTheme.Text }, 0, 0);
            hotkeyRow.Controls.Add(new FieldHost(globalHotkey) { Dock = DockStyle.Top }, 1, 0);
            var clearHotkey = new ActionButton("清除") { Dock = DockStyle.Top, Height = 30 };
            clearHotkey.Click += (_, __) => { globalHotkey.Text = ""; globalHotkey.Focus(); };
            hotkeyRow.Controls.Add(clearHotkey, 2, 0);
            Row(keys, hotkeyRow, 42);
            Row(keys, globalHotkeyHint, 40);
            Row(keys, Section("宿主快捷键"), 34);
            var gridHost = new Panel { Dock = DockStyle.Fill, BackColor = Color.White, Padding = new Padding(1) };
            gridHost.Paint += (_, e) => { using (var pen = new Pen(HostTheme.FieldBorder)) e.Graphics.DrawRectangle(pen, 0, 0, gridHost.Width - 1, gridHost.Height - 1); };
            gridHost.Controls.Add(bindings);
            Row(keys, gridHost, 200);
            Row(keys, Note("点击右侧组合可修改；空白表示禁用。重复组合不能保存。\n" +
                "Ctrl+P 列出标签，Ctrl+Shift+P 打开命令面板。\n默认保留 Explorer 的 F2 文件重命名。"), 58);
            var resetRow = new Panel { Dock = DockStyle.Fill, BackColor = Color.White };
            var reset = new ActionButton("恢复默认快捷键") { Dock = DockStyle.Right, Width = 140, Height = 30 };
            reset.Click += (_, __) => Fill(HostShortcutCatalog.Defaults());
            resetRow.Controls.Add(reset);
            Row(keys, resetRow, 34);
            Spacer(keys);
            pages.Keys = keys;

            pages.Host.Controls.Add(pages.General); pages.Host.Controls.Add(pages.Tabs); pages.Host.Controls.Add(pages.Keys);
            pages.Tabs.Visible = false; pages.Keys.Visible = false;
            SetupShortcutGrid();
            Fill(original.KeyBindings);
            return pages;
        }

        private Control BuildNavigation(Pages pages)
        {
            var nav = new Panel { Dock = DockStyle.Left, Width = 196, BackColor = HostTheme.Canvas, Padding = new Padding(12, 18, 12, 12) };
            var stack = new FlowLayoutPanel
            { Dock = DockStyle.Top, FlowDirection = FlowDirection.TopDown, WrapContents = false, AutoSize = true, AutoSizeMode = AutoSizeMode.GrowAndShrink, BackColor = HostTheme.Canvas };
            pages.GeneralNav = new SidebarButton(UiGlyph.Settings, "常规") { Width = 172 };
            pages.TabsNav = new SidebarButton(UiGlyph.TabBar, "标签栏") { Width = 172 };
            pages.KeysNav = new SidebarButton(UiGlyph.Keyboard, "快捷键") { Width = 172 };
            pages.GeneralNav.Click += (_, __) => SelectPage(pages, 0, nav);
            pages.TabsNav.Click += (_, __) => SelectPage(pages, 1, nav);
            pages.KeysNav.Click += (_, __) => SelectPage(pages, 2, nav);
            stack.Controls.AddRange(new Control[] { pages.GeneralNav, pages.TabsNav, pages.KeysNav });
            nav.Controls.Add(stack);
            return nav;
        }

        private Control BuildFooter()
        {
            var footer = new Panel { Dock = DockStyle.Bottom, Height = 66, BackColor = Color.White, Padding = new Padding(20, 12, 20, 12) };
            var buttons = new FlowLayoutPanel { Dock = DockStyle.Right, Width = 200, FlowDirection = FlowDirection.RightToLeft, WrapContents = false, BackColor = Color.White };
            var cancel = new ActionButton("取消") { DialogResult = DialogResult.Cancel, Width = 90, Height = 32 };
            var save = new ActionButton("保存", true) { Width = 90, Height = 32 };
            save.Click += (_, __) => Save();
            buttons.Controls.Add(cancel); buttons.Controls.Add(save);
            footer.Controls.Add(error); footer.Controls.Add(buttons);
            AcceptButton = save; CancelButton = cancel;
            return footer;
        }

        private void SelectPage(Pages pages, int index, Control? nav)
        {
            pages.General.Visible = index == 0; pages.Tabs.Visible = index == 1; pages.Keys.Visible = index == 2;
            pages.GeneralNav.Selected = index == 0; pages.TabsNav.Selected = index == 1; pages.KeysNav.Selected = index == 2;
            if (index == 2) globalHotkeyHint.Text = "点击输入框后按下组合键（Ctrl / Alt / Shift + 字母、数字或 F1–F12）；Esc 清空停用。";
        }

        private void ChooseDirectory(string preset)
        {
            if (preset == "用户目录") directory.Text = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
            else if (preset == "我的电脑") directory.Text = "此电脑";
            else if (preset == "文档") directory.Text = Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments);
            else if (preset == "下载") directory.Text = DownloadsPath();
            directory.Focus();
        }
        private void BrowseDirectory()
        {
            using (var picker = new FolderBrowserDialog { Description = "选择新建标签默认目录", ShowNewFolderButton = false })
                if (picker.ShowDialog(this) == DialogResult.OK) directory.Text = picker.SelectedPath;
        }
        private static string DownloadsPath()
        {
            string path = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), "Downloads");
            return Directory.Exists(path) ? path : Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
        }
        private void RefreshDirectoryState()
        {
            string value = CurrentDirectoryValue();
            bool Matches(string preset)
            {
                try { return FolderTarget.Same(value, preset); } catch { return false; }
            }
            userPreset.Selected = Matches(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile));
            thisPcPreset.Selected = Matches(FolderTarget.ThisPc);
            documentsPreset.Selected = Matches(Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments));
            downloadsPreset.Selected = Matches(DownloadsPath());
            string text = directory.Text.Trim();
            directoryHint.Text = "当前：" + (FolderTarget.IsThisPc(value) ? "此电脑" : (string.IsNullOrEmpty(text) ? "未设置" : text));
        }
        private string CurrentDirectoryValue()
        {
            string text = directory.Text.Trim();
            return text == "此电脑" ? FolderTarget.ThisPc : text;
        }
        private void styleHotkeyHint()
        {
            globalHotkeyHint.Dock = DockStyle.Fill; globalHotkeyHint.ForeColor = HostTheme.MutedText; globalHotkeyHint.AutoSize = false;
        }

        private void SetupShortcutGrid()
        {
            bindings.Columns.Add(new DataGridViewTextBoxColumn
            { Name = "Action", HeaderText = "操作", ReadOnly = true, FillWeight = 65, SortMode = DataGridViewColumnSortMode.NotSortable });
            var chordColumn = new DataGridViewComboBoxColumn
            { Name = "Chord", HeaderText = "快捷键", FillWeight = 35, SortMode = DataGridViewColumnSortMode.NotSortable, FlatStyle = FlatStyle.Flat };
            chordColumn.Items.AddRange(HostShortcutCatalog.Choices.Cast<object>().ToArray());
            bindings.Columns.Add(chordColumn);
            bindings.RowTemplate.Height = 28; bindings.ColumnHeadersHeight = 30;
            bindings.ColumnHeadersDefaultCellStyle.BackColor = HostTheme.Canvas;
            bindings.ColumnHeadersDefaultCellStyle.ForeColor = HostTheme.Text;
            bindings.DefaultCellStyle.BackColor = Color.White;
            bindings.DefaultCellStyle.ForeColor = HostTheme.Text;
            bindings.DefaultCellStyle.SelectionBackColor = Color.FromArgb(232, 240, 253);
            bindings.DefaultCellStyle.SelectionForeColor = HostTheme.Text;
            bindings.DataError += (_, e) => { error.Text = "请选择有效快捷键。"; e.ThrowException = false; };
        }
        private void Fill(System.Collections.Generic.IReadOnlyList<HostShortcut> values)
        {
            bindings.Rows.Clear();
            foreach (var binding in values)
            {
                int row = bindings.Rows.Add(HostShortcutCatalog.Name(binding.Command), binding.Chord);
                bindings.Rows[row].Tag = binding.Command;
            }
        }
        protected override bool ProcessCmdKey(ref Message msg, Keys keyData)
        {
            if (!globalHotkey.Focused) return base.ProcessCmdKey(ref msg, keyData);
            var key = keyData & Keys.KeyCode;
            if (key == Keys.Escape) { globalHotkey.Text = ""; return true; }
            if (key == Keys.ControlKey || key == Keys.ShiftKey || key == Keys.Menu || key == Keys.LWin || key == Keys.RWin)
                return true;
            int modifiers = 0;
            if ((keyData & Keys.Control) != 0) modifiers |= GlobalHotkeyDefinition.Control;
            if ((keyData & Keys.Alt) != 0) modifiers |= GlobalHotkeyDefinition.Alt;
            if ((keyData & Keys.Shift) != 0) modifiers |= GlobalHotkeyDefinition.Shift;
            string chord = GlobalHotkeyDefinition.FromVirtualKey((int)key, modifiers);
            if (chord.Length > 0 && GlobalHotkeyDefinition.IsValid(chord)) globalHotkey.Text = chord;
            return true;
        }
        private void Save()
        {
            try
            {
                bindings.EndEdit();
                var result = original.Copy();
                result.Mode = mode.SelectedIndex == 0 ? HostingMode.Embedded : HostingMode.Grouped;
                result.ShortcutsEnabled = shortcuts.Checked;
                result.NewTabDirectory = FolderTarget.Normalize(CurrentDirectoryValue());
                result.WrapTabs = wrap.Checked; result.MaximumTabRows = (int)maximumRows.Value; result.RememberSession = remember.Checked;
                result.KeyBindings = bindings.Rows.Cast<DataGridViewRow>()
                    .Select(row => new HostShortcut { Command = (HostCommand)row.Tag, Chord = (string)row.Cells[1].Value }).ToList();
                HostShortcutCatalog.Validate(result.KeyBindings);
                string chord = GlobalHotkeyDefinition.Normalize(globalHotkey.Text);
                if (GlobalHotkeyDefinition.Conflicts(chord, result.KeyBindings))
                    throw new ArgumentException("全局快捷键不能与宿主快捷键重复。");
                if (chord.Length > 0 && GlobalHotkeyCheck != null)
                {
                    string? issue = GlobalHotkeyCheck(chord);
                    if (!string.IsNullOrEmpty(issue)) throw new ArgumentException(issue);
                }
                result.GlobalHotkey = chord.Length == 0 ? null : chord;
                Result = result; DialogResult = DialogResult.OK;
            }
            catch (Exception issue) when (issue is ArgumentException || issue is InvalidDataException || issue is NotSupportedException || issue is PathTooLongException)
            { error.Text = issue.Message; }
        }

        private static TableLayoutPanel Page()
        {
            var page = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 1, RowCount = 0, BackColor = Color.White };
            page.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
            return page;
        }
        private static void Row(TableLayoutPanel page, Control control, int height)
        {
            control.Dock = DockStyle.Fill; control.Margin = Padding.Empty;
            page.RowStyles.Add(new RowStyle(SizeType.Absolute, height));
            page.Controls.Add(control, 0, page.RowCount++);
        }
        private static void Spacer(TableLayoutPanel page)
        { page.RowStyles.Add(new RowStyle(SizeType.Percent, 100)); page.RowCount++; }
        private static TableLayoutPanel FormRow(string labelText, Control field)
        {
            var row = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 2, RowCount = 1, BackColor = Color.White };
            row.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 120));
            row.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
            row.Controls.Add(new Label { Text = labelText, Dock = DockStyle.Fill, TextAlign = ContentAlignment.MiddleLeft, ForeColor = HostTheme.Text }, 0, 0);
            field.Dock = DockStyle.Top; field.Margin = Padding.Empty;
            row.Controls.Add(field, 1, 0);
            return row;
        }
        private Label Section(string text) => new Label
        {
            Text = text, Dock = DockStyle.Fill, TextAlign = ContentAlignment.MiddleLeft, ForeColor = HostTheme.Text,
            Font = new Font(Font.FontFamily, 10.5F, FontStyle.Bold)
        };
        private static Label Note(string text) => new Label
        {
            Text = text, Dock = DockStyle.Fill, BackColor = HostTheme.NoteBackground, ForeColor = HostTheme.MutedText,
            Padding = new Padding(12, 10, 12, 10), AutoSize = false
        };
    }
}
