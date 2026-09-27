using System;
using Microsoft.Win32;

namespace RazerBatteryTray
{
    internal interface IAutoStartService
    {
        bool IsEnabled();
        void Sync();
        void SetEnabled(bool enabled);
    }

    internal sealed class AutoStartService : IAutoStartService
    {
        private const string AppName = "RazerBatteryTray";
        private readonly string executablePath;
        private readonly string runKey;
        internal AutoStartService(string executablePath,
            string runKey = @"Software\Microsoft\Windows\CurrentVersion\Run")
        {
            this.executablePath = executablePath;
            this.runKey = runKey;
        }
        public bool IsEnabled()
        {
            try
            {
                using (var key = Registry.CurrentUser.OpenSubKey(runKey, false))
                    return key != null && key.GetValue(AppName) != null;
            }
            catch { return false; }
        }
        private string Command { get { return "\"" + executablePath + "\" --autostart"; } }
        internal bool IsCurrentExecutableEnabled()
        {
            try { using (var key = Registry.CurrentUser.OpenSubKey(runKey, false))
                return key != null && string.Equals((key.GetValue(AppName) as string ?? "").Trim(), Command, StringComparison.OrdinalIgnoreCase); }
            catch { return false; }
        }
        public void Sync()
        {
            try
            {
                using (var key = Registry.CurrentUser.OpenSubKey(runKey, true))
                {
                    if (key == null) return;
                    string value = key.GetValue(AppName) as string;
                    if (!string.IsNullOrEmpty(value) && !string.Equals(value.Trim(), Command, StringComparison.OrdinalIgnoreCase))
                        key.SetValue(AppName, Command);
                }
            }
            catch { }
        }
        public void SetEnabled(bool enabled)
        {
            using (var key = Registry.CurrentUser.OpenSubKey(runKey, true))
            {
                if (key == null) return;
                if (enabled) key.SetValue(AppName, Command);
                else key.DeleteValue(AppName, false);
            }
        }
    }
}
