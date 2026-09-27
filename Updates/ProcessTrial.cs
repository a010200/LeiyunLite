using System;
using System.Diagnostics;
using System.IO;
using System.Threading;
namespace RazerBatteryTray.Updates
{
    internal sealed class ProcessTrial : IUpdateTrial
    {
        private readonly EventWaitHandle ready, commit, abort;
        private readonly Process process;
        internal static string EventName(string token, string kind) { Guid value; if (!Guid.TryParseExact(token, "N", out value)) throw new ArgumentException("Invalid trial token."); return "Local\\LeiyunLite.Trial." + token + "." + kind; }
        internal ProcessTrial(InstallLayout layout, InstallState state, bool hidden)
        {
            ready = new EventWaitHandle(false, EventResetMode.ManualReset, EventName(state.Token, "ready"));
            commit = new EventWaitHandle(false, EventResetMode.ManualReset, EventName(state.Token, "commit"));
            abort = new EventWaitHandle(false, EventResetMode.ManualReset, EventName(state.Token, "abort"));
            try {
                process = Process.Start(new ProcessStartInfo(Path.Combine(layout.VersionPath(state.Pending), "LeiyunLite.Desktop.exe"),
                    "--update-trial " + state.Token + (hidden ? " --update-hidden" : "")) { UseShellExecute = false, WorkingDirectory = layout.VersionPath(state.Pending) });
                state.TrialPid = process.Id; state.TrialStart = process.StartTime.ToUniversalTime().Ticks; layout.Save(state);
            } catch { Abort(); Dispose(); throw; }
        }
        public bool WaitReady()
        {
            var watch = Stopwatch.StartNew();
            while (watch.ElapsedMilliseconds < 30000) { if (process.HasExited) return false; if (ready.WaitOne(100)) return !process.HasExited; }
            return false;
        }
        public void Commit() { commit.Set(); }
        public void Abort()
        {
            if (abort != null) abort.Set();
            if (process != null && !process.HasExited) {
                process.CloseMainWindow();
                if (!process.WaitForExit(5000)) throw new IOException("Trial is not responding; no process was force-killed. Close it before recovery.");
            }
        }
        public void Dispose() { if (process != null) process.Dispose(); if (ready != null) ready.Dispose(); if (commit != null) commit.Dispose(); if (abort != null) abort.Dispose(); }
    }
}
