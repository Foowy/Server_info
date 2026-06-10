using System;
using System.Collections.Generic;
using System.Drawing;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Windows.Forms;

namespace ServerInfoSubmitter
{
    public class MainForm : Form
    {
        private AppConfig _config;

        // Controls
        private TextBox     _tbServers      = null!;
        private TextBox     _tbIncident     = null!;
        private Button      _btnRun         = null!;
        private Button      _btnSettings    = null!;
        private Label       _lblStatus      = null!;
        private ProgressBar _progressBar    = null!;
        private RichTextBox _rtbOutput      = null!;
        private ToolTip     _toolTip        = null!;

        public MainForm(AppConfig config)
        {
            _config = config;
            BuildUi();
        }

        private void BuildUi()
        {
            Text            = "Server Info Submitter";
            FormBorderStyle = FormBorderStyle.Sizable;
            MinimumSize     = new Size(680, 480);
            StartPosition   = FormStartPosition.CenterScreen;
            ClientSize      = new Size(700, 540);
            Font            = new Font("Segoe UI", 9f);

            _toolTip = new ToolTip();

            // --- Servers row ---
            AddLabel("Servers:", 10, 14);
            _tbServers = new TextBox
            {
                Location = new Point(90, 11),
                Size     = new Size(528, 23),
                Anchor   = AnchorStyles.Top | AnchorStyles.Left | AnchorStyles.Right
            };
            WinFormsHelper.SetCueBanner(_tbServers, "e.g.  Server01,Server02  (blank = local machine)");
            _toolTip.SetToolTip(_tbServers,
                "Enter server names or IP addresses separated by commas.\r\nLeave blank to query the local machine.");
            Controls.Add(_tbServers);

            var btnHelp = new Button
            {
                Text     = "?",
                Size     = new Size(26, 23),
                Location = new Point(624, 11),
                FlatStyle = FlatStyle.Flat,
                Anchor   = AnchorStyles.Top | AnchorStyles.Right
            };
            btnHelp.FlatAppearance.BorderColor = SystemColors.ControlDark;
            btnHelp.Click += (s, e) => MessageBox.Show(
                "Enter one or more server names or IP addresses, comma-separated.\r\n\r\n" +
                "Leave blank to query the local machine.\r\n\r\n" +
                "Examples:\r\n  Server01\r\n  Server01, Server02\r\n  10.0.0.15",
                "Servers help", MessageBoxButtons.OK, MessageBoxIcon.Information);
            Controls.Add(btnHelp);

            // --- Incident row ---
            AddLabel("Incident:", 10, 45);
            _tbIncident = new TextBox
            {
                Location        = new Point(90, 42),
                Size            = new Size(200, 23),
                CharacterCasing = CharacterCasing.Upper
            };
            WinFormsHelper.SetCueBanner(_tbIncident, "INC0012345");
            Controls.Add(_tbIncident);

            // --- Buttons row ---
            _btnRun = new Button
            {
                Text     = "Collect && Submit",
                Size     = new Size(140, 30),
                Location = new Point(90, 80),
                BackColor = Color.FromArgb(0, 120, 212),
                ForeColor = Color.White,
                FlatStyle = FlatStyle.Flat,
                Font      = new Font("Segoe UI", 9f, FontStyle.Bold)
            };
            _btnRun.FlatAppearance.BorderSize = 0;
            _btnRun.Click += BtnRun_Click;
            Controls.Add(_btnRun);

            _btnSettings = new Button
            {
                Text     = "Settings",
                Size     = new Size(88, 30),
                Location = new Point(240, 80),
                FlatStyle = FlatStyle.Flat
            };
            _btnSettings.Click += BtnSettings_Click;
            Controls.Add(_btnSettings);

            // --- Status label ---
            _lblStatus = new Label
            {
                Text     = "Ready.",
                Location = new Point(10, 124),
                Size     = new Size(680, 18),
                Anchor   = AnchorStyles.Top | AnchorStyles.Left | AnchorStyles.Right
            };
            Controls.Add(_lblStatus);

            // --- Progress bar ---
            _progressBar = new ProgressBar
            {
                Location = new Point(10, 145),
                Size     = new Size(680, 14),
                Style    = ProgressBarStyle.Marquee,
                MarqueeAnimationSpeed = 0, // stopped until an operation starts
                Anchor   = AnchorStyles.Top | AnchorStyles.Left | AnchorStyles.Right
            };
            Controls.Add(_progressBar);

            // --- Output area ---
            _rtbOutput = new RichTextBox
            {
                Location   = new Point(10, 168),
                Size       = new Size(680, 340),
                ReadOnly   = true,
                BackColor  = Color.FromArgb(30, 30, 30),
                ForeColor  = Color.FromArgb(220, 220, 220),
                Font       = new Font("Consolas", 9f),
                ScrollBars = RichTextBoxScrollBars.Vertical,
                Anchor     = AnchorStyles.Top | AnchorStyles.Bottom | AnchorStyles.Left | AnchorStyles.Right,
                BorderStyle = BorderStyle.None
            };
            Controls.Add(_rtbOutput);
        }

        private void AddLabel(string text, int x, int y)
        {
            Controls.Add(new Label
            {
                Text      = text,
                Location  = new Point(x, y + 3),
                Size      = new Size(78, 18),
                TextAlign = ContentAlignment.MiddleRight
            });
        }

        private void SetBusy(bool busy)
        {
            _btnRun.Enabled                   = !busy;
            _btnSettings.Enabled              = !busy;
            _progressBar.MarqueeAnimationSpeed = busy ? 30 : 0;
        }

        private void AppendOutput(string text, Color? color = null)
        {
            _rtbOutput.SelectionStart  = _rtbOutput.TextLength;
            _rtbOutput.SelectionLength = 0;
            _rtbOutput.SelectionColor  = color ?? Color.FromArgb(220, 220, 220);
            _rtbOutput.AppendText(text + "\n");
            _rtbOutput.ScrollToCaret();
        }

        private void SetStatus(string text) => _lblStatus.Text = text;

        private async void BtnRun_Click(object? sender, EventArgs e)
        {
            // Validate inputs
            string incidentRaw = _tbIncident.Text.Trim().ToUpper();
            if (string.IsNullOrWhiteSpace(incidentRaw) || !System.Text.RegularExpressions.Regex.IsMatch(incidentRaw, @"^INC\d+$"))
            {
                MessageBox.Show("Enter a valid incident number (e.g. INC0012345).",
                    "Validation", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                _tbIncident.Focus();
                return;
            }

            string[] servers = string.IsNullOrWhiteSpace(_tbServers.Text)
                ? new[] { Environment.MachineName }
                : _tbServers.Text.Split(new[] { ',' }, StringSplitOptions.RemoveEmptyEntries)
                                 .Select(s => s.Trim())
                                 .Where(s => !string.IsNullOrEmpty(s))
                                 .ToArray();

            SetBusy(true);
            _rtbOutput.Clear();

            // ---- Phase 1: Collect metrics ----
            var allMetrics = new List<ServerMetrics>();
            var progress   = new Progress<string>(msg => SetStatus(msg));

            foreach (string server in servers)
            {
                AppendOutput($"=== Collecting from {server} ===", Color.FromArgb(100, 180, 255));
                SetStatus($"Querying {server}...");
                try
                {
                    ServerMetrics m = await MetricsCollector.CollectAsync(server);
                    allMetrics.Add(m);
                    AppendOutput(FormatConsolePreview(m));
                }
                catch (Exception ex)
                {
                    AppendOutput($"ERROR: {ex.Message}", Color.FromArgb(255, 100, 100));
                }
            }

            if (allMetrics.Count == 0)
            {
                SetStatus("No metrics collected.");
                SetBusy(false);
                return;
            }

            // ---- Phase 2: Confirm before posting ----
            SetStatus("Metrics collected.");
            SetBusy(false);

            var confirm = MessageBox.Show(
                $"Post the server information shown above as a work note to {incidentRaw}?",
                "Confirm submission",
                MessageBoxButtons.YesNo,
                MessageBoxIcon.Question);

            if (confirm != DialogResult.Yes)
            {
                SetStatus("Cancelled.");
                return;
            }

            SetBusy(true);

            // ---- Phase 3: OAuth ----
            string token;
            try
            {
                SetStatus("Opening browser for authentication...");
                token = await OAuthHelper.GetAccessTokenAsync(_config,
                    new Progress<string>(msg => SetStatus(msg)));
                AppendOutput("Authenticated successfully.", Color.FromArgb(100, 220, 100));
            }
            catch (Exception ex)
            {
                AppendOutput($"Authentication failed: {ex.Message}", Color.FromArgb(255, 100, 100));
                SetStatus("Authentication failed.");
                SetBusy(false);
                return;
            }

            // ---- Phase 4: Lookup incident ----
            IncidentRecord incident;
            try
            {
                SetStatus($"Looking up {incidentRaw}...");
                incident = await ServiceNowClient.GetIncidentAsync(_config.InstanceUrl, token, incidentRaw);
                AppendOutput($"Found: {incident.Number} -- {incident.ShortDescription}",
                    Color.FromArgb(100, 220, 100));
            }
            catch (Exception ex)
            {
                AppendOutput($"Incident lookup failed: {ex.Message}", Color.FromArgb(255, 100, 100));
                SetStatus("Incident lookup failed.");
                SetBusy(false);
                return;
            }

            // ---- Phase 5: Build and post work note ----
            try
            {
                SetStatus("Posting work note...");
                var noteParts = new StringBuilder();
                foreach (ServerMetrics m in allMetrics)
                    noteParts.Append(WorkNoteFormatter.Format(m));

                await ServiceNowClient.PostWorkNoteAsync(
                    _config.InstanceUrl, token, incident.SysId, noteParts.ToString());

                AppendOutput($"\nWork note posted to {incident.Number}.", Color.FromArgb(100, 220, 100));
                SetStatus($"Done. Work note posted to {incident.Number}.");
            }
            catch (Exception ex)
            {
                AppendOutput($"Failed to post work note: {ex.Message}", Color.FromArgb(255, 100, 100));
                SetStatus("Failed to post work note.");
            }

            SetBusy(false);
        }

        private void BtnSettings_Click(object? sender, EventArgs e)
        {
            using (var dlg = new SettingsForm(_config))
            {
                dlg.ShowDialog(this);
                _config = AppConfig.Load(); // reload after potential save
            }
        }

        // Compact console-style preview shown in the output area before the user confirms submission
        private static string FormatConsolePreview(ServerMetrics m)
        {
            var sb = new StringBuilder();
            sb.AppendLine($"  Computer : {m.ComputerName}");
            sb.AppendLine($"  CPU Load : {m.CPU?.Load ?? 0}%");
            if (m.Uptime?.LastBoot != null)
                sb.AppendLine($"  Uptime   : {m.Uptime.Days}d {m.Uptime.Hours}h {m.Uptime.Minutes}m  (boot: {m.Uptime.LastBoot:MM/dd/yyyy HH:mm})");
            if (m.Memory?.Error == null)
                sb.AppendLine($"  Memory   : {m.Memory!.UsedGB} / {m.Memory.TotalGB} GB  ({m.Memory.Percent}% used)");
            if (m.Storage?.Error == null)
            {
                foreach (var d in m.Storage!.Drives)
                {
                    string label = string.IsNullOrWhiteSpace(d.Label) ? "No Label" : d.Label;
                    sb.AppendLine($"  Drive {d.Drive,-4} ({label,-12}) {d.UsedGB,7:F2} / {d.TotalGB,7:F2} GB  ({d.Percent}% used)");
                }
            }
            foreach (var n in m.Network)
                sb.AppendLine($"  Network  : {n.Adapter}  {n.IPAddress}");
            sb.AppendLine();
            return sb.ToString();
        }
    }
}
