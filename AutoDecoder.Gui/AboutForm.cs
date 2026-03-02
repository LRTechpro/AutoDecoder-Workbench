#nullable enable
using System;
using System.Diagnostics;
using System.Drawing;
using System.Linq;
using System.Windows.Forms;

namespace AutoDecoder.Gui
{
    public sealed class AboutForm : Form
    {
        private const string RepoUrl = "https://github.com/LRTechpro/AutoDecoderWorkbench";

        public AboutForm()
        {
            Text = "About AutoDecoder Workbench";
            StartPosition = FormStartPosition.CenterParent;
            FormBorderStyle = FormBorderStyle.FixedDialog;
            MaximizeBox = false;
            MinimizeBox = false;
            ShowInTaskbar = false;

            ClientSize = new Size(680, 460);
            BackColor = SystemColors.Control;

            // ---------- Root ----------
            var root = new TableLayoutPanel
            {
                Dock = DockStyle.Fill,
                Padding = new Padding(18, 16, 18, 14),
                ColumnCount = 1,
                RowCount = 4
            };
            root.RowStyles.Add(new RowStyle(SizeType.AutoSize));          // header
            root.RowStyles.Add(new RowStyle(SizeType.Percent, 100));      // body
            root.RowStyles.Add(new RowStyle(SizeType.AutoSize));          // footer link
            root.RowStyles.Add(new RowStyle(SizeType.AutoSize));          // buttons
            Controls.Add(root);

            // ---------- Header ----------
            var header = new Panel { Dock = DockStyle.Top, AutoSize = true, Margin = new Padding(0, 0, 0, 12) };

            var lblTitle = new Label
            {
                Text = "AutoDecoder Workbench v1.0",
                Font = new Font("Segoe UI", 18, FontStyle.Bold),
                AutoSize = true,
                Location = new Point(0, 0)
            };

            var lblSubtitle = new Label
            {
                Text = "Embedded Diagnostics & UDS Analysis Platform",
                Font = new Font("Segoe UI", 11, FontStyle.Regular),
                AutoSize = true,
                Location = new Point(2, 38)
            };

            header.Controls.Add(lblTitle);
            header.Controls.Add(lblSubtitle);

            // ---------- Body Host (card) ----------
            var bodyHost = new Panel
            {
                Dock = DockStyle.Fill,
                BackColor = Color.White,
                Padding = new Padding(12),
                Margin = new Padding(0, 0, 0, 12)
            };

            // Use RichTextBox for reliable multi-line rendering
            var rtb = new RichTextBox
            {
                Dock = DockStyle.Fill,
                ReadOnly = true,
                BorderStyle = BorderStyle.None,
                BackColor = Color.White,
                Font = new Font("Segoe UI", 10, FontStyle.Regular),
                ScrollBars = RichTextBoxScrollBars.Vertical,
                TabStop = false
            };

            // Build info (clean + short)
            var pv = Application.ProductVersion ?? "1.0.0";
            var versionOnly = pv.Split('+')[0];
            var commitShort = TryGetShortCommit(pv);

            rtb.Text =
                "Build Information\r\n" +
                $"Version: {versionOnly}\r\n" +
                $"Commit: {commitShort}\r\n" +
                "Release Channel: Production\r\n" +
                "Architecture: Layered Protocol Engine (CAN / ISO-TP / UDS)\r\n\r\n" +

                "Purpose\r\n" +
                "Analyze ISO 15765 (CAN) diagnostic traffic and UDS protocol exchanges, transforming raw log data into " +
                "structured engineering insight for triage, validation, and embedded system analysis.\r\n\r\n" +

                "Core Capabilities\r\n" +
                "• ISO-TP frame reconstruction and arbitration-aware CAN analysis\r\n" +
                "• UDS service decoding (SIDs, DIDs, NRCs, sessions, subfunctions)\r\n" +
                "• Negative response detection and diagnostic fault identification\r\n" +
                "• Session-aware filtering and search (hex, DID, NRC, SID)\r\n" +
                "• Aggregated findings (NRC frequency, DID usage, conversation counts)\r\n\r\n" +

                "Designed For\r\n" +
                "Automotive cybersecurity engineers • Embedded validation teams • Diagnostic tool developers • " +
                "ECU triage and reverse engineering workflows\r\n\r\n" +

                "Author:\r\n" +
                "Harold L.R. Watkins\r\n";

            // OPTIONAL: bold the section headers
            BoldFirstOccurrence(rtb, "Build Information");
            BoldFirstOccurrence(rtb, "Purpose");
            BoldFirstOccurrence(rtb, "Core Capabilities");
            BoldFirstOccurrence(rtb, "Designed For");
            BoldFirstOccurrence(rtb, "Engineering Lead");

            bodyHost.Controls.Add(rtb);

            // ---------- Footer link ----------
            var link = new LinkLabel
            {
                Text = "Repository / Documentation",
                AutoSize = true,
                Margin = new Padding(0, 0, 0, 10),
                Anchor = AnchorStyles.Left
            };
            link.LinkClicked += (_, __) => OpenUrl(RepoUrl);

            // ---------- Buttons ----------
            var buttons = new FlowLayoutPanel
            {
                Dock = DockStyle.Fill,
                FlowDirection = FlowDirection.RightToLeft,
                WrapContents = false,
                Margin = new Padding(0)
            };

            var btnClose = new Button
            {
                Text = "Close",
                Width = 96,
                Height = 30,
                Margin = new Padding(0)
            };
            btnClose.Click += (_, __) => Close();
            AcceptButton = btnClose;

            buttons.Controls.Add(btnClose);

            // ---------- Assemble ----------
            root.Controls.Add(header, 0, 0);
            root.Controls.Add(bodyHost, 0, 1);
            root.Controls.Add(link, 0, 2);
            root.Controls.Add(buttons, 0, 3);
        }

        private static void BoldFirstOccurrence(RichTextBox rtb, string text)
        {
            int idx = rtb.Text.IndexOf(text, StringComparison.Ordinal);
            if (idx < 0) return;

            rtb.Select(idx, text.Length);
            rtb.SelectionFont = new Font(rtb.Font, FontStyle.Bold);
            rtb.Select(0, 0);
        }

        private static string TryGetShortCommit(string productVersion)
        {
            var parts = productVersion.Split('+');
            if (parts.Length < 2) return "n/a";

            var hash = parts[1];
            if (string.IsNullOrWhiteSpace(hash)) return "n/a";

            var hexOnly = new string(hash.Where(Uri.IsHexDigit).ToArray());
            return hexOnly.Length >= 8 ? hexOnly.Substring(0, 8) : (hexOnly.Length > 0 ? hexOnly : "n/a");
        }

        private static void OpenUrl(string url)
        {
            try
            {
                Process.Start(new ProcessStartInfo { FileName = url, UseShellExecute = true });
            }
            catch
            {
                MessageBox.Show("Could not open the link.", "About",
                    MessageBoxButtons.OK, MessageBoxIcon.Information);
            }
        }
    }
}