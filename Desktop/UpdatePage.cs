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
            check = Ui.Button(session.Installation == null ? Ui.T("检查新版", "Check for updates") : Ui.T("检查并更新", "Check and prepare update"), async () => { if (session.Installation == null) await session.Check(true); else await session.CheckAndPrepareUpdate(); }, true);
            download = Ui.Button(Ui.T("下载并验证", "Download and verify"), async () => await session.Download(true), true);
            install = Ui.Button(Ui.T("更新并重启", "Update and restart"), () => {
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
                    Ui.Row(check, cancel), progress, status, automaticCheck,
                    automaticDownload, automaticInstall, previews)),
                Ui.Card(Ui.Stack(notes, Ui.Row(download, install), reveal,
                    Ui.Button(Ui.T("打开发布页面", "Open release page"), () => Open(session.Offer == null ? ReleaseUpdateService.ReleasesPage : session.Offer.Page, null))))));
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
                status.Text = session.Status; notes.Text = session.Offer == null ? "" : Ui.T("更新内容", "What's new") + "\n\n" + ReleaseNotesFormatter.Format(session.Offer.Notes);
                progress.Value = session.Progress; progress.IsIndeterminate = session.Busy && session.Progress == 0;
                progress.Visibility = session.Busy || session.Progress > 0 ? Visibility.Visible : Visibility.Collapsed;
                check.IsEnabled = !session.Busy; download.IsEnabled = !session.Busy && session.Offer != null;
                check.Visibility = session.Job != null ? Visibility.Collapsed : Visibility.Visible;
                download.Content = Ui.T("下载新版", "Download new version");
                download.Visibility = session.Offer != null && session.Downloaded == null && (session.Installation == null || session.Offer.SignedManifestUrl == null || session.Offer.Version.Preview) ? Visibility.Visible : Visibility.Collapsed;
                install.IsEnabled = !session.Busy && session.Job != null; install.Visibility = session.Job == null ? Visibility.Collapsed : Visibility.Visible;
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
