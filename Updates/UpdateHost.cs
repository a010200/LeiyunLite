using System;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Threading;
using System.Windows.Forms;
using Microsoft.Win32;
namespace RazerBatteryTray.Updates
{
    internal static class UpdateHost
    {
        [STAThread]
        private static int Main(string[] args)
        {
            InstallLayout layout = null; bool restartOnFailure = false; Mutex gate = null;
            try {
                string own = System.Reflection.Assembly.GetExecutingAssembly().Location;
                bool launcher = Path.GetFileName(own).Equals("LeiyunLite.exe", StringComparison.OrdinalIgnoreCase);
                if (launcher) {
                    layout = new InstallLayout(Path.GetDirectoryName(own));
                    if (args.Length == 2 && args[0] == "--preflight") {
                        EnsureAppStopped(); var before = layout.Read(); UpdatePackage.RequireVersion(args[1]);
                        if (before.Pending != null || Version.Parse(args[1]) < Version.Parse(before.Current)) throw new IOException("Pending update or installer downgrade.");
                        layout.VerifyVersion(before.Current, UpdateTrust.PublicKey); return 0;
                    }
                    gate = layout.Lock();
                    if (args.Length == 1 && args[0] == "--uninstall-clean") { EnsureAppStopped(); UpdateMaintenance.Clean(layout); return 0; }
                    if (args.Length == 1 && args[0] == "--purge-user-data") { EnsureAppStopped(); UpdateMaintenance.Purge(); return 0; }
                    UpdateTransaction.Recover(layout);
                    var state = layout.Read(); layout.VerifyVersion(state.Current, UpdateTrust.PublicKey);
                    if (args.Length > 0 && args[0] == "--activate") {
                        if (args.Length != 2) throw new ArgumentException("Invalid installer activation.");
                        ActivateInstaller(layout, args[1]); return 0;
                    }
                    StartCurrent(layout, false, args.Contains("--demo"), args.Contains("--autostart")); return 0;
                }
                if (args.Length != 6 || args[0] != "--apply") throw new ArgumentException("Updater requires a managed update job.");
                layout = new InstallLayout(args[1]); gate = layout.Lock();
                string job = Path.GetFullPath(args[2]);
                string updates = Path.Combine(layout.Root, "updates") + Path.DirectorySeparatorChar;
                if (!job.StartsWith(updates, StringComparison.OrdinalIgnoreCase) || Path.GetDirectoryName(job) != updates.TrimEnd(Path.DirectorySeparatorChar)) throw new IOException("Update job outside installation.");
                InstallLayout.NoReparse(job);
                int pid = int.Parse(args[3]); long ticks = long.Parse(args[4]);
                var stateBefore = layout.Read();
                WaitForParent(layout, stateBefore.Current, pid, ticks);
                restartOnFailure = true;
                EnsureAppStopped();
                var envelope = InstallLayout.ReadBounded(Path.Combine(job, "update.json"));
                var manifest = UpdatePackage.Verify(envelope, UpdateTrust.PublicKey);
                string version = UpdateTransaction.Stage(layout, Path.Combine(job, manifest.Package), envelope, UpdateTrust.PublicKey);
                Backup(layout, job);
                UpdateTransaction.Activate(layout, version, UpdateTrust.PublicKey, s => new ProcessTrial(layout, s, args[5] == "hidden"));
                UpdateDisplayVersion(layout, version);
                try { InstallLayout.Atomic(Path.Combine(job, "result.json"), "{\"status\":\"complete\"}"); } catch { /* Activation already committed; optional log failure is not rollback. */ }
                return 0;
            } catch (Exception ex) {
                if (layout != null) {
                    try { UpdateDisplayVersion(layout, layout.Read().Current); } catch { }
                    try { InstallLayout.Atomic(Path.Combine(layout.Root, "last-update-error.log"), DateTime.UtcNow.ToString("o") + "\n" + ex.Message); } catch { }
                    if (restartOnFailure) try { UpdateTransaction.Recover(layout); EnsureAppStopped(); StartCurrent(layout, true, false); } catch { }
                }
                MessageBox.Show("更新未完成，未强制结束任何程序。\n旧版本与配置备份已保留。\n\n" + ex.Message, "雷云lite — 安装 / 更新", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                return 1;
            } finally { if (gate != null) { gate.ReleaseMutex(); gate.Dispose(); } }
        }
        internal static void EnsureAppStopped()
        {
            using (var mutex = new Mutex(false, "Local\\LeiyunLite.v1")) {
                bool held; try { held = mutex.WaitOne(0); } catch (AbandonedMutexException) { held = true; }
                if (!held) throw new IOException("LeiyunLite is still running; no files were replaced.");
                mutex.ReleaseMutex();
            }
        }
        private static void WaitForParent(InstallLayout layout, string version, int pid, long ticks)
        {
            Process p; try { p = Process.GetProcessById(pid); } catch (ArgumentException) { return; }
            using (p) {
                if (p.StartTime.ToUniversalTime().Ticks != ticks || !string.Equals(p.MainModule.FileName, Path.Combine(layout.VersionPath(version), "LeiyunLite.Desktop.exe"), StringComparison.OrdinalIgnoreCase)) throw new IOException("Update parent identity mismatch.");
                if (!p.WaitForExit(20000)) throw new IOException("Waiting for application exit timed out.");
            }
        }
        internal static void StartCurrent(InstallLayout layout, bool hidden, bool demo, bool autoStart = false)
        {
            var state = layout.Read(); layout.VerifyVersion(state.Current, UpdateTrust.PublicKey);
            Process.Start(new ProcessStartInfo(Path.Combine(layout.VersionPath(state.Current), "LeiyunLite.Desktop.exe"), (demo ? "--demo" : hidden ? "--update-hidden" : autoStart ? "--autostart" : "")) { UseShellExecute = false, WorkingDirectory = layout.VersionPath(state.Current) });
        }
        private static void Backup(InstallLayout layout, string job)
        {
            string data = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "LeiyunLite");
            UpdateBackup.Create(data, Path.Combine(data, "UpdateBackups", Path.GetFileName(job)), @"Software\RazerBatteryTray");
        }
        private static void ActivateInstaller(InstallLayout layout, string version)
        {
            UpdatePackage.RequireVersion(version); EnsureAppStopped();
            var state = layout.Read(); layout.VerifyVersion(version, UpdateTrust.PublicKey);
            int order = Version.Parse(version).CompareTo(Version.Parse(state.Current));
            if (order < 0) throw new InvalidDataException("Installer downgrade refused.");
            if (order > 0) {
                string job = Path.Combine(layout.Root, "updates", "installer-" + Guid.NewGuid().ToString("N")); Directory.CreateDirectory(job); Backup(layout, job);
                UpdateTransaction.Activate(layout, version, UpdateTrust.PublicKey, s => new ProcessTrial(layout, s, false));
            }
            UpdateDisplayVersion(layout, version);
        }
        private static void UpdateDisplayVersion(InstallLayout layout, string version)
        {
            // Only touch the installer record that belongs to this exact directory.
            try {
            using (var uninstall = Registry.CurrentUser.OpenSubKey(@"Software\Microsoft\Windows\CurrentVersion\Uninstall")) {
                if (uninstall == null) return;
                foreach (string name in uninstall.GetSubKeyNames()) using (var key = uninstall.OpenSubKey(name, true)) {
                    string path = key == null ? null : key.GetValue("InstallLocation") as string;
                    if (path != null && string.Equals(path.TrimEnd('\\'), layout.Root, StringComparison.OrdinalIgnoreCase) && (key.GetValue("DisplayName") as string ?? "").StartsWith("雷云lite")) key.SetValue("DisplayVersion", version);
                }
            }
            } catch (Exception ex) {
                try { InstallLayout.Atomic(Path.Combine(layout.Root, "last-update-error.log"), "Application activation succeeded; display-version registration failed: " + ex.Message); } catch { }
            }
        }
    }
}
