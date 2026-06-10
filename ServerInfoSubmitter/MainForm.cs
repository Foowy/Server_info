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
        private string? _token;               // reused within a session; cleared on 401
        private IncidentDetails? _loadedIncident;

        // Top-bar controls
        private TextBox     _tbIncident  = null!;
        private Button      _btnLoad     = null!;
        private Button      _btnSettings = null!;
        private Label       _lblStatus   = null!;
        private ProgressBar _progressBar = null!;

        // Tab control
        private TabControl _tabs = null!;

        // Server Info tab
        private TextBox     _tbServers = null!;
        private Button      _btnRun    = null!;
        private RichTextBox _rtbOutput = null!;

        // Incident tab
        private ListView _lvIncident = null!;

        // CI Info tab
        private ListView _lvCI = null!;

        // CI History tab
        private DataGridView _dgCIHistory = null!;

        // CI Changes tab
        private DataGridView _dgCIChanges = null!;

        public MainForm(AppConfig config)
        {
            _config = config;
            BuildUi();
        }

        // -----------------------------------------------------------------------
        // UI construction
        // -----------------------------------------------------------------------

        private void BuildUi()
        {
            Text            = "Server Info Submitter";
            MinimumSize     = new Size(860, 560);
            StartPosition   = FormStartPosition.CenterScreen;
            ClientSize      = new Size(880, 640);
            Font            = new Font("Segoe UI", 9f);
            FormBorderStyle = FormBorderStyle.Sizable;

            // --- Top panel ---
            var topPanel = new Panel
            {
                Dock   = DockStyle.Top,
                Height = 80,
                Padding = new Padding(8, 8, 8, 4)
            };

            var lblInc = new Label
            {
                Text      = "Incident:",
                Location  = new Point(8, 14),
                Size      = new Size(65, 20),
                TextAlign = ContentAlignment.MiddleRight
            };

            _tbIncident = new TextBox
            {
                Location        = new Point(78, 11),
                Size            = new Size(200, 23),
                CharacterCasing = CharacterCasing.Upper
            };
            WinFormsHelper.SetCueBanner(_tbIncident, "INC0012345");

            _btnLoad = new Button
            {
                Text      = "Load",
                Location  = new Point(286, 10),
                Size      = new Size(70, 26),
                FlatStyle = FlatStyle.Flat,
                BackColor = Color.FromArgb(0, 120, 212),
                ForeColor = Color.White,
                Font      = new Font("Segoe UI", 9f, FontStyle.Bold)
            };
            _btnLoad.FlatAppearance.BorderSize = 0;
            _btnLoad.Click += BtnLoad_Click;

            _btnSettings = new Button
            {
                Text      = "Settings",
                Location  = new Point(364, 10),
                Size      = new Size(88, 26),
                FlatStyle = FlatStyle.Flat
            };
            _btnSettings.Click += BtnSettings_Click;

            _lblStatus = new Label
            {
                Text      = "Enter an incident number and click Load.",
                Location  = new Point(8, 44),
                Size      = new Size(860, 18),
                Anchor    = AnchorStyles.Top | AnchorStyles.Left | AnchorStyles.Right
            };

            _progressBar = new ProgressBar
            {
                Location              = new Point(8, 64),
                Size                  = new Size(860, 10),
                Style                 = ProgressBarStyle.Marquee,
                MarqueeAnimationSpeed = 0,
                Anchor                = AnchorStyles.Top | AnchorStyles.Left | AnchorStyles.Right
            };

            topPanel.Controls.AddRange(new Control[]
            {
                lblInc, _tbIncident, _btnLoad, _btnSettings, _lblStatus, _progressBar
            });

            // --- Tab control ---
            _tabs = new TabControl { Dock = DockStyle.Fill };

            _tabs.TabPages.Add(BuildServerInfoTab());
            _tabs.TabPages.Add(BuildIncidentTab());
            _tabs.TabPages.Add(BuildCITab());
            _tabs.TabPages.Add(BuildCIHistoryTab());
            _tabs.TabPages.Add(BuildCIChangesTab());

            // Add Fill before Top so docking works correctly
            Controls.Add(_tabs);
            Controls.Add(topPanel);
        }

        private TabPage BuildServerInfoTab()
        {
            var page = new TabPage("Server Info");

            var lblSrv = new Label
            {
                Text      = "Servers:",
                Location  = new Point(8, 14),
                Size      = new Size(65, 20),
                TextAlign = ContentAlignment.MiddleRight
            };

            _tbServers = new TextBox
            {
                Location = new Point(78, 11),
                Size     = new Size(580, 23),
                Anchor   = AnchorStyles.Top | AnchorStyles.Left | AnchorStyles.Right
            };
            WinFormsHelper.SetCueBanner(_tbServers, "Server01,Server02  (blank = local machine)");

            var btnHelp = new Button
            {
                Text      = "?",
                Size      = new Size(26, 23),
                Location  = new Point(664, 11),
                FlatStyle = FlatStyle.Flat,
                Anchor    = AnchorStyles.Top | AnchorStyles.Right
            };
            btnHelp.FlatAppearance.BorderColor = SystemColors.ControlDark;
            btnHelp.Click += (s, e) => MessageBox.Show(
                "Enter server names or IP addresses, comma-separated.\r\n\r\n" +
                "Leave blank to query the local machine.\r\n\r\n" +
                "Examples:\r\n  Server01\r\n  Server01, Server02\r\n  10.0.0.15",
                "Servers", MessageBoxButtons.OK, MessageBoxIcon.Information);

            _btnRun = new Button
            {
                Text      = "Collect && Submit",
                Location  = new Point(78, 44),
                Size      = new Size(150, 28),
                BackColor = Color.FromArgb(0, 120, 212),
                ForeColor = Color.White,
                FlatStyle = FlatStyle.Flat,
                Font      = new Font("Segoe UI", 9f, FontStyle.Bold)
            };
            _btnRun.FlatAppearance.BorderSize = 0;
            _btnRun.Click += BtnRun_Click;

            _rtbOutput = new RichTextBox
            {
                Location    = new Point(8, 82),
                Size        = new Size(840, 420),
                ReadOnly    = true,
                BackColor   = Color.FromArgb(30, 30, 30),
                ForeColor   = Color.FromArgb(220, 220, 220),
                Font        = new Font("Consolas", 9f),
                ScrollBars  = RichTextBoxScrollBars.Vertical,
                BorderStyle = BorderStyle.None,
                Anchor      = AnchorStyles.Top | AnchorStyles.Bottom | AnchorStyles.Left | AnchorStyles.Right
            };

            page.Controls.AddRange(new Control[]
            {
                lblSrv, _tbServers, btnHelp, _btnRun, _rtbOutput
            });
            return page;
        }

        private TabPage BuildIncidentTab()
        {
            var page = new TabPage("Incident");
            _lvIncident = BuildPropertyList(page);
            SetPropertyListPlaceholder(_lvIncident, "Load an incident to see its details.");
            return page;
        }

        private TabPage BuildCITab()
        {
            var page = new TabPage("CI Info");
            _lvCI = BuildPropertyList(page);
            SetPropertyListPlaceholder(_lvCI, "Load an incident with a CI to see configuration item details.");
            return page;
        }

        private TabPage BuildCIHistoryTab()
        {
            var page = new TabPage("CI History");

            _dgCIHistory = new DataGridView
            {
                Dock                  = DockStyle.Fill,
                ReadOnly              = true,
                AllowUserToAddRows    = false,
                AllowUserToDeleteRows = false,
                AllowUserToResizeRows = false,
                ColumnHeadersHeightSizeMode = DataGridViewColumnHeadersHeightSizeMode.AutoSize,
                SelectionMode         = DataGridViewSelectionMode.FullRowSelect,
                MultiSelect           = false,
                BackgroundColor       = SystemColors.Window,
                BorderStyle           = BorderStyle.None,
                RowHeadersVisible     = false,
                AutoSizeRowsMode      = DataGridViewAutoSizeRowsMode.AllCells
            };

            _dgCIHistory.Columns.Add(new DataGridViewTextBoxColumn
                { Name = "Number",      HeaderText = "Number",      Width = 100, ReadOnly = true });
            _dgCIHistory.Columns.Add(new DataGridViewTextBoxColumn
                { Name = "Description", HeaderText = "Description", Width = 340, ReadOnly = true,
                  DefaultCellStyle = new DataGridViewCellStyle { WrapMode = DataGridViewTriState.True } });
            _dgCIHistory.Columns.Add(new DataGridViewTextBoxColumn
                { Name = "State",       HeaderText = "State",       Width = 100, ReadOnly = true });
            _dgCIHistory.Columns.Add(new DataGridViewTextBoxColumn
                { Name = "Priority",    HeaderText = "Priority",    Width = 80,  ReadOnly = true });
            _dgCIHistory.Columns.Add(new DataGridViewTextBoxColumn
                { Name = "OpenedAt",    HeaderText = "Opened",      Width = 150, ReadOnly = true });
            _dgCIHistory.Columns.Add(new DataGridViewTextBoxColumn
                { Name = "ResolvedAt",  HeaderText = "Resolved",    Width = 150, ReadOnly = true,
                  AutoSizeMode = DataGridViewAutoSizeColumnMode.Fill });

            page.Controls.Add(_dgCIHistory);
            return page;
        }

        private TabPage BuildCIChangesTab()
        {
            var page = new TabPage("CI Changes");

            _dgCIChanges = new DataGridView
            {
                Dock                  = DockStyle.Fill,
                ReadOnly              = true,
                AllowUserToAddRows    = false,
                AllowUserToDeleteRows = false,
                AllowUserToResizeRows = false,
                ColumnHeadersHeightSizeMode = DataGridViewColumnHeadersHeightSizeMode.AutoSize,
                SelectionMode         = DataGridViewSelectionMode.FullRowSelect,
                MultiSelect           = false,
                BackgroundColor       = SystemColors.Window,
                BorderStyle           = BorderStyle.None,
                RowHeadersVisible     = false,
                AutoSizeRowsMode      = DataGridViewAutoSizeRowsMode.AllCells
            };

            _dgCIChanges.Columns.Add(new DataGridViewTextBoxColumn
                { Name = "Number",      HeaderText = "Number",      Width = 100, ReadOnly = true });
            _dgCIChanges.Columns.Add(new DataGridViewTextBoxColumn
                { Name = "Description", HeaderText = "Description", Width = 280, ReadOnly = true,
                  DefaultCellStyle = new DataGridViewCellStyle { WrapMode = DataGridViewTriState.True } });
            _dgCIChanges.Columns.Add(new DataGridViewTextBoxColumn
                { Name = "Type",        HeaderText = "Type",        Width = 90,  ReadOnly = true });
            _dgCIChanges.Columns.Add(new DataGridViewTextBoxColumn
                { Name = "State",       HeaderText = "State",       Width = 110, ReadOnly = true });
            _dgCIChanges.Columns.Add(new DataGridViewTextBoxColumn
                { Name = "Risk",        HeaderText = "Risk",        Width = 80,  ReadOnly = true });
            _dgCIChanges.Columns.Add(new DataGridViewTextBoxColumn
                { Name = "StartDate",   HeaderText = "Start",       Width = 150, ReadOnly = true });
            _dgCIChanges.Columns.Add(new DataGridViewTextBoxColumn
                { Name = "EndDate",     HeaderText = "End",         Width = 150, ReadOnly = true,
                  AutoSizeMode = DataGridViewAutoSizeColumnMode.Fill });

            page.Controls.Add(_dgCIChanges);
            return page;
        }

        // Creates a two-column ListView (Key / Value) that fills a TabPage
        private static ListView BuildPropertyList(TabPage page)
        {
            var lv = new ListView
            {
                Dock          = DockStyle.Fill,
                View          = View.Details,
                FullRowSelect = true,
                GridLines     = false,
                HeaderStyle   = ColumnHeaderStyle.None,
                MultiSelect   = false,
                HideSelection = false,
                BorderStyle   = BorderStyle.None
            };
            lv.Columns.Add("Key",   160);
            lv.Columns.Add("Value", 660);
            lv.Columns[1].AutoResize(ColumnHeaderAutoResizeStyle.None);
            page.Controls.Add(lv);
            return lv;
        }

        private static void SetPropertyListPlaceholder(ListView lv, string message)
        {
            lv.Items.Clear();
            var item = new ListViewItem(string.Empty);
            item.SubItems.Add(message);
            item.ForeColor = SystemColors.GrayText;
            lv.Items.Add(item);
        }

        private static void PopulatePropertyList(ListView lv, List<(string Key, string Value)> rows)
        {
            lv.BeginUpdate();
            lv.Items.Clear();
            foreach (var (key, value) in rows)
            {
                var item = new ListViewItem(key);
                item.Font = new Font("Segoe UI", 9f, FontStyle.Bold);
                item.SubItems.Add(value);
                lv.Items.Add(item);
            }
            lv.EndUpdate();
        }

        // -----------------------------------------------------------------------
        // State helpers
        // -----------------------------------------------------------------------

        private void SetBusy(bool busy)
        {
            _btnLoad.Enabled              = !busy;
            _btnRun.Enabled               = !busy;
            _btnSettings.Enabled          = !busy;
            _progressBar.MarqueeAnimationSpeed = busy ? 30 : 0;
        }

        private void SetStatus(string text)
        {
            if (InvokeRequired) Invoke(new Action<string>(SetStatus), text);
            else _lblStatus.Text = text;
        }

        private void AppendOutput(string text, Color? color = null)
        {
            _rtbOutput.SelectionStart  = _rtbOutput.TextLength;
            _rtbOutput.SelectionLength = 0;
            _rtbOutput.SelectionColor  = color ?? Color.FromArgb(220, 220, 220);
            _rtbOutput.AppendText(text + "\n");
            _rtbOutput.ScrollToCaret();
        }

        // -----------------------------------------------------------------------
        // Token management
        // -----------------------------------------------------------------------

        private async Task<string> EnsureTokenAsync()
        {
            if (!string.IsNullOrEmpty(_token)) return _token!;
            _token = await OAuthHelper.GetAccessTokenAsync(_config,
                new Progress<string>(SetStatus));
            return _token;
        }

        // Runs an API call; on 401/403 clears the token and retries once with a fresh auth
        private async Task<T> WithAuthAsync<T>(Func<string, Task<T>> apiCall)
        {
            try
            {
                return await apiCall(await EnsureTokenAsync());
            }
            catch (Exception ex)
                when (ex.Message.Contains("401") || ex.Message.Contains("403") ||
                      ex.Message.Contains("Unauthorized") || ex.Message.Contains("Forbidden"))
            {
                _token = null;
                return await apiCall(await EnsureTokenAsync());
            }
        }

        // -----------------------------------------------------------------------
        // Load button -- populates Incident, CI Info, CI History, CI Changes tabs
        // -----------------------------------------------------------------------

        private async void BtnLoad_Click(object? sender, EventArgs e)
        {
            string inc = _tbIncident.Text.Trim().ToUpper();
            if (!System.Text.RegularExpressions.Regex.IsMatch(inc, @"^INC\d+$"))
            {
                MessageBox.Show("Enter a valid incident number (e.g. INC0012345).",
                    "Validation", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                _tbIncident.Focus();
                return;
            }

            SetBusy(true);
            _loadedIncident = null;

            try
            {
                // Load full incident details
                SetStatus($"Loading {inc}...");
                _loadedIncident = await WithAuthAsync(t =>
                    ServiceNowClient.GetIncidentDetailsAsync(_config.InstanceUrl, t, inc));

                PopulateIncidentTab(_loadedIncident);
                _tabs.SelectedTab = _tabs.TabPages[1]; // switch to Incident tab

                // Load CI data if the incident has one
                if (!string.IsNullOrEmpty(_loadedIncident.CmdbCiSysId))
                {
                    SetStatus($"Loading CI: {_loadedIncident.CmdbCiName}...");

                    Task<CIRecord> ciTask =
                        WithAuthAsync(t => ServiceNowClient.GetCIAsync(
                            _config.InstanceUrl, t, _loadedIncident.CmdbCiSysId));

                    Task<List<CIIncidentHistory>> histTask =
                        WithAuthAsync(t => ServiceNowClient.GetCIIncidentHistoryAsync(
                            _config.InstanceUrl, t, _loadedIncident.CmdbCiSysId));

                    Task<List<CIChange>> changesTask =
                        WithAuthAsync(t => ServiceNowClient.GetCIChangesAsync(
                            _config.InstanceUrl, t, _loadedIncident.CmdbCiSysId));

                    await Task.WhenAll(ciTask, histTask, changesTask);

                    PopulateCITab(ciTask.Result);
                    PopulateCIHistoryTab(histTask.Result);
                    PopulateCIChangesTab(changesTask.Result);
                }
                else
                {
                    SetPropertyListPlaceholder(_lvCI, "No CI is associated with this incident.");
                    _dgCIHistory.Rows.Clear();
                    _dgCIChanges.Rows.Clear();
                }

                SetStatus($"Loaded {_loadedIncident.Number} -- {_loadedIncident.ShortDescription}");
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Failed to load incident:\n\n{ex.Message}", "Error",
                    MessageBoxButtons.OK, MessageBoxIcon.Error);
                SetStatus("Load failed.");
            }
            finally
            {
                SetBusy(false);
            }
        }

        // -----------------------------------------------------------------------
        // Tab population helpers
        // -----------------------------------------------------------------------

        private void PopulateIncidentTab(IncidentDetails d)
        {
            PopulatePropertyList(_lvIncident, new List<(string, string)>
            {
                ("Number",           d.Number),
                ("Description",      d.ShortDescription),
                ("State",            d.State),
                ("Priority",         d.Priority),
                ("Severity",         d.Severity),
                ("Urgency",          d.Urgency),
                ("Impact",           d.Impact),
                ("Category",         d.Category),
                ("Subcategory",      d.Subcategory),
                ("Caller",           d.CallerName),
                ("Assigned To",      d.AssignedTo),
                ("Assignment Group", d.AssignmentGroup),
                ("CI",               d.CmdbCiName),
                ("Opened",           d.OpenedAt),
                ("Resolved",         d.ResolvedAt),
                ("Detail",           d.Description)
            });
        }

        private void PopulateCITab(CIRecord ci)
        {
            PopulatePropertyList(_lvCI, new List<(string, string)>
            {
                ("Name",               ci.Name),
                ("Class",              ci.Class),
                ("Manufacturer",       ci.Manufacturer),
                ("Model",              ci.Model),
                ("Operating System",   ci.OperatingSystem),
                ("IP Address",         ci.IPAddress),
                ("Serial Number",      ci.SerialNumber),
                ("Asset Tag",          ci.AssetTag),
                ("Operational Status", ci.OperationalStatus),
                ("Install Status",     ci.InstallStatus),
                ("Location",           ci.Location),
                ("Department",         ci.Department)
            });
        }

        private void PopulateCIHistoryTab(List<CIIncidentHistory> items)
        {
            _dgCIHistory.Rows.Clear();
            foreach (CIIncidentHistory h in items)
            {
                _dgCIHistory.Rows.Add(
                    h.Number, h.ShortDescription, h.State,
                    h.Priority, h.OpenedAt, h.ResolvedAt);
            }
            _tabs.TabPages[3].Text = $"CI History ({items.Count})";
        }

        private void PopulateCIChangesTab(List<CIChange> items)
        {
            _dgCIChanges.Rows.Clear();
            foreach (CIChange c in items)
            {
                _dgCIChanges.Rows.Add(
                    c.Number, c.ShortDescription, c.Type,
                    c.State, c.Risk, c.StartDate, c.EndDate);
            }
            _tabs.TabPages[4].Text = $"CI Changes ({items.Count})";
        }

        // -----------------------------------------------------------------------
        // Server Info tab -- collect metrics and post work note
        // -----------------------------------------------------------------------

        private async void BtnRun_Click(object? sender, EventArgs e)
        {
            string inc = _tbIncident.Text.Trim().ToUpper();
            if (!System.Text.RegularExpressions.Regex.IsMatch(inc, @"^INC\d+$"))
            {
                MessageBox.Show("Enter a valid incident number in the Incident field at the top.",
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
            _tabs.SelectedTab = _tabs.TabPages[0];

            // Collect metrics
            var allMetrics = new List<ServerMetrics>();
            foreach (string server in servers)
            {
                AppendOutput($"=== Collecting from {server} ===",
                    Color.FromArgb(100, 180, 255));
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

            SetStatus("Metrics collected.");
            SetBusy(false);

            if (MessageBox.Show(
                $"Post the server information shown above as a work note to {inc}?",
                "Confirm submission", MessageBoxButtons.YesNo, MessageBoxIcon.Question)
                != DialogResult.Yes)
            {
                SetStatus("Cancelled.");
                return;
            }

            SetBusy(true);

            // Ensure we have the incident sys_id (use loaded incident if available, else load)
            string sysId;
            try
            {
                if (_loadedIncident != null &&
                    string.Equals(_loadedIncident.Number, inc, StringComparison.OrdinalIgnoreCase))
                {
                    sysId = _loadedIncident.SysId;
                }
                else
                {
                    SetStatus($"Looking up {inc}...");
                    IncidentRecord rec = await WithAuthAsync(t =>
                        ServiceNowClient.GetIncidentAsync(_config.InstanceUrl, t, inc));
                    sysId = rec.SysId;
                }
            }
            catch (Exception ex)
            {
                AppendOutput($"Incident lookup failed: {ex.Message}", Color.FromArgb(255, 100, 100));
                SetStatus("Incident lookup failed.");
                SetBusy(false);
                return;
            }

            // Build and post work note
            try
            {
                SetStatus("Posting work note...");
                var note = new StringBuilder();
                foreach (ServerMetrics m in allMetrics)
                    note.Append(WorkNoteFormatter.Format(m));

                await WithAuthAsync<object?>(async t =>
                {
                    await ServiceNowClient.PostWorkNoteAsync(
                        _config.InstanceUrl, t, sysId, note.ToString());
                    return null;
                });

                AppendOutput($"\nWork note posted to {inc}.", Color.FromArgb(100, 220, 100));
                SetStatus($"Done. Work note posted to {inc}.");
            }
            catch (Exception ex)
            {
                AppendOutput($"Failed to post work note: {ex.Message}", Color.FromArgb(255, 100, 100));
                SetStatus("Failed to post work note.");
            }

            SetBusy(false);
        }

        // -----------------------------------------------------------------------
        // Settings
        // -----------------------------------------------------------------------

        private void BtnSettings_Click(object? sender, EventArgs e)
        {
            using (var dlg = new SettingsForm(_config))
            {
                dlg.ShowDialog(this);
                _config = AppConfig.Load();
                _token  = null; // force re-auth after any config change
            }
        }

        // -----------------------------------------------------------------------
        // Formatting
        // -----------------------------------------------------------------------

        private static string FormatConsolePreview(ServerMetrics m)
        {
            var sb = new StringBuilder();
            sb.AppendLine($"  Computer : {m.ComputerName}");
            sb.AppendLine($"  CPU Load : {m.CPU?.Load ?? 0}%");
            if (m.Uptime?.LastBoot != null)
                sb.AppendLine($"  Uptime   : {m.Uptime.Days}d {m.Uptime.Hours}h {m.Uptime.Minutes}m" +
                              $"  (boot: {m.Uptime.LastBoot:MM/dd/yyyy HH:mm})");
            if (m.Memory?.Error == null)
                sb.AppendLine($"  Memory   : {m.Memory!.UsedGB} / {m.Memory.TotalGB} GB" +
                              $"  ({m.Memory.Percent}% used)");
            if (m.Storage?.Error == null)
                foreach (var d in m.Storage!.Drives)
                {
                    string label = string.IsNullOrWhiteSpace(d.Label) ? "No Label" : d.Label;
                    sb.AppendLine($"  Drive {d.Drive,-4} ({label,-12})" +
                                  $"  {d.UsedGB,7:F2} / {d.TotalGB,7:F2} GB  ({d.Percent}% used)");
                }
            foreach (var n in m.Network)
                sb.AppendLine($"  Network  : {n.Adapter}  {n.IPAddress}");
            sb.AppendLine();
            return sb.ToString();
        }
    }
}
