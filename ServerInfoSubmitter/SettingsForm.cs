using System;
using System.Drawing;
using System.Windows.Forms;

namespace ServerInfoSubmitter
{
    public class SettingsForm : Form
    {
        private readonly bool _isInitialSetup;
        private TextBox _tbInstanceUrl = null!;
        private TextBox _tbClientId    = null!;

        public SettingsForm(AppConfig config, bool isInitialSetup = false)
        {
            _isInitialSetup = isInitialSetup;
            BuildUi(config);
        }

        private void BuildUi(AppConfig config)
        {
            Text            = "Settings";
            FormBorderStyle = FormBorderStyle.FixedDialog;
            MaximizeBox     = false;
            MinimizeBox     = false;
            StartPosition   = FormStartPosition.CenterParent;
            ClientSize      = new Size(500, _isInitialSetup ? 290 : 260);
            Font            = new Font("Segoe UI", 9f);

            int y = 12;

            if (_isInitialSetup)
            {
                var banner = new Label
                {
                    Text      = "Configure your ServiceNow connection before continuing.",
                    ForeColor = Color.FromArgb(0, 90, 160),
                    Font      = new Font("Segoe UI", 9f, FontStyle.Bold),
                    Location  = new Point(12, y),
                    Size      = new Size(476, 20),
                    TextAlign = ContentAlignment.MiddleLeft
                };
                Controls.Add(banner);
                y += 30;
            }

            // Instance URL
            AddLabel("ServiceNow Instance URL:", 12, y);
            y += 22;
            _tbInstanceUrl = new TextBox
            {
                Location = new Point(12, y),
                Size     = new Size(476, 23),
                Text     = config.InstanceUrl
            };
            WinFormsHelper.SetCueBanner(_tbInstanceUrl, "https://yourcompany.service-now.com");
            Controls.Add(_tbInstanceUrl);
            y += 32;

            AddLabel("OAuth Client ID  (from System OAuth > Application Registry):", 12, y);
            y += 22;
            _tbClientId = new TextBox
            {
                Location = new Point(12, y),
                Size     = new Size(476, 23),
                Text     = config.ClientId
            };
            WinFormsHelper.SetCueBanner(_tbClientId, "xxxxxxxx-xxxx-xxxx-xxxx-xxxxxxxxxxxx");
            Controls.Add(_tbClientId);
            y += 40;

            // Help text
            var hint = new Label
            {
                Text      = "The OAuth app must be registered in ServiceNow with grant type\r\n" +
                            "\"Authorization Code\" and redirect URI: http://localhost:9875/callback",
                ForeColor = SystemColors.GrayText,
                Location  = new Point(12, y),
                Size      = new Size(476, 36),
            };
            Controls.Add(hint);
            y += 50;

            // Buttons
            var btnSave = new Button
            {
                Text     = "Save",
                Size     = new Size(88, 28),
                Location = new Point(ClientSize.Width - 200, y),
                DialogResult = DialogResult.None
            };
            btnSave.Click += BtnSave_Click;

            var btnCancel = new Button
            {
                Text         = "Cancel",
                Size         = new Size(88, 28),
                Location     = new Point(ClientSize.Width - 104, y),
                DialogResult = _isInitialSetup ? DialogResult.Cancel : DialogResult.Cancel
            };

            Controls.AddRange(new Control[] { btnSave, btnCancel });
            AcceptButton = btnSave;
            CancelButton = btnCancel;

            if (_isInitialSetup)
                FormClosing += (s, e) =>
                {
                    if (DialogResult != DialogResult.OK)
                        DialogResult = DialogResult.Cancel;
                };
        }

        private void BtnSave_Click(object? sender, EventArgs e)
        {
            string url      = _tbInstanceUrl.Text.Trim();
            string clientId = _tbClientId.Text.Trim();

            if (string.IsNullOrWhiteSpace(url) || !url.StartsWith("https://", StringComparison.OrdinalIgnoreCase))
            {
                MessageBox.Show("Instance URL must start with https://", "Validation",
                    MessageBoxButtons.OK, MessageBoxIcon.Warning);
                _tbInstanceUrl.Focus();
                return;
            }

            if (string.IsNullOrWhiteSpace(clientId))
            {
                MessageBox.Show("Client ID cannot be empty.", "Validation",
                    MessageBoxButtons.OK, MessageBoxIcon.Warning);
                _tbClientId.Focus();
                return;
            }

            var config = new AppConfig { InstanceUrl = url, ClientId = clientId };
            try
            {
                config.Save();
                DialogResult = DialogResult.OK;
                Close();
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Failed to save settings:\n{ex.Message}", "Error",
                    MessageBoxButtons.OK, MessageBoxIcon.Error);
            }
        }

        private void AddLabel(string text, int x, int y)
        {
            Controls.Add(new Label
            {
                Text     = text,
                Location = new Point(x, y),
                Size     = new Size(476, 18),
                AutoSize = false
            });
        }
    }
}
