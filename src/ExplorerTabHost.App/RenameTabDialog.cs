using System;
using System.Drawing;
using System.Windows.Forms;
using ExplorerTabHost.Core;

namespace ExplorerTabHost.App
{
    internal sealed class RenameTabDialog : Form
    {
        private readonly TextBox name = new TextBox { Dock = DockStyle.Fill, MaxLength = TabManagementPolicy.MaximumTitleLength };
        private readonly Label error = new Label { Dock = DockStyle.Fill, AutoSize = true, ForeColor = Color.Firebrick };
        public string TabTitle => name.Text;
        public RenameTabDialog(string current)
        {
            Text = "重命名标签（不修改目录或 Explorer 标题）";
            Font = new Font("Microsoft YaHei UI", 9F);
            FormBorderStyle = FormBorderStyle.FixedDialog;
            StartPosition = FormStartPosition.CenterParent;
            MinimizeBox = false; MaximizeBox = false; ShowInTaskbar = false;
            ClientSize = new Size(440, 158);
            var layout = new TableLayoutPanel { Dock = DockStyle.Fill, Padding = new Padding(12), ColumnCount = 1, RowCount = 4 };
            layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 28));
            layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 30));
            layout.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
            layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 34));
            layout.Controls.Add(new Label { Text = "名称随保存的会话 / 标签组保留，最多 120 个字符。", Dock = DockStyle.Fill }, 0, 0);
            layout.Controls.Add(name, 0, 1); layout.Controls.Add(error, 0, 2);
            var buttons = new FlowLayoutPanel { Dock = DockStyle.Fill, FlowDirection = FlowDirection.RightToLeft, WrapContents = false };
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
        }
    }
}
