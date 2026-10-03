using System;
using System.Diagnostics;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Threading;
using RazerBatteryTray.Macros;

namespace RazerBatteryTray.Desktop
{
    internal sealed partial class MacroPage
    {
        private readonly DispatcherTimer recordingTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(100) };
        private readonly Stopwatch recordingClock = new Stopwatch();
        private Border recordingStatus;
        private TextBlock recordingText;
        private MacroDefinition recordingTarget;
        private RecordingDelay recordingDelay;
        private int recordingFixedDelay, recordingCapacity;
        private bool recording, countingDown, appendRecording;
        internal bool IsRecording { get { return recording; } }
        private FrameworkElement BuildRecordingStatus()
        {
            recordingText = Ui.Text("", 13); recordingText.VerticalAlignment = VerticalAlignment.Center; recordingText.Margin = new Thickness(0);
            var row = new DockPanel(); var cancel = Tool(Ui.T("取消录制", "Cancel recording"), () => EndRecording(true));
            DockPanel.SetDock(cancel, Dock.Right); row.Children.Add(cancel); row.Children.Add(recordingText);
            recordingStatus = Ui.Card(row); recordingStatus.Padding = new Thickness(12, 8, 4, 8); recordingStatus.Visibility = Visibility.Collapsed;
            return recordingStatus;
        }
        private void InitializeRecording() { recordingTimer.Tick += (s, e) => TickRecording(); Loaded += (s, e) => recordingTimer.Start(); Unloaded += (s, e) => { if (!recording) recordingTimer.Stop(); }; }
        private void OpenRecording()
        {
            if (!CommitPending() || recording) return;
            var destination = Ui.Combo(Selected == null ? new[] { Ui.T("新建宏", "New macro") } : new[] { Ui.T("新建宏", "New macro"), Ui.T("追加到当前宏", "Append to current macro") });
            var timing = Ui.Combo(new[] { Ui.T("实际间隔", "Actual delays"), Ui.T("固定间隔", "Fixed delay"), Ui.T("不添加间隔", "No delays") });
            var scope = Ui.Combo(new[] { Ui.T("本页录制区域（推荐）", "Recording area (recommended)"), Ui.T("外部应用", "External applications") });
            var fixedInput = Ui.Input("50"); fixedInput.IsEnabled = false;
            timing.SelectionChanged += (s, e) => fixedInput.IsEnabled = timing.SelectedIndex == 1;
            var error = Ui.Text("", 13, Ui.Muted);
            shell.OpenDrawer(Ui.T("录制键鼠操作", "Record keyboard and mouse"), Ui.Stack(
                Ui.Text(Ui.T("仅在你开始后录制，本机处理。请勿输入密码或其他敏感内容。", "Explicit local recording only. Do not enter passwords or sensitive information."), 14),
                Ui.Text(Ui.T("区域模式直接在中间操作键鼠；其他控件不录入。不记录移动轨迹或输入法文字。外部模式需切换到目标应用。", "Area mode records physical input in the center, excluding controls. No pointer paths or IME text. External mode requires a target app."), 13, Ui.Muted),
                Ui.Text(Ui.T("录制范围", "Recording scope")), scope,
                Ui.Text(Ui.T("录制结果", "Destination")), destination, Ui.Text(Ui.T("动作间隔", "Timing")), timing,
                Ui.Text(Ui.T("固定间隔（毫秒）", "Fixed delay (ms)")), fixedInput,
                Ui.Text(Ui.T("3 秒倒计时后开始。Ctrl + Shift + F12 停止。最多 10 分钟、500 步（包括间隔与释放动作）。", "Starts after 3 seconds. Ctrl + Shift + F12 stops. Maximum 10 minutes / 500 steps including delays and releases."), 13, Ui.Muted), error,
                Ui.Button(Ui.T("开始倒计时", "Start countdown"), () => {
                    int ms = 0; if (timing.SelectedIndex == 1 && (!int.TryParse(fixedInput.Text, out ms) || ms < 0 || ms > 600000)) { error.Text = Ui.T("请输入 0–600000 毫秒。", "Enter 0–600000 ms."); return; }
                    if (shell.Demo) { error.Text = Ui.T("安全预览不监听输入；请用正常版进行录制。", "Safe preview does not listen to input. Use the normal app to record."); return; }
                    bool append = destination.SelectedIndex == 1;
                    if (append && Selected.Steps.Count > 498 || !append && shell.Draft.Macros.Count >= 100) { error.Text = Ui.T("宏或动作数量已达上限。", "Macro or step limit reached."); return; }
                    try { areaRecording = scope.SelectedIndex == 0; BeginRecording(append, (RecordingDelay)timing.SelectedIndex, ms); shell.CloseDrawer(); }
                    catch (Exception ex) { EndRecording(true); error.Text = ex.Message; }
                }, true), Ui.Button(Ui.T("取消", "Cancel"), shell.CloseDrawer)));
        }
        private void BeginRecording(bool append, RecordingDelay delay, int fixedDelay)
        {
            shell.Notice("");
            shell.Macros.PrepareRecording();
            recordingTarget = append ? Selected : new MacroDefinition { Name = Ui.T("录制宏 ", "Recorded macro ") + (shell.Draft.Macros.Count + 1) };
            appendRecording = append; recordingDelay = delay; recordingFixedDelay = fixedDelay; recordingCapacity = 500 - recordingTarget.Steps.Count;
            recording = countingDown = true; recordingClock.Restart(); recordingTimer.Start(); TickRecording();
            UpdateDirty();
            renderedCount = -1; recordedActions.Items.Clear();
        }
        private void TickRecording()
        {
            try
            {
                if (recording)
                {
                    var recorder = shell.Macros.Recorder;
                    if (shell.Macros.RecordingStopRequested || recorder != null && recorder.Completed) { EndRecording(false); return; }
                    if (countingDown && recordingClock.ElapsedMilliseconds >= 3000) { shell.Macros.StartRecording(recordingDelay, recordingFixedDelay, recordingCapacity, areaRecording); countingDown = false; recordingClock.Restart(); UpdateRecordingControls(); if (areaRecording) recordingPad.Focus(); }
                    if (!countingDown && recordingClock.Elapsed.TotalMinutes >= 10) { EndRecording(false); return; }
                    recordingText.Text = countingDown ? Ui.T("即将开始 · ", "Starting in · ") + Math.Max(1, 3 - (int)recordingClock.Elapsed.TotalSeconds) + (areaRecording ? Ui.T(" 秒 · 在中间区域操作", " s · use the center area") : Ui.T(" 秒 · 切换到目标窗口", " s · focus the target app"))
                        : "● " + Ui.T("录制中 ", "Recording ") + recordingClock.Elapsed.ToString(@"mm\:ss") + " · " + (shell.Macros.Recorder == null ? 0 : shell.Macros.Recorder.Count) + Ui.T(" 步 · Ctrl + Shift + F12 停止", " steps · Ctrl + Shift + F12 to stop");
                }
                UpdateRecordingControls(); UpdateLiveRecording();
            }
            catch (Exception ex) { EndRecording(true); shell.Notice(Ui.T("录制已取消：", "Recording cancelled: ") + ex.Message); }
        }
        internal void EndRecording(bool cancel)
        {
            if (!recording) return;
            bool wasCountdown = countingDown; recording = countingDown = false; recordingClock.Stop();
            var recorder = shell.Macros.Recorder;
            var result = shell.Macros.EndRecording();
            string reason = recorder == null ? "" : recorder.Reason;
            if (!cancel && !wasCountdown && result.Count > 0 && reason != "error")
            {
                recordingTarget.Steps.AddRange(result);
                if (!appendRecording) shell.Draft.Macros.Add(recordingTarget);
                shell.DraftDirty = true; ReloadLibrary(recordingTarget.Id);
                UiMotion.PulseOnce(saveBar);
                shell.Notice(Ui.T("录制已加入宏，请保存修改。", "Recording added; save your changes.") + (reason == "limit" || reason == "overflow" ? Ui.T(" 已达到录制上限，自动停止。", " Recording limit reached; stopped automatically.") : ""));
            }
            else shell.Notice(!cancel && !wasCountdown && result.Count == 0 ? Ui.T("未录到输入；请在录制区域操作，或选择外部应用模式。原有宏未改变。", "No input captured. Use the recording area or choose external mode; existing macros unchanged.") : Ui.T("录制结束，原有宏未改变。", "Recording ended; existing macros unchanged."));
            recordingTarget = null; UpdateDirty();
        }
        private void UpdateRecordingControls()
        {
            if (testButton == null || workspace == null) return;
            bool running = shell.Macros != null && shell.Macros.IsRunning;
            testButton.IsEnabled = !recording && !running && Selected != null && Selected.Steps.Count > 0;
            stopButton.IsEnabled = recording || running; recordButton.IsEnabled = !recording;
            saveButton.IsEnabled = !recording; libraryTools.IsEnabled = bindingWorkspace.IsEnabled = !recording;
            recordingPalette.IsEnabled = recordingTools.IsEnabled = properties.IsEnabled = steps.IsEnabled = !recording;
            recordingPad.Visibility = recording ? Visibility.Visible : Visibility.Collapsed;
            steps.Visibility = recording ? Visibility.Collapsed : Visibility.Visible;
            myTab.IsEnabled = bindingTab.IsEnabled = !recording;
            emptyHint.Visibility = !recording && (Selected == null || Selected.Steps.Count == 0) ? Visibility.Visible : Visibility.Collapsed;
            recordingStatus.Visibility = recording ? Visibility.Visible : Visibility.Collapsed;
        }
        internal void DisposeRecording() { EndRecording(true); recordingTimer.Stop(); shell.Activated -= RecordingWindowChanged; shell.Deactivated -= RecordingWindowChanged; shell.LocationChanged -= RecordingWindowChanged; }
    }
}
