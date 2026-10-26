using System;
using System.Collections.Generic;
using System.Drawing;
using System.Linq;
using System.Windows.Forms;
using ExplorerTabHost.Core;
namespace ExplorerTabHost.App
{
    internal sealed class PickerEntry
    {
        public string Title { get; }
        public string Detail { get; }
        public object Value { get; }
        public PickerEntry(string title, string detail, object value) { Title = title; Detail = detail; Value = value; }
    }
    internal sealed class QuickPickerDialog : Form
    {
        private readonly TextBox query = new TextBox { Dock = DockStyle.Fill, BorderStyle = BorderStyle.FixedSingle };
        private readonly ListBox results = new ListBox { Dock = DockStyle.Fill, IntegralHeight = false, DrawMode = DrawMode.OwnerDrawFixed, ItemHeight = 48, BorderStyle = BorderStyle.None };
        private readonly Label count = new Label { Dock = DockStyle.Fill, ForeColor = Color.FromArgb(102, 113, 131) };
        private readonly IReadOnlyList<PickerEntry> entries;
        public object? SelectedValue => (results.SelectedItem as PickerEntry)?.Value;
        public QuickPickerDialog(string title, IReadOnlyList<PickerEntry> entries)
        {
            this.entries = entries; Text = title; Font = new Font("Microsoft YaHei UI", 9F);
            StartPosition = FormStartPosition.CenterParent; MinimizeBox = false; MaximizeBox = false; ShowInTaskbar = false;
            ClientSize = new Size(650, 450); MinimumSize = new Size(450, 300); BackColor = Color.White;
            var layout = new TableLayoutPanel { Dock = DockStyle.Fill, Padding = new Padding(16), ColumnCount = 1, RowCount = 3 };
            layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 38)); layout.RowStyles.Add(new RowStyle(SizeType.Percent, 100)); layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 30));
            layout.Controls.Add(query, 0, 0); layout.Controls.Add(results, 0, 1); layout.Controls.Add(count, 0, 2); Controls.Add(layout);
            query.AccessibleName = "输入标签名或目录关键字"; results.AccessibleName = "匹配列表";
            query.TextChanged += (_, __) => Filter(); query.KeyDown += QueryKey;
            results.DoubleClick += (_, __) => AcceptSelection();
            results.KeyDown += (_, e) => { if (e.KeyCode == Keys.Enter) { AcceptSelection(); e.SuppressKeyPress = true; } };
            results.DrawItem += (_, e) =>
            {
                if (e.Index < 0) return;
                var item = (PickerEntry)results.Items[e.Index]; bool selected = (e.State & DrawItemState.Selected) != 0;
                using (var brush = new SolidBrush(selected ? Color.FromArgb(227, 237, 251) : Color.White)) e.Graphics.FillRectangle(brush, e.Bounds);
                TextRenderer.DrawText(e.Graphics, item.Title, Font, new Rectangle(e.Bounds.Left + 10, e.Bounds.Top + 4, e.Bounds.Width - 20, 22), Color.FromArgb(31, 43, 60), TextFormatFlags.EndEllipsis | TextFormatFlags.NoPrefix);
                TextRenderer.DrawText(e.Graphics, item.Detail, Font, new Rectangle(e.Bounds.Left + 10, e.Bounds.Top + 25, e.Bounds.Width - 20, 19), Color.FromArgb(101, 112, 130), TextFormatFlags.EndEllipsis | TextFormatFlags.NoPrefix);
                e.DrawFocusRectangle();
            };
            Shown += (_, __) => query.Focus(); Filter();
        }
        private void Filter()
        {
            results.BeginUpdate(); results.Items.Clear();
            foreach (var entry in entries.Where(e => TabSearchPolicy.Matches(e.Title, e.Detail, query.Text))) results.Items.Add(entry);
            if (results.Items.Count > 0) results.SelectedIndex = 0;
            results.EndUpdate(); count.Text = $"{results.Items.Count} / {entries.Count} 项 · ↑↓ 选择 · Enter 打开 · Esc 取消";
        }
        private void QueryKey(object? sender, KeyEventArgs e)
        {
            if (e.KeyCode == Keys.Down || e.KeyCode == Keys.Up)
            {
                if (results.Items.Count > 0) results.SelectedIndex = Math.Max(0, Math.Min(results.Items.Count - 1, results.SelectedIndex + (e.KeyCode == Keys.Down ? 1 : -1)));
                e.SuppressKeyPress = true;
            }
            else if (e.KeyCode == Keys.Enter) { AcceptSelection(); e.SuppressKeyPress = true; }
        }
        private void AcceptSelection() { if (SelectedValue != null) DialogResult = DialogResult.OK; }
        protected override bool ProcessCmdKey(ref Message msg, Keys keys)
        { if (keys == Keys.Escape) { DialogResult = DialogResult.Cancel; return true; } return base.ProcessCmdKey(ref msg, keys); }
    }
}
