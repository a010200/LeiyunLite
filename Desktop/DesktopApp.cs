using System;
using System.Globalization;
using System.IO;
using System.Reflection;
using System.Threading;
using System.Windows;
using System.Windows.Interop;
using System.Windows.Markup;
using System.Xml;

namespace RazerBatteryTray.Desktop
{
    internal static class DesktopApp
    {
        internal static string TrialToken;
        internal static bool UpdateHidden;
        internal static void LoadTheme(Application app)
        {
            Ui.InstallResources(app);
            ElasticSwitch.Install();
            using (var stream = Assembly.GetExecutingAssembly().GetManifestResourceStream("Fluent.xaml"))
            using (var reader = XmlReader.Create(stream))
                app.Resources.MergedDictionaries.Add((ResourceDictionary)XamlReader.Load(reader));
        }
        [STAThread]
        private static void Main(string[] args)
        {
            bool demo = Array.IndexOf(args, "--demo") >= 0;
            UpdateHidden = Array.IndexOf(args, "--update-hidden") >= 0;
            int trial = Array.IndexOf(args, "--update-trial");
            if (!demo && trial < 0) {
                try {
                    var installed = RazerBatteryTray.Updates.InstallLayout.Detect(Assembly.GetExecutingAssembly().Location);
                    if (installed != null && installed.Read().Current != AppVersion.Number) { System.Diagnostics.Process.Start(installed.Launcher); return; }
                } catch (Exception ex) { MessageBox.Show(ex.Message, "雷云lite — 安装状态异常"); return; }
            }
            if (trial >= 0) {
                try {
                    if (demo || trial + 1 >= args.Length) return;
                    TrialToken = args[trial + 1];
                    var layout = RazerBatteryTray.Updates.InstallLayout.Detect(Assembly.GetExecutingAssembly().Location);
                    var state = layout.Read();
                    if (state.Token != TrialToken || state.Pending != AppVersion.Number) return;
                } catch { return; }
            }
            bool created;
            using (var mutex = new Mutex(true, demo ? "Local\\LeiyunLite.Desktop.Preview" : "Local\\LeiyunLite.v1", out created))
            {
                if (!created) { if (TrialToken == null) MessageBox.Show("雷云lite 已在运行。请从托盘退出旧版后再启动新版；也可使用 --demo 安全预览界面。", "雷云lite"); return; }
                try
                {
                    var app = new Application { ShutdownMode = ShutdownMode.OnMainWindowClose };
                    LoadTheme(app);
                    var window = new ShellWindow(demo, Array.IndexOf(args, "--autostart") >= 0);
                    app.MainWindow = window;
                    app.Startup += async (s, e) => {
                        try
                        {
                            // Create the message-only owner HWND without revealing the main
                            // window. Device notifications and the tray icon can then start
                            // normally during a silent sign-in launch.
                            new WindowInteropHelper(window).EnsureHandle();
                            if (!window.StartHidden) window.Show();
                            await window.InitializeRuntime();
                        }
                        catch (Exception ex)
                        {
                            if (TrialToken == null) MessageBox.Show(ex.ToString(), "雷云lite — 启动失败 / Startup failed");
                            window.AbortStartup();
                        }
                    };
                    app.Run();
                }
                catch (Exception ex) { if (TrialToken == null) MessageBox.Show(ex.ToString(), "雷云lite — 启动失败 / Startup failed"); }
                finally { mutex.ReleaseMutex(); }
            }
        }
    }
}
