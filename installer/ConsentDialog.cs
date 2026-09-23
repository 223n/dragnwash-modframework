using System;
using System.Diagnostics;
using System.Drawing;
using System.IO;
using System.Windows.Forms;

namespace DragNWash.Installer
{
    // Before the first connection, and only when Install would download ModFramework:
    // where the installer connects, why, what it sends and how the file is checked.
    // "Choose zip" takes a zip the player downloaded instead, with no internet. The
    // answer is not remembered; the window comes back whenever a download is needed.
    internal sealed class ConsentDialog : Form
    {
        private readonly TableLayoutPanel _layout = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 3, Padding = new Padding(20, 18, 20, 12), BackColor = SystemColors.Window };
        private readonly TableLayoutPanel _buttons = new TableLayoutPanel { Dock = DockStyle.Bottom, AutoSize = true, ColumnCount = 4, Padding = new Padding(12, 8, 12, 8), BackColor = SystemColors.Control };

        // The zip picked with "Choose zip" when the result is OK; Yes is "Download and install".
        internal string ChosenZip { get; private set; }

        // bepInEx: BepInEx is downloaded too, from the BepInEx/BepInEx releases.
        internal ConsentDialog(string title, FrameworkPlan plan, bool bepInEx)
        {
            AutoScaleMode = AutoScaleMode.Dpi;
            Font = Strings.UiFont();
            Text = title;
            FormBorderStyle = FormBorderStyle.FixedDialog;
            MaximizeBox = false;
            MinimizeBox = false;
            ShowInTaskbar = false;
            StartPosition = FormStartPosition.CenterParent;
            BackColor = SystemColors.Window;
            ClientSize = new Size(580, 300);

            FrameworkPin pin = plan.Pin;
            string and = Strings.Get(Strings.Key.ListAnd);
            string need = plan.Core == null ? Strings.Get(Strings.Key.ConsentNeedMissing, pin.Version)
                : plan.Core < pin.Pinned ? Strings.Get(Strings.Key.ConsentNeedOlder, pin.Version, InstallerCore.ShortVersion(plan.Core))
                : Strings.Get(Strings.Key.ConsentNeedParts, pin.Version);
            string files = $"{pin.ZipName} ({Kilobytes(pin.Size)} KB)" +
                           (bepInEx ? and + $"{Path.GetFileName(new Uri(Paths.BepInExUrl).AbsolutePath)} ({Kilobytes(Paths.BepInExSize)} KB)" : "");

            _layout.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
            _layout.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
            _layout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
            var icon = new PictureBox { Image = SystemIcons.Information.ToBitmap(), SizeMode = PictureBoxSizeMode.AutoSize, Margin = new Padding(0, 0, 14, 0) };
            _layout.Controls.Add(icon, 0, 0);

            AddWide(Paragraph(Strings.Get(Strings.Key.ConsentHead), 2, bold: true));
            AddWide(Paragraph(need, 6));
            AddLine(10);
            AddFact(Strings.Key.ConsentWhere, Strings.Get(Strings.Key.ConsentWhereText, Paths.FrameworkRepo + (bepInEx ? and + Paths.BepInExRepo : ""), Paths.ReleaseAssetsHost));
            AddFact(Strings.Key.ConsentWhy, Strings.Get(Strings.Key.ConsentWhyText, files));
            AddFact(Strings.Key.ConsentSent, Strings.Get(Strings.Key.ConsentSentText, Downloader.UserAgent));
            AddFact(Strings.Key.ConsentCheck, Strings.Get(bepInEx ? Strings.Key.ConsentCheckTextBoth : Strings.Key.ConsentCheckText));
            Label note = Paragraph(Strings.Get(bepInEx ? Strings.Key.ConsentNoApiBepInEx : Strings.Key.ConsentNoApi), 6);
            note.ForeColor = SystemColors.GrayText;
            AddWide(note);
            var page = new LinkLabel { AutoSize = true, Text = Strings.Get(Strings.Key.OpenReleasePage), Margin = new Padding(0, 6, 0, 0) };
            page.LinkClicked += (_, __) => OpenPage(Paths.FrameworkReleasePage(pin.Version));
            AddWide(page);
            _layout.SetRowSpan(icon, _layout.RowCount);

            var zip = new Button { AutoSize = true, MinimumSize = new Size(88, 28), Text = Strings.Get(Strings.Key.ChooseZip) };
            var download = new Button { AutoSize = true, MinimumSize = new Size(88, 28), Text = Strings.Get(Strings.Key.DownloadAndInstall), DialogResult = DialogResult.Yes };
            var cancel = new Button { AutoSize = true, MinimumSize = new Size(88, 28), Text = Strings.Get(Strings.Key.CancelPlain), DialogResult = DialogResult.Cancel };
            _buttons.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
            _buttons.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
            _buttons.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
            _buttons.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
            _buttons.Controls.Add(zip, 0, 0);
            _buttons.Controls.Add(download, 2, 0);
            _buttons.Controls.Add(cancel, 3, 0);
            AcceptButton = download;
            CancelButton = cancel;
            zip.Click += (_, __) =>
            {
                string file = ChooseZip(this, pin.ZipName);
                if (file != null)
                {
                    ChosenZip = file;
                    DialogResult = DialogResult.OK;
                }
            };

            Controls.Add(_layout);
            Controls.Add(_buttons);
            FitHeight();
            // Again once the dialog is scaled to the screen's DPI, where the text wraps differently.
            Load += (_, __) =>
            {
                FitHeight();
                download.Focus();
            };
        }

        // A ModFramework zip the player downloaded; null when they cancel. Checked like a
        // download afterwards, so a zip of another version is refused there.
        internal static string ChooseZip(IWin32Window owner, string zipName)
        {
            using (var dialog = new OpenFileDialog
            {
                Title = Strings.Get(Strings.Key.ChooseZipTitle, zipName),
                FileName = zipName,
                Filter = $"{zipName}|DragNWash.ModFramework-*.zip|zip|*.zip",
                CheckFileExists = true,
            })
            {
                return dialog.ShowDialog(owner) == DialogResult.OK ? dialog.FileName : null;
            }
        }

        // The pinned release's page on github.com, in the browser.
        internal static void OpenPage(string url)
        {
            try
            {
                Process.Start(new ProcessStartInfo(url) { UseShellExecute = true });
            }
            catch (Exception)
            {
            }
        }

        private static long Kilobytes(long bytes) => (bytes + 512) / 1024;

        private Label Paragraph(string text, int top, bool bold = false)
        {
            return new Label
            {
                AutoSize = true, Dock = DockStyle.Fill, Text = text, UseMnemonic = false, Margin = new Padding(0, top, 0, 0),
                Font = bold ? new Font(Font, FontStyle.Bold) : null,
            };
        }

        private void AddWide(Control control)
        {
            _layout.Controls.Add(control, 1, _layout.RowCount);
            _layout.SetColumnSpan(control, 2);
            _layout.RowStyles.Add(new RowStyle(SizeType.AutoSize));
            _layout.RowCount++;
        }

        // A thin rule between the facts, as in a two-column table.
        private void AddLine(int top)
        {
            AddWide(new Panel { Height = 1, Dock = DockStyle.Top, BackColor = Color.FromArgb(229, 229, 229), Margin = new Padding(0, top, 0, 0) });
        }

        private void AddFact(Strings.Key name, string text)
        {
            var label = new Label { AutoSize = true, Text = Strings.Get(name), UseMnemonic = false, Font = new Font(Font, FontStyle.Bold), MinimumSize = new Size(72, 0), Margin = new Padding(0, 6, 10, 6) };
            var value = new Label { AutoSize = true, Dock = DockStyle.Fill, Text = text, UseMnemonic = false, Margin = new Padding(0, 6, 0, 6) };
            _layout.Controls.Add(label, 1, _layout.RowCount);
            _layout.Controls.Add(value, 2, _layout.RowCount);
            _layout.RowStyles.Add(new RowStyle(SizeType.AutoSize));
            _layout.RowCount++;
            AddLine(0);
        }

        // Fit the height to the text at the dialog's width, which wraps.
        private void FitHeight()
        {
            _layout.PerformLayout();
            int height = _layout.GetPreferredSize(new Size(ClientSize.Width, 0)).Height;
            ClientSize = new Size(ClientSize.Width, height + _buttons.GetPreferredSize(new Size(ClientSize.Width, 0)).Height);
        }
    }
}
