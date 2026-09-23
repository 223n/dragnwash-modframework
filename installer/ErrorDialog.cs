using System;
using System.Drawing;
using System.IO;
using System.Net;
using System.Net.Http;
using System.Runtime.InteropServices;
using System.Windows.Forms;

namespace DragNWash.Installer
{
    // A failed Install or Uninstall: what went wrong in plain words, chosen by the
    // kind of exception, with the raw exception behind "Show details" for a bug
    // report. Retry runs the same action again. When getting ModFramework failed, it
    // also offers the release page, a zip the player downloaded ("Choose zip", result
    // OK) and, when the installed framework meets the mod's minimums, installing only
    // the mod (result Ignore).
    internal sealed class ErrorDialog : Form
    {
        private readonly TableLayoutPanel _layout = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 2, Padding = new Padding(20, 18, 20, 10), BackColor = SystemColors.Window };
        private readonly TableLayoutPanel _buttons = new TableLayoutPanel { Dock = DockStyle.Bottom, AutoSize = true, ColumnCount = 5, Padding = new Padding(12, 8, 12, 8), BackColor = SystemColors.Control };
        private readonly LinkLabel _toggle = new LinkLabel { AutoSize = true, Margin = new Padding(0, 10, 0, 0) };
        private readonly TextBox _details = new TextBox
        {
            Multiline = true, ReadOnly = true, WordWrap = false, ScrollBars = ScrollBars.Both, Dock = DockStyle.Fill, Height = 110,
            Font = new Font(FontFamily.GenericMonospace, 8.25f), Margin = new Padding(0, 6, 0, 0), Visible = false,
        };

        // The zip picked with "Choose zip" when the result is OK.
        internal string ChosenZip { get; private set; }

        // details: from Details below, taken where the error was caught.
        internal ErrorDialog(string title, Exception error, string details)
        {
            FrameworkHelp framework = (error as InstallerException)?.Framework;
            AutoScaleMode = AutoScaleMode.Dpi;
            Font = Strings.UiFont();
            Text = title;
            FormBorderStyle = FormBorderStyle.FixedDialog;
            MaximizeBox = false;
            MinimizeBox = false;
            ShowInTaskbar = false;
            StartPosition = FormStartPosition.CenterParent;
            BackColor = SystemColors.Window;
            ClientSize = new Size(framework == null ? 500 : 600, 200);

            var (headline, help) = Describe(error);
            var icon = new PictureBox { Image = SystemIcons.Error.ToBitmap(), SizeMode = PictureBoxSizeMode.AutoSize, Margin = new Padding(0, 0, 14, 0) };
            var headlineLabel = new Label { AutoSize = true, Dock = DockStyle.Fill, Text = headline, UseMnemonic = false, Font = new Font(Font, FontStyle.Bold), Margin = new Padding(0, 2, 0, 0) };
            var helpLabel = new Label { AutoSize = true, Dock = DockStyle.Fill, Text = help ?? "", UseMnemonic = false, Margin = new Padding(0, 6, 0, 0), Visible = help != null };
            _details.Text = details;

            _layout.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
            _layout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
            _layout.Controls.Add(icon, 0, 0);
            _layout.Controls.Add(headlineLabel, 1, 0);
            _layout.Controls.Add(helpLabel, 1, 1);
            int row = 2;
            if (framework != null)
            {
                // The way by hand: the pinned release's page, and the zip from there checked the same way.
                _layout.Controls.Add(new Label { AutoSize = true, Dock = DockStyle.Fill, Text = Strings.Get(Strings.Key.FwManualHelp), UseMnemonic = false, Margin = new Padding(0, 6, 0, 0) }, 1, row++);
                var page = new LinkLabel { AutoSize = true, Text = Strings.Get(Strings.Key.FwReleaseLink, framework.ReleasePage.Substring("https://".Length)), Margin = new Padding(0, 6, 0, 0) };
                page.LinkClicked += (_, __) => ConsentDialog.OpenPage(framework.ReleasePage);
                _layout.Controls.Add(page, 1, row++);
                if (framework.KeepVersion != null)
                {
                    var keep = new LinkLabel { AutoSize = true, Text = Strings.Get(Strings.Key.FwKeepLink, framework.KeepVersion), Margin = new Padding(0, 6, 0, 0) };
                    keep.LinkClicked += (_, __) => DialogResult = DialogResult.Ignore;
                    _layout.Controls.Add(keep, 1, row++);
                }
            }
            _layout.Controls.Add(_toggle, 1, row++);
            _layout.Controls.Add(_details, 1, row++);
            _layout.SetRowSpan(icon, row);

            var copy = new Button { AutoSize = true, MinimumSize = new Size(88, 28), Text = Strings.Get(Strings.Key.CopyDetails) };
            var retry = new Button { AutoSize = true, MinimumSize = new Size(88, 28), Text = Strings.Get(Strings.Key.Retry), DialogResult = DialogResult.Retry };
            var close = new Button { AutoSize = true, MinimumSize = new Size(88, 28), Text = Strings.Get(Strings.Key.Close), DialogResult = DialogResult.Cancel };
            _buttons.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
            _buttons.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
            _buttons.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
            _buttons.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
            _buttons.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
            _buttons.Controls.Add(copy, 0, 0);
            if (framework != null)
            {
                var zip = new Button { AutoSize = true, MinimumSize = new Size(88, 28), Text = Strings.Get(Strings.Key.ChooseZip) };
                zip.Click += (_, __) =>
                {
                    string file = ConsentDialog.ChooseZip(this, framework.ZipName);
                    if (file != null)
                    {
                        ChosenZip = file;
                        DialogResult = DialogResult.OK;
                    }
                };
                _buttons.Controls.Add(zip, 2, 0);
            }
            _buttons.Controls.Add(retry, 3, 0);
            _buttons.Controls.Add(close, 4, 0);
            AcceptButton = retry;
            CancelButton = close;

            Controls.Add(_layout);
            Controls.Add(_buttons);

            _toggle.LinkClicked += (_, __) => ShowDetails(!_details.Visible);
            copy.Click += (_, __) =>
            {
                try
                {
                    Clipboard.SetText(_details.Text);
                }
                catch (ExternalException)
                {
                    // Another program holds the clipboard; the text can still be selected and copied by hand.
                    ShowDetails(true);
                }
            };
            ShowDetails(false);
            // Again once the dialog is scaled to the screen's DPI, where the text wraps differently.
            Load += (_, __) => ShowDetails(false);
        }

        private void ShowDetails(bool show)
        {
            _details.Visible = show;
            _toggle.Text = Strings.Get(show ? Strings.Key.HideDetails : Strings.Key.ShowDetails);
            // Fit the height to the text at the dialog's width, which wraps.
            _layout.PerformLayout();
            int height = _layout.GetPreferredSize(new Size(ClientSize.Width, 0)).Height;
            ClientSize = new Size(ClientSize.Width, height + _buttons.GetPreferredSize(new Size(ClientSize.Width, 0)).Height);
        }

        // The headline and what to do about it. The log keeps the English original.
        internal static (string Headline, string Help) Describe(Exception error)
        {
            switch (error)
            {
                case InstallerException known:
                    // What to do is its own when it says, else that of the failure underneath, when there is one.
                    return (known.Text(), known.HelpKey != null ? Strings.Get(known.HelpKey.Value)
                        : known.InnerException == null ? null : Describe(known.InnerException).Help);
                case HttpRequestException _:
                case WebException _:
                    return (Strings.Get(Strings.Key.DownloadFailed), Strings.Get(Strings.Key.DownloadFailedHelp));
                case UnauthorizedAccessException _:
                case IOException _:
                    return (Strings.Get(Strings.Key.CannotWrite), Strings.Get(Strings.Key.CannotWriteHelp));
                case InvalidDataException _:
                    return (Strings.Get(Strings.Key.BadZip), Strings.Get(Strings.Key.BadZipHelp));
                default:
                    return (Strings.Get(Strings.Key.SomethingWrong), Strings.Get(Strings.Key.SomethingWrongHelp));
            }
        }

        // What "Copy details" gives for a bug report, in English like the log: call it
        // on a thread whose UI culture is the invariant one, so .NET's own messages are too.
        internal static string Details(Exception error)
        {
            if (error is InstallerException known)
            {
                return known.Text(english: true) + (known.Detail == null ? "" : Environment.NewLine + known.Detail);
            }
            return error.ToString();
        }
    }
}
