using System;
using System.Drawing;
using System.Windows.Forms;
using ExplorerTabs.Core;

namespace ExplorerTabs.App
{
    /// <summary>Small application description dialog; the logo is drawn from the embedded icon,
    /// not from the borderless dialog's native title-bar icon (DialogStyle clears it).</summary>
    internal sealed class AboutDialog : ShellDialog
    {
        private readonly PictureBox logo = new PictureBox();
        private readonly Label versionLabel = new Label();
        internal static string VersionText => typeof(AboutDialog).Assembly.GetName().Version?.ToString(3) ?? "未知";

        public AboutDialog()
        {
            Text = "关于 " + ProductIdentity.Name;
            Font = new Font("Microsoft YaHei UI", 9F);
            StartPosition = FormStartPosition.CenterParent;
            MinimizeBox = false; MaximizeBox = false; ShowInTaskbar = false;
            ClientSize = new Size(500, 310);
            BackColor = Color.White;

            var root = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 1, RowCount = 2, BackColor = Color.White };
            root.RowStyles.Add(new RowStyle(SizeType.Percent, 100F));
            root.RowStyles.Add(new RowStyle(SizeType.Absolute, 54F));
            var content = new Panel { Dock = DockStyle.Fill, BackColor = Color.White };
            var bottom = new FlowLayoutPanel
            {
                Dock = DockStyle.Fill, FlowDirection = FlowDirection.RightToLeft,
                WrapContents = false, Padding = new Padding(0, 10, 20, 10), BackColor = Color.White
            };
            var close = new ActionButton("关闭", true) { Width = 90, Height = 32, DialogResult = DialogResult.OK };
            bottom.Controls.Add(close);

            logo.Size = new Size(72, 72); logo.SizeMode = PictureBoxSizeMode.Zoom; logo.Location = new Point(24, 24);
            using (var stream = typeof(AboutDialog).Assembly.GetManifestResourceStream("ExplorerTabs.AppIcon.ico"))
                if (stream != null) using (var icon = new Icon(stream, new Size(64, 64))) logo.Image = icon.ToBitmap();
            var name = new Label
            {
                Text = ProductIdentity.Name, AutoSize = true, Location = new Point(116, 24),
                Font = new Font(Font.FontFamily, 18F, FontStyle.Bold), ForeColor = HostTheme.Text
            };
            versionLabel.Text = "版本 " + VersionText; versionLabel.AutoSize = true;
            versionLabel.Location = new Point(118, 58); versionLabel.ForeColor = Color.FromArgb(108, 122, 143);
            var intro = new Label
            {
                Text = "用标签页管理 Windows 文件资源管理器窗口。\n切换标签时保留每个窗口的浏览状态。",
                AutoSize = false, Size = new Size(360, 44), Location = new Point(116, 84), ForeColor = HostTheme.Text
            };
            var safety = new Label
            {
                Text = "分离或退出会恢复原窗口。\n本程序不会结束共享的 explorer.exe 进程。",
                AutoSize = false, Size = new Size(450, 40), Location = new Point(24, 156), ForeColor = Color.FromArgb(108, 122, 143)
            };
            content.Controls.AddRange(new Control[] { logo, name, versionLabel, intro, safety });
            root.Controls.Add(content, 0, 0); root.Controls.Add(bottom, 0, 1);
            Controls.Add(root);
            AcceptButton = close; CancelButton = close;
            DialogStyle.Apply(this);
        }

        protected override void Dispose(bool disposing)
        {
            if (disposing) { var image = logo.Image; logo.Image = null; image?.Dispose(); }
            base.Dispose(disposing);
        }
    }
}
