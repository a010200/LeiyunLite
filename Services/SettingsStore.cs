using Microsoft.Win32;

namespace RazerBatteryTray
{
    internal interface ISettingsStore
    {
        AppSettings Load();
        void Save(AppSettings settings);
    }

    internal sealed class SettingsStore : ISettingsStore
    {
        private readonly string keyPath;
        private readonly bool throwOnSave;
        internal SettingsStore(string keyPath = @"Software\RazerBatteryTray", bool throwOnSave = false) { this.keyPath = keyPath; this.throwOnSave = throwOnSave; }

        public AppSettings Load()
        {
            var settings = new AppSettings();
            try
            {
                using (var key = Registry.CurrentUser.OpenSubKey(keyPath))
                {
                    if (key == null) return settings;
                    settings.LowBatteryAlert = (int)key.GetValue("LowBatteryAlert", 1) == 1;
                    settings.DpiOsdAlert = (int)key.GetValue("DpiOsdAlert", 1) == 1;
                    settings.AutoStartShowUI = (int)key.GetValue("AutoStartShowUI", 0) == 1;
                    settings.TrayIconStyle = (int)key.GetValue("TrayIconStyle", 0);
                    if (settings.TrayIconStyle < 0 || settings.TrayIconStyle > 1) settings.TrayIconStyle = 0;
                    settings.OsdStyle = (int)key.GetValue("OsdStyle", 0);
                    if (settings.OsdStyle < 0 || settings.OsdStyle > 2) settings.OsdStyle = 0;
                    settings.RefreshInterval = (int)key.GetValue("RefreshInterval", 60000);
                    if (settings.RefreshInterval != 30000 && settings.RefreshInterval != 60000 && settings.RefreshInterval != 300000)
                        settings.RefreshInterval = 60000;
                }
            }
            catch { }
            return settings;
        }

        public void Save(AppSettings settings)
        {
            try
            {
                using (var key = Registry.CurrentUser.CreateSubKey(keyPath))
                {
                    if (key == null) { if (throwOnSave) throw new System.IO.IOException("无法保存设置 / Unable to save preferences."); return; }
                    key.SetValue("LowBatteryAlert", settings.LowBatteryAlert ? 1 : 0);
                    key.SetValue("DpiOsdAlert", settings.DpiOsdAlert ? 1 : 0);
                    key.SetValue("AutoStartShowUI", settings.AutoStartShowUI ? 1 : 0);
                    key.SetValue("TrayIconStyle", settings.TrayIconStyle);
                    key.SetValue("OsdStyle", settings.OsdStyle);
                    key.SetValue("RefreshInterval", settings.RefreshInterval);
                }
            }
            catch { if (throwOnSave) throw; }
        }
    }
}
