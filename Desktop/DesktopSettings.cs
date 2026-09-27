using System;
using System.IO;
using System.Xml;
using System.Xml.Serialization;

namespace RazerBatteryTray.Desktop
{
    public sealed class DesktopPreferences
    {
        public string Language = "system";
        public bool ReducedMotion;
        public bool CloseToTray = true;
        public string Theme = "dark";
        public bool TrayAnimation = true;
        public int LowBatteryThreshold = 20;
        public bool ConnectionNotifications;
        public bool IncludePrereleases;
        public bool AutoCheckUpdates = true;
        public bool AutoDownloadUpdates;
        public bool AutoInstallUpdates;
    }
    internal sealed class DesktopSettings
    {
        private readonly string path;
        public DesktopSettings(string directory) { path = Path.Combine(directory, "desktop.xml"); }
        public DesktopPreferences Load()
        {
            if (!File.Exists(path)) return new DesktopPreferences();
            using (var reader = XmlReader.Create(path, new XmlReaderSettings { DtdProcessing = DtdProcessing.Prohibit, XmlResolver = null, MaxCharactersInDocument = 65536 }))
            {
                var result = (DesktopPreferences)new XmlSerializer(typeof(DesktopPreferences)).Deserialize(reader);
                if (result.Theme != "light") result.Theme = "dark";
                if (result.Language != "zh" && result.Language != "en") result.Language = "system";
                if (result.LowBatteryThreshold != 10 && result.LowBatteryThreshold != 15 && result.LowBatteryThreshold != 20) result.LowBatteryThreshold = 20;
                if (!result.AutoCheckUpdates) result.AutoDownloadUpdates = result.AutoInstallUpdates = false;
                if (!result.AutoDownloadUpdates) result.AutoInstallUpdates = false;
                return result;
            }
        }
        public void Save(DesktopPreferences settings)
        {
            Directory.CreateDirectory(Path.GetDirectoryName(path));
            string temp = path + ".tmp";
            try
            {
                using (var stream = new FileStream(temp, FileMode.Create, FileAccess.Write, FileShare.None))
                { new XmlSerializer(typeof(DesktopPreferences)).Serialize(stream, settings); stream.Flush(true); }
                if (File.Exists(path)) File.Replace(temp, path, path + ".bak"); else File.Move(temp, path);
            }
            finally { if (File.Exists(temp)) File.Delete(temp); }
        }
    }
}
