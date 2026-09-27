using System;
using System.Diagnostics;
using System.Windows;
using System.Windows.Controls;
namespace RazerBatteryTray.Desktop
{
    internal sealed class UpdatePage : UserControl, IDisposable
    {
        private readonly ShellWindow shell;
        private readonly UpdateSession session;
        private readonly TextBlock status = Ui.Text("", 16), notes = Ui.Text("", 14, Ui.Muted);
        private readonly Button check, download, cancel, reveal, install;
        private readonly CheckBox previews, automaticCheck, automaticDownload, automaticInstall;
        private readonly ProgressBar progress = new ProgressBar { Minimum = 0, Maximum = 100, Height = 4, Foreground = Ui.Accent, Background = Ui.Border, BorderThickness = new Thickness(0), Margin = new Thickness(0, 12, 0, 16) };
        private bool syncing;
        internal UpdatePage(ShellWindow shell)
        {
            this.shell = shell; session = shell.Updates;
            check = Ui.Button(Ui.T("检查更新", "Check for updates"), async () => await session.Check(true), true);
            download = Ui.Button(Ui.T("下载并验证", "Download and verify"), async () => await session.Download(true), true);
            install = Ui.Button(Ui.T("安装并重启雷云lite", "Install and restart LeiyunLite"), () => {
                var block = shell.UpdateBlockReason();
                if (block != null) { shell.Notice(block); return; }
                if (MessageBox.Show(Ui.T("将退出雷云lite、安装已验证的新版并重新启动。不会重启电脑。继续吗？", "Exit LeiyunLite, install the verified update and restart the app? Windows will not restart."), AppVersion.DisplayName, MessageBoxButton.OKCancel, MessageBoxImage.Information) == MessageBoxResult.OK) session.Install(false);
            }, true);
            cancel = Ui.Button(Ui.T("取消下载 / 检查", "Cancel"), session.Cancel);
            reveal = Ui.Button(Ui.T("打开下载位置", "Open download location"), () => { if (session.Downloaded != null) Open("explorer.exe", "/select,\"" + session.Downloaded + "\""); });
            previews = Ui.Toggle(Ui.T("接收预发布版本（仅手动下载）", "Include prereleases (manual download only)"), shell.Preferences.IncludePrereleases, b => {
                if (syncing) return; session.Cancel(); shell.SaveUpdateChannel(b); session.Offer = null; session.Job = null; session.Notify();
            });
            automaticCheck = Policy(Ui.T("自动检查更新", "Check automatically"), "check", shell.Preferences.AutoCheckUpdates);
            automaticDownload = Policy(Ui.T("自动下载已签名更新", "Download signed updates automatically"), "download", shell.Preferences.AutoDownloadUpdates);
            automaticInstall = Policy(Ui.T("空闲时自动安装并重启", "Install and restart when idle"), "install", shell.Preferences.AutoInstallUpdates);
            Content = Ui.Scroll(Ui.Stack(Ui.Text(Ui.T("自动更新", "Updates"), 32),
                Ui.Card(Ui.Stack(Ui.Text(AppVersion.DisplayName, 24), Ui.Text("a010200 / LeiyunLite · GitHub Releases", 13, Ui.Muted),
                    Ui.Text(session.Installation == null ? Ui.T("便携版：支持下载；安装版可一键升级。", "Portable: download only. Install for one-click upgrades.") : Ui.T("安装版：签名验证、配置备份、失败回退。", "Installed: signed updates, configuration backup, rollback."), 13, Ui.Muted),
                    automaticCheck, automaticDownload, automaticInstall, previews,
                    Ui.Text(Ui.T("自动安装仅在至少2分钟无键鼠操作、无宏运行/录制、无未保存修改时进行。", "Automatic installation waits for 2 minutes of inactivity, no recording/playback and no unsaved changes."), 12, Ui.Muted),
                    Ui.Row(check, cancel), progress, status)),
                Ui.Card(Ui.Stack(notes, Ui.Row(download, install), reveal,
                    Ui.Button(Ui.T("打开发布页面", "Open release page"), () => Open(session.Offer == null ? ReleaseUpdateService.ReleasesPage : session.Offer.Page, null)),
                    Ui.Text(Ui.T("更新包必须通过项目签名与文件校验。便携ZIP不会自动执行。失败时保留旧版和配置备份，不强制结束程序。", "Installation requires the project signature and file verification. Portable ZIPs are never executed. Failures retain the old version and backups; no forced process termination."), 13, Ui.Muted),
                    Ui.Text(Ui.T("项目更新签名不等于Windows代码签名；软件目前没有已验证发布者证书，请勿关闭系统防护。", "Project signatures are not Windows code signing. The app has no verified publisher certificate; do not disable system protections."), 12, Ui.Muted)))));
            session.Changed += Refresh; Refresh();
        }
        private CheckBox Policy(string label, string name, bool value)
        {
            return Ui.Toggle(label, value, b => { if (syncing) return; try { session.SetPolicy(name, b); } catch (Exception ex) { shell.Notice(ex.Message); Refresh(); } });
        }
        private void Refresh()
        {
            syncing = true;
            try {
                status.Text = session.Status; notes.Text = session.Offer == null ? "" : session.Offer.Published + "\n\n" + session.Offer.Notes;
                progress.Value = session.Progress; progress.IsIndeterminate = session.Busy && session.Progress == 0;
                progress.Visibility = session.Busy || session.Progress > 0 ? Visibility.Visible : Visibility.Collapsed;
                check.IsEnabled = !session.Busy; download.IsEnabled = !session.Busy && session.Offer != null;
                install.IsEnabled = !session.Busy && session.Job != null; install.Visibility = session.Installation == null ? Visibility.Collapsed : Visibility.Visible;
                cancel.Visibility = session.Busy ? Visibility.Visible : Visibility.Collapsed; reveal.Visibility = session.Downloaded == null ? Visibility.Collapsed : Visibility.Visible;
                previews.IsEnabled = !session.Busy;
                automaticCheck.IsChecked = shell.Preferences.AutoCheckUpdates; automaticDownload.IsChecked = shell.Preferences.AutoDownloadUpdates; automaticInstall.IsChecked = shell.Preferences.AutoInstallUpdates;
                automaticDownload.IsEnabled = automaticInstall.IsEnabled = session.Installation != null;
            } finally { syncing = false; }
        }
        private void Open(string target, string args) { try { Process.Start(new ProcessStartInfo(target, args ?? "") { UseShellExecute = true }); } catch (Exception ex) { status.Text = ex.Message; } }
        public void Dispose() { session.Changed -= Refresh; }
    }
}
