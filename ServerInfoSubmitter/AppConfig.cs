using System;
using System.IO;
using System.Runtime.Serialization;
using System.Runtime.Serialization.Json;

namespace ServerInfoSubmitter
{
    [DataContract]
    public class AppConfig
    {
        public static readonly string ConfigDir = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "ServerInfoSubmitter");

        private static readonly string ConfigFile = Path.Combine(ConfigDir, "config.json");

        [DataMember(Name = "instanceUrl")]
        public string InstanceUrl { get; set; } = string.Empty;

        [DataMember(Name = "clientId")]
        public string ClientId { get; set; } = string.Empty;

        public bool IsConfigured =>
            !string.IsNullOrWhiteSpace(InstanceUrl) &&
            !string.IsNullOrWhiteSpace(ClientId) &&
            InstanceUrl.StartsWith("https://", StringComparison.OrdinalIgnoreCase);

        public static AppConfig Load()
        {
            if (!File.Exists(ConfigFile))
                return new AppConfig();

            try
            {
                byte[] bytes = File.ReadAllBytes(ConfigFile);
                var ser = new DataContractJsonSerializer(typeof(AppConfig));
                using (var ms = new MemoryStream(bytes))
                    return (AppConfig)ser.ReadObject(ms);
            }
            catch
            {
                return new AppConfig();
            }
        }

        public void Save()
        {
            Directory.CreateDirectory(ConfigDir);
            var ser = new DataContractJsonSerializer(typeof(AppConfig));
            using (var ms = new MemoryStream())
            {
                ser.WriteObject(ms, this);
                File.WriteAllBytes(ConfigFile, ms.ToArray());
            }
        }
    }
}
