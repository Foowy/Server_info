using System;
using System.Windows.Forms;

namespace ServerInfoSubmitter
{
    static class Program
    {
        [STAThread]
        static void Main()
        {
            Application.EnableVisualStyles();
            Application.SetCompatibleTextRenderingDefault(false);

            AppConfig config = AppConfig.Load();

            if (!config.IsConfigured)
            {
                using (var setup = new SettingsForm(config, isInitialSetup: true))
                {
                    if (setup.ShowDialog() != DialogResult.OK)
                        return; // user dismissed setup without saving -- exit
                    config = AppConfig.Load();
                }
            }

            Application.Run(new MainForm(config));
        }
    }
}
