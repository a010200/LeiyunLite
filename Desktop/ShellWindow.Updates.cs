using System;
using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using RazerBatteryTray.Updates;
namespace RazerBatteryTray.Desktop
{
    internal static class UpdateSafety
    {
        internal static string Block(bool dirty, bool pending, bool recording, bool running, bool failedSave, bool drawer)
        {
            if (failedSave) return Ui.T("解绑等安全修改尚未保存，请先重试保存。", "A safety change has not been saved. Retry saving first.");
            if (recording) return Ui.T("正在录制，请结束并保存后更新。", "Finish and save the recording before updating.");
            if (running) return Ui.T("宏正在运行，请先停止。", "Stop macro playback before updating.");
            if (dirty || pending) return Ui.T("有未保存的宏修改，请先保存或明确放弃修改。", "Save or explicitly discard pending macro changes first.");
            if (drawer) return Ui.T("请先完成当前操作或关闭面板。", "Finish the current operation or close the panel.");
            return null;
        }
        [StructLayout(LayoutKind.Sequential)] private struct LastInput { public uint Size; public uint Tick; }
        [DllImport("user32.dll")] private static extern bool GetLastInputInfo(ref LastInput value);
        internal static bool IdleFor(TimeSpan time) { var data = new LastInput { Size = 8 }; return GetLastInputInfo(ref data) && unchecked((uint)Environment.TickCount - data.Tick) >= time.TotalMilliseconds; }
    }
    internal sealed partial class ShellWindow
    {
        internal UpdateSession Updates;
        internal string UpdateBlockReason()
        {
            return UpdateSafety.Block(DraftDirty, macroPage != null && macroPage.HasPendingEdit, macroPage != null && macroPage.IsRecording || Macros != null && Macros.RecordingSession, Macros != null && Macros.IsRunning, SafetySavePending, DrawerOpen);
        }
        internal void SaveUpdatePolicy(string policy, bool value)
        {
            bool check = Preferences.AutoCheckUpdates, download = Preferences.AutoDownloadUpdates, install = Preferences.AutoInstallUpdates;
            if (policy == "check") { Preferences.AutoCheckUpdates = value; if (!value) Preferences.AutoDownloadUpdates = Preferences.AutoInstallUpdates = false; }
            if (policy == "download") { Preferences.AutoDownloadUpdates = value; if (value) Preferences.AutoCheckUpdates = true; else Preferences.AutoInstallUpdates = false; }
            if (policy == "install") { Preferences.AutoInstallUpdates = value; if (value) Preferences.AutoCheckUpdates = Preferences.AutoDownloadUpdates = true; }
            try { if (!Demo) store.Save(Preferences); }
            catch { Preferences.AutoCheckUpdates = check; Preferences.AutoDownloadUpdates = download; Preferences.AutoInstallUpdates = install; throw; }
        }
        internal void ExitForUpdate(Func<Process> start)
        {
            string block = UpdateBlockReason(); if (block != null) throw new InvalidOperationException(block);
            if (Macros != null && !Macros.BeginUpdateHandoff()) throw new InvalidOperationException(Ui.T("宏状态已改变，请稍后重试。", "Macro state changed; retry later."));
            try {
                block = UpdateBlockReason(); if (block != null) throw new InvalidOperationException(block);
                using (var helper = start()) { if (helper == null) throw new InvalidOperationException("Update helper did not start."); }
                exiting = true; Close();
            } catch { if (Macros != null) Macros.CancelUpdateHandoff(); throw; }
        }
        private async Task<bool> CompleteTrial()
        {
            string token = DesktopApp.TrialToken;
            if (token == null) return true;
            IsEnabled = false;
            try {
                using (var ready = EventWaitHandle.OpenExisting(ProcessTrial.EventName(token, "ready")))
                using (var commit = EventWaitHandle.OpenExisting(ProcessTrial.EventName(token, "commit")))
                using (var abort = EventWaitHandle.OpenExisting(ProcessTrial.EventName(token, "abort"))) {
                    ready.Set();
                    int result = await Task.Run(() => WaitHandle.WaitAny(new WaitHandle[] { commit, abort }, 35000));
                    if (result != 0) { exiting = true; discardApproved = true; Close(); return false; }
                    var layout = InstallLayout.Detect(System.Reflection.Assembly.GetExecutingAssembly().Location); var state = layout.Read();
                    if (state.Current != AppVersion.Number || state.Pending != null) throw new InvalidOperationException("Update commit not confirmed.");
                }
                if (Macros != null) Macros.SuspendBindings(false); IsEnabled = true; return true;
            } catch { exiting = true; discardApproved = true; Close(); return false; }
        }
    }
}
