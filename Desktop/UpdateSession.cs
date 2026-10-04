using System;
using System.Diagnostics;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Threading;
using RazerBatteryTray.Updates;
namespace RazerBatteryTray.Desktop
{
    // UI orchestration seam. The production adapter delegates to the existing verification code.
    internal interface IReleaseUpdateSource
    {
        Task<ReleaseOffer> Check(bool previews, CancellationToken token);
        Task<string> Download(ReleaseOffer offer, IProgress<int> progress, CancellationToken token);
        Task<string> DownloadSigned(ReleaseOffer offer, InstallLayout installation, IProgress<int> progress, CancellationToken token);
    }
    internal sealed class ReleaseUpdateSource : IReleaseUpdateSource
    {
        private readonly ReleaseUpdateService service = new ReleaseUpdateService();
        public Task<ReleaseOffer> Check(bool previews, CancellationToken token) { return service.Check(previews, token); }
        public Task<string> Download(ReleaseOffer offer, IProgress<int> progress, CancellationToken token) { return service.Download(offer, progress, token); }
        public Task<string> DownloadSigned(ReleaseOffer offer, InstallLayout installation, IProgress<int> progress, CancellationToken token) { return service.DownloadSigned(offer, installation, progress, token); }
    }
    internal sealed class UpdateSession : IDisposable
    {
        private readonly ShellWindow shell;
        private readonly DispatcherTimer timer;
        private CancellationTokenSource operation;
        private CancellationTokenSource preparation;
        private readonly IReleaseUpdateSource source;
        private readonly bool testSource;
        private readonly UpdateCheckState checkState;
        private readonly Func<DateTime> utcNow;
        private bool disposed, autoInstallFailed;
        internal readonly InstallLayout Installation;
        internal ReleaseOffer Offer;
        internal string Downloaded, Job, Status = Ui.T("准备就绪", "Ready");
        internal int Progress;
        internal bool Busy { get { return operation != null || preparation != null; } }
        internal event Action Changed;
        internal UpdateSession(ShellWindow shell, IReleaseUpdateSource source = null, InstallLayout installation = null, UpdateCheckState checkState = null, Func<DateTime> utcNow = null)
        {
            this.shell = shell;
            this.source = source ?? new ReleaseUpdateSource(); testSource = source != null;
            this.utcNow = utcNow ?? (() => DateTime.UtcNow);
            this.checkState = checkState ?? new UpdateCheckState(shell.Demo || testSource ? null : Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "LeiyunLite"));
            this.checkState.Load(this.utcNow());
            Installation = installation ?? (shell.Demo ? null : InstallLayout.Detect(System.Reflection.Assembly.GetExecutingAssembly().Location));
            timer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(15) };
            timer.Tick += async (s, e) => await Tick();
        }
        internal void Start() { if (!shell.Demo) timer.Start(); }
        internal void Notify() { var handler = Changed; if (handler != null) handler(); }
        internal void SetPolicy(string policy, bool value)
        {
            shell.SaveUpdatePolicy(policy, value);
            if (policy == "check") checkState.ScheduleStartup(utcNow());
            if (!value) Cancel();
            autoInstallFailed = false; Notify();
        }
        internal async Task Tick()
        {
            if (disposed || Busy || shell.Demo) return;
            if (shell.Preferences.AutoCheckUpdates && checkState.CanCheck(false, utcNow())) {
                await Check(false);
                if (Offer != null && shell.Preferences.AutoDownloadUpdates && !Busy) await Download(false);
            }
            if (!autoInstallFailed && Job != null && shell.Preferences.AutoInstallUpdates && UpdateSafety.IdleFor(TimeSpan.FromMinutes(2)) && shell.UpdateBlockReason() == null) Install(true);
        }
        internal async Task Check(bool manual)
        {
            if (preparation != null) return;
            await CheckCore(manual);
        }
        internal async Task CheckAndPrepareUpdate()
        {
            if (disposed || Busy) return;
            preparation = new CancellationTokenSource(); Notify();
            try {
                await CheckCore(true);
                if (!disposed && !preparation.IsCancellationRequested && Offer != null && Installation != null) await DownloadCore(true);
            } finally { preparation.Dispose(); preparation = null; if (!disposed) Notify(); }
        }
        private async Task CheckCore(bool manual)
        {
            if (disposed || operation != null) return;
            if (!checkState.CanCheck(manual, utcNow())) {
                if (manual) {
                    Offer = null; Job = null; Downloaded = null; Progress = 0;
                    Status = RateLimitStatus(); Notify();
                }
                return;
            }
            if (!Begin()) return; Offer = null; Job = null; Downloaded = null; Progress = 0;
            Status = Ui.T("正在检查…", "Checking…"); Notify();
            try {
                var offer = await source.Check(shell.Preferences.IncludePrereleases, operation.Token);
                operation.Token.ThrowIfCancellationRequested(); Offer = offer;
                if (disposed) return;
                checkState.Succeeded(utcNow());
                Status = Offer == null ? Ui.T("当前已是最新兼容版本。", "No newer compatible version.") : Ui.T("发现新版本：", "New version: ") + Offer.Tag;
                if (!manual && Offer != null) shell.Notice(Status);
            } catch (GitHubRateLimitException ex) { checkState.RateLimited(ex, utcNow()); Status = RateLimitStatus(); }
              catch (OperationCanceledException) { checkState.Failed(utcNow()); Status = Ui.T("检查已取消或超时。", "Check cancelled or timed out."); }
              catch (Exception ex) { checkState.Failed(utcNow()); Status = Ui.T("检查失败：", "Check failed: ") + ex.Message; }
            finally { End(); }
        }
        private string RateLimitStatus()
        {
            DateTime now = utcNow().ToLocalTime(), until = checkState.RateLimitedUntilUtc.ToLocalTime();
            string time = until.ToString(until.Date == now.Date ? "HH:mm" : "MM-dd HH:mm", System.Globalization.CultureInfo.CurrentCulture);
            return Ui.T("GitHub 更新接口当前请求较多，暂时无法检查更新。预计可在 ", "GitHub update requests are rate limited. Try again after ") + time + Ui.T(" 后重试。", ".");
        }
        internal async Task Download(bool manual)
        {
            if (preparation != null) return;
            await DownloadCore(manual);
        }
        private async Task DownloadCore(bool manual)
        {
            var selected = Offer; if (selected == null || !Begin()) return;
            Downloaded = Job = null; Progress = 0; autoInstallFailed = false;
            Status = Ui.T("正在下载并验证…", "Downloading and verifying…"); Notify();
            try {
                var progress = new Progress<int>(v => { Progress = v; Notify(); });
                if (Installation != null && selected.SignedManifestUrl != null && !selected.Version.Preview) {
                    var job = await source.DownloadSigned(selected, Installation, progress, operation.Token);
                    operation.Token.ThrowIfCancellationRequested(); if (disposed) return; Job = job;
                    Downloaded = Job; Progress = 100; Status = selected.Tag + Ui.T(" 已准备好 · 签名与文件校验通过", " ready · signature and file verification passed");
                } else {
                    if (!manual) { Status = Ui.T("此版本需手动下载或安装，未自动执行。", "Manual installation required; nothing executed."); return; }
                    var downloaded = await source.Download(selected, progress, operation.Token);
                    operation.Token.ThrowIfCancellationRequested(); if (disposed) return; Downloaded = downloaded; Progress = 100;
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
        private bool Begin() { if (disposed || operation != null) return false; if (shell.Demo && !testSource) { Status = Ui.T("演示模式不联网、不安装。", "Demo mode does not connect or install."); Notify(); return false; } operation = preparation == null ? new CancellationTokenSource() : CancellationTokenSource.CreateLinkedTokenSource(preparation.Token); operation.CancelAfter(TimeSpan.FromMinutes(5)); return true; }
        private void End() { operation.Dispose(); operation = null; if (!disposed) Notify(); }
        internal void Cancel() { if (preparation != null) preparation.Cancel(); if (operation != null) operation.Cancel(); }
        public void Dispose() { disposed = true; timer.Stop(); Cancel(); Changed = null; }
    }
}
