using System;
using System.Runtime.InteropServices;
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
        [DllImport("kernel32.dll")] private static extern ulong GetTickCount64();
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
        internal static ulong SystemUptimeMilliseconds
        {
            get { try { return GetTickCount64(); } catch { return ulong.MaxValue; } }
        }
        internal static bool ShouldStartHidden(bool explicitAutoStart, bool updateHidden, bool showAtSignIn,
            bool registeredAtSignIn, ulong uptimeMilliseconds)
        {
            if (updateHidden) return true;
            if (showAtSignIn) return false;
            // Older registrations did not always carry --autostart. During the first
            // minutes after boot, a registered launch is therefore treated as silent.
            return explicitAutoStart || registeredAtSignIn && uptimeMilliseconds < 360000;
        }
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
