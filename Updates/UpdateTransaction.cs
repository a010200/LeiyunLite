using System;
using System.Diagnostics;
using System.IO;

namespace RazerBatteryTray.Updates
{
    internal interface IUpdateTrial : IDisposable
    {
        bool WaitReady();
        void Commit();
        void Abort();
    }
    internal static class UpdateTransaction
    {
        internal static void Recover(InstallLayout layout)
        {
            var state = layout.Read();
            if (state.Pending == null) return;
            if (TrialAlive(layout, state)) throw new IOException("An update trial is still running. Wait for it to exit before retrying.");
            // Before health acknowledgement Current still points to the previous version.
            state.Pending = null; state.Token = null; state.TrialPid = 0; state.TrialStart = 0; layout.Save(state);
        }
        private static bool TrialAlive(InstallLayout layout, InstallState state)
        {
            if (state.TrialPid <= 0) return false;
            try {
                using (var p = Process.GetProcessById(state.TrialPid))
                    return !p.HasExited && p.StartTime.ToUniversalTime().Ticks == state.TrialStart &&
                        string.Equals(p.MainModule.FileName, Path.Combine(layout.VersionPath(state.Pending), "LeiyunLite.Desktop.exe"), StringComparison.OrdinalIgnoreCase);
            } catch (ArgumentException) { return false; }
        }
        internal static void Activate(InstallLayout layout, string version, string key, Func<InstallState, IUpdateTrial> start)
        {
            Recover(layout); var state = layout.Read();
            if (Version.Parse(version) <= Version.Parse(state.Current)) throw new InvalidDataException("Same-version or downgrade update rejected.");
            layout.VerifyVersion(version, key); layout.VerifyVersion(state.Current, key);
            string old = state.Current, previous = state.Previous;
            state.Pending = version; state.Token = Guid.NewGuid().ToString("N"); state.TrialPid = 0; state.TrialStart = 0;
            layout.Save(state);
            IUpdateTrial trial = null;
            try {
                trial = start(state);
                if (!trial.WaitReady()) throw new IOException("New version did not become ready; previous version retained.");
                state.Current = version; state.Previous = old; state.Pending = null; state.Token = null;
                state.TrialPid = 0; state.TrialStart = 0;
                layout.Save(state);
                trial.Commit();
            } catch {
                if (trial != null) trial.Abort();
                state.Current = old; state.Previous = previous; state.Pending = null; state.Token = null; state.TrialPid = 0; state.TrialStart = 0;
                layout.Save(state); throw;
            } finally { if (trial != null) trial.Dispose(); }
        }
        internal static string Stage(InstallLayout layout, string zip, string envelope, string key)
        {
            var m = UpdatePackage.Verify(envelope, key); var state = layout.Read();
            if (Version.Parse(m.Version) <= Version.Parse(state.Current)) throw new InvalidDataException("Update must be newer than installed version.");
            layout.CheckUpdatePathBudget(state.Current, m.Version);
            string destination = layout.VersionPath(m.Version);
            if (Directory.Exists(destination)) {
                var existing = layout.VerifyVersion(m.Version, key);
                if (existing.Sha256 != m.Sha256) throw new InvalidDataException("Same version has different signed content; use a new version number.");
                return m.Version;
            }
            string staging = Path.Combine(layout.Root, "updates", "s-" + Guid.NewGuid().ToString("N").Substring(0, InstallLayout.ShortIdLength));
            Directory.CreateDirectory(Path.GetDirectoryName(staging));
            UpdatePackage.Extract(zip, staging, m); InstallLayout.Atomic(Path.Combine(staging, "update.json"), envelope);
            Directory.CreateDirectory(Path.Combine(layout.Root, "versions")); InstallLayout.NoReparse(destination);
            Directory.Move(staging, destination); return m.Version;
        }
    }
}
