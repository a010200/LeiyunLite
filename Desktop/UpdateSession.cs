using System;
using System.Diagnostics;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Threading;
using RazerBatteryTray.Updates;
namespace RazerBatteryTray.Desktop
{
    internal sealed class UpdateSession : IDisposable
    {
        private readonly ShellWindow shell;
        private readonly DispatcherTimer timer;
        private CancellationTokenSource operation;
        private bool disposed, autoInstallFailed;
        private DateTime nextCheck = DateTime.UtcNow.AddSeconds(45);
        internal readonly InstallLayout Installation;
        internal ReleaseOffer Offer;
        internal string Downloaded, Job, Status = Ui.T("准备就绪", "Ready");
        internal int Progress;
        internal bool Busy { get { return operation != null; } }
        internal event Action Changed;
        internal UpdateSession(ShellWindow shell)
        {
            this.shell = shell;
            Installation = shell.Demo ? null : InstallLayout.Detect(System.Reflection.Assembly.GetExecutingAssembly().Location);
            timer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(15) };
            timer.Tick += async (s, e) => await Tick();
        }
        internal void Start() { if (!shell.Demo) timer.Start(); }
        internal void Notify() { var handler = Changed; if (handler != null) handler(); }
        internal void SetPolicy(string policy, bool value)
        {
            shell.SaveUpdatePolicy(policy, value);
            if (policy == "check") nextCheck = DateTime.UtcNow.AddSeconds(45);
            if (!value && operation != null) operation.Cancel();
            autoInstallFailed = false; Notify();
        }
        internal async Task Tick()
        {
            if (disposed || Busy || shell.Demo) return;
            if (shell.Preferences.AutoCheckUpdates && DateTime.UtcNow >= nextCheck) {
                await Check(false);
                if (Offer != null && shell.Preferences.AutoDownloadUpdates && !Busy) await Download(false);
            }
            if (!autoInstallFailed && Job != null && shell.Preferences.AutoInstallUpdates && UpdateSafety.IdleFor(TimeSpan.FromMinutes(2)) && shell.UpdateBlockReason() == null) Install(true);
        }
        internal async Task Check(bool manual)
        {
            if (!Begin()) return; Offer = null; Job = null; Downloaded = null; Progress = 0;
            Status = Ui.T("正在检查…", "Checking…"); Notify(); nextCheck = DateTime.UtcNow.AddHours(6);
            try {
                Offer = await new ReleaseUpdateService().Check(shell.Preferences.IncludePrereleases, operation.Token);
                if (disposed) return;
                Status = Offer == null ? Ui.T("当前已是最新兼容版本。", "No newer compatible version.") : Ui.T("发现新版本：", "New version: ") + Offer.Tag;
                if (!manual && Offer != null) shell.Notice(Status);
            } catch (OperationCanceledException) { Status = Ui.T("检查已取消或超时。", "Check cancelled or timed out."); }
              catch (Exception ex) { nextCheck = DateTime.UtcNow.AddMinutes(30); Status = Ui.T("检查失败：", "Check failed: ") + ex.Message; }
            finally { End(); }
        }
        internal async Task Download(bool manual)
        {
            var selected = Offer; if (selected == null || !Begin()) return;
            Downloaded = Job = null; Progress = 0; autoInstallFailed = false;
            Status = Ui.T("正在下载并验证…", "Downloading and verifying…"); Notify();
            try {
                var progress = new Progress<int>(v => { Progress = v; Notify(); });
                if (Installation != null && selected.SignedManifestUrl != null && !selected.Version.Preview) {
                    Job = await new ReleaseUpdateService().DownloadSigned(selected, Installation, progress, operation.Token);
                    Downloaded = Job; Status = Ui.T("签名与文件校验通过，可以安装并重启。", "Signature and file verification passed. Ready to install.");
                } else {
                    if (!manual) { Status = Ui.T("此版本需手动下载或安装，未自动执行。", "Manual installation required; nothing executed."); return; }
                    Downloaded = await new ReleaseUpdateService().Download(selected, progress, operation.Token);
                    Status = Ui.T("便携 ZIP 已校验；请手动解压，或使用安装版。", "Portable ZIP verified; extract manually or use the installer.");
                }
            } catch (OperationCanceledException) { Status = Ui.T("下载已取消或超时。", "Download cancelled or timed out."); }
              catch (Exception ex) { Status = Ui.T("下载/签名验证失败：", "Download/signature verification failed: ") + ex.Message; }
            finally { End(); }
        }
        internal void Install(bool automatic)
        {
            if (disposed || Busy || Job == null || Installation == null || shell.Demo) return;
            string blocked = shell.UpdateBlockReason();
            if (blocked != null) { Status = blocked; Notify(); return; }
            try {
                Installation.VerifyVersion(Installation.Read().Current, UpdateTrust.PublicKey);
                var m = UpdatePackage.Verify(InstallLayout.ReadBounded(Path.Combine(Job, "update.json")), UpdateTrust.PublicKey);
                if (Version.Parse(m.Version) <= Version.Parse(AppVersion.Number)) throw new InvalidDataException("Update is not newer.");
                string helper = Path.Combine(Installation.VersionPath(AppVersion.Number), "LeiyunLite.Updater.exe");
                using (var own = Process.GetCurrentProcess()) {
                    var info = new ProcessStartInfo(helper, "--apply " + Quote(Installation.Root) + " " + Quote(Job) + " " + own.Id + " " + own.StartTime.ToUniversalTime().Ticks + " " + (!shell.IsVisible ? "hidden" : "visible")) { UseShellExecute = false, CreateNoWindow = true, WorkingDirectory = Installation.Root };
                    shell.ExitForUpdate(() => Process.Start(info));
                }
            } catch (Exception ex) { autoInstallFailed = automatic; Status = Ui.T("无法开始安装：", "Cannot start installation: ") + ex.Message; Notify(); }
        }
        private static string Quote(string path) { if (path.Contains("\"") || path.EndsWith("\\")) throw new ArgumentException("Invalid path."); return "\"" + path + "\""; }
        private bool Begin() { if (disposed || Busy) return false; if (shell.Demo) { Status = Ui.T("演示模式不联网、不安装。", "Demo mode does not connect or install."); Notify(); return false; } operation = new CancellationTokenSource(TimeSpan.FromMinutes(5)); return true; }
        private void End() { operation.Dispose(); operation = null; if (!disposed) Notify(); }
        internal void Cancel() { if (operation != null) operation.Cancel(); }
        public void Dispose() { disposed = true; timer.Stop(); if (operation != null) operation.Cancel(); Changed = null; }
    }
}
