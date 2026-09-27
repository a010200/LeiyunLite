using System;
using System.Threading;
using System.Runtime.InteropServices;
using System.Windows.Forms;

namespace RazerBatteryTray
{
    static class Program
    {
        [DllImport("user32.dll")]
        static extern bool SetProcessDPIAware();

        [DllImport("kernel32.dll")]
        static extern ulong GetTickCount64();

        [STAThread]
        static void Main(string[] args)
        {
            try
            {
                SetProcessDPIAware();
            }
            catch { }

            Application.SetUnhandledExceptionMode(UnhandledExceptionMode.CatchException);
            Application.ThreadException += (s, e) => {
                MessageBox.Show("程序发生异常: " + e.Exception.Message + "\n\n" + e.Exception.StackTrace, Theme.Title + " - 错误提示", MessageBoxButtons.OK, MessageBoxIcon.Error);
            };
            AppDomain.CurrentDomain.UnhandledException += (s, e) => {
                Exception ex = e.ExceptionObject as Exception;
                string msg = ex != null ? (ex.Message + "\n\n" + ex.StackTrace) : "未知系统错误";
                MessageBox.Show("未处理的致命异常: " + msg, Theme.Title + " - 致命错误", MessageBoxButtons.OK, MessageBoxIcon.Error);
            };

            bool isAutoStart = false;
            if (args != null)
            {
                for (int i = 0; i < args.Length; i++)
                {
                    if (args[i] != null && args[i].IndexOf("autostart", StringComparison.OrdinalIgnoreCase) >= 0)
                    {
                        isAutoStart = true;
                        break;
                    }
                }
            }

            // Fallback heuristic: If launched without --autostart, but system was booted within 6 minutes (360,000 ms)
            // and this app is registered in HKCU Run key, check if AutoStartShowUI is NOT 1.
            // This ensures that even if Windows Run key was outdated or stripped parameters, it will stay silent on boot.
            if (!isAutoStart)
            {
                try
                {
                    ulong uptimeMs = GetTickCount64();
                    if (uptimeMs < 360000) // Within 6 minutes of system boot
                    {
                        var autoStart = new AutoStartService(Application.ExecutablePath);
                        if (autoStart.IsEnabled() && !new SettingsStore().Load().AutoStartShowUI)
                            isAutoStart = true;
                    }
                }
                catch { }
            }

            Application.EnableVisualStyles();
            Application.SetCompatibleTextRenderingDefault(false);
            bool created;
            using (var instance = new Mutex(true, @"Local\LeiyunLite.v1", out created))
            {
                if (!created)
                {
                    if (!isAutoStart) MessageBox.Show("雷云lite 已在运行，请从系统托盘打开。", Theme.Title);
                    return;
                }
                try { using (var form = new MainForm(isAutoStart)) Application.Run(form); }
                finally { instance.ReleaseMutex(); }
            }
        }
    }
}
