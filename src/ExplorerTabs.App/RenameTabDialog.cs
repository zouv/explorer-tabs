using System;
using System.Drawing;
using System.Windows.Forms;
using ExplorerTabs.Core;

namespace ExplorerTabs.App
{
    internal sealed class RenameTabDialog : ShellDialog
    {
        private readonly TextBox name = new TextBox { Dock = DockStyle.Fill, MaxLength = TabManagementPolicy.MaximumTitleLength };
        private readonly Label error = new Label { Dock = DockStyle.Fill, AutoSize = true, ForeColor = Color.Firebrick };
        public string TabTitle => name.Text;
        public RenameTabDialog(string current)
            : this(current, "重命名标签（不修改目录或 Explorer 标题）") { }
        public RenameTabDialog(string current, string title)
        {
            Text = title;
            Font = new Font("Microsoft YaHei UI", 9F);
            StartPosition = FormStartPosition.CenterParent;
            MinimizeBox = false; MaximizeBox = false; ShowInTaskbar = false;
            ClientSize = new Size(460, 210);
            BackColor = Color.White;
            var layout = new TableLayoutPanel { Dock = DockStyle.Fill, Padding = new Padding(16, 14, 16, 12), ColumnCount = 1, RowCount = 4, BackColor = Color.White };
            layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 30));
            layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 30));
            layout.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
            layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 38));
            layout.Controls.Add(new Label { Text = "名称随保存的会话 / 标签组保留，最多 120 个字符。", Dock = DockStyle.Fill, ForeColor = Color.FromArgb(91, 104, 123) }, 0, 0);
            layout.Controls.Add(name, 0, 1); layout.Controls.Add(error, 0, 2);
            var buttons = new FlowLayoutPanel { Dock = DockStyle.Fill, FlowDirection = FlowDirection.RightToLeft, WrapContents = false, BackColor = Color.White };
            var cancel = new ActionButton("取消") { DialogResult = DialogResult.Cancel, Width = 90, Height = 30 };
            var ok = new ActionButton("确定", true) { Width = 90, Height = 30 };
            ok.Click += (_, __) =>
            {
                try { name.Text = TabManagementPolicy.NormalizeTitle(name.Text); DialogResult = DialogResult.OK; }
                catch (ArgumentException issue) { error.Text = issue.Message; name.Focus(); }
            };
            buttons.Controls.Add(cancel); buttons.Controls.Add(ok);
            layout.Controls.Add(buttons, 0, 3); Controls.Add(layout);
            name.Text = current; AcceptButton = ok; CancelButton = cancel;
            Shown += (_, __) => { name.Focus(); name.SelectAll(); };
            DialogStyle.Apply(this);
        }
    }
}
