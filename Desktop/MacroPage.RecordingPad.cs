using System;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using RazerBatteryTray.Macros;
using MouseAction = RazerBatteryTray.Macros.MouseAction;
namespace RazerBatteryTray.Desktop
{
    internal sealed partial class MacroPage
    {
        private Border recordingPad;
        private ListBox recordedActions;
        private TextBlock padHint;
        private Grid countdownOverlay;
        private TextBlock countdownNumber, countdownCaption;
        private int lastCountdownNumber = -1;
        private bool areaRecording = true;
        private int renderedCount = -1;
        private FrameworkElement BuildRecordingPad()
        {
            padHint = Ui.Text("", 13, Ui.Muted);
            recordedActions = new ListBox { IsHitTestVisible = false, Focusable = false };
            var dock = new DockPanel(); DockPanel.SetDock(padHint, Dock.Top); dock.Children.Add(padHint); dock.Children.Add(recordedActions);
            var content = new Grid(); content.Children.Add(dock);
            recordingPad = new Border { Background = Ui.Brush("#191919"), BorderBrush = Ui.Accent, BorderThickness = new Thickness(1), CornerRadius = new CornerRadius(8), Padding = new Thickness(12), Focusable = true, Child = content, Visibility = Visibility.Collapsed };
            countdownNumber = Ui.Text("", 72, Ui.Accent); countdownNumber.FontWeight = FontWeights.SemiBold;
            countdownNumber.TextAlignment = TextAlignment.Center;
            countdownCaption = Ui.Text("", 14, Ui.Muted); countdownCaption.TextAlignment = TextAlignment.Center;
            var countdown = Ui.Stack(countdownNumber, countdownCaption);
            countdown.HorizontalAlignment = HorizontalAlignment.Center; countdown.VerticalAlignment = VerticalAlignment.Center;
            countdownOverlay = new Grid { Background = recordingPad.Background, Visibility = Visibility.Collapsed, IsHitTestVisible = false };
            countdownOverlay.Children.Add(countdown); content.Children.Add(countdownOverlay);
            recordingPad.PreviewMouseDown += (s, e) => { recordingPad.Focus(); UpdateRecordingRegion(); e.Handled = true; };
            recordingPad.PreviewMouseUp += (s, e) => e.Handled = true;
            recordingPad.PreviewMouseWheel += (s, e) => e.Handled = true;
            recordingPad.PreviewKeyDown += (s, e) => e.Handled = true;
            recordingPad.PreviewKeyUp += (s, e) => e.Handled = true;
            recordingPad.IsKeyboardFocusWithinChanged += (s, e) => UpdateRecordingRegion();
            recordingPad.SizeChanged += (s, e) => UpdateRecordingRegion();
            shell.Activated += RecordingWindowChanged;
            shell.Deactivated += RecordingWindowChanged;
            shell.LocationChanged += RecordingWindowChanged;
            return recordingPad;
        }
        private void UpdateCountdownVisual()
        {
            if (countdownOverlay == null) return;
            bool show = recording && countingDown && areaRecording;
            countdownOverlay.Visibility = show ? Visibility.Visible : Visibility.Collapsed;
            if (!show) { lastCountdownNumber = -1; return; }
            int seconds = Math.Max(1, 3 - (int)recordingClock.Elapsed.TotalSeconds);
            if (seconds != lastCountdownNumber) { countdownNumber.Text = seconds.ToString(); lastCountdownNumber = seconds; }
            countdownCaption.Text = Ui.T("准备录制", "Get ready");
        }
        private void RecordingWindowChanged(object sender, EventArgs args) { UpdateRecordingRegion(); }
        private void UpdateRecordingRegion()
        {
            if (shell.Macros == null || recordingPad == null) return;
            bool active = recording && !countingDown && areaRecording && shell.IsActive && recordingPad.IsKeyboardFocusWithin && recordingPad.IsVisible;
            var bounds = System.Drawing.Rectangle.Empty;
            if (recordingPad.IsVisible && PresentationSource.FromVisual(recordingPad) != null) {
                var a = recordingPad.PointToScreen(new Point()); var b = recordingPad.PointToScreen(new Point(recordingPad.ActualWidth, recordingPad.ActualHeight));
                bounds = System.Drawing.Rectangle.FromLTRB((int)Math.Ceiling(a.X), (int)Math.Ceiling(a.Y), (int)Math.Floor(b.X), (int)Math.Floor(b.Y));
            }
            shell.Macros.SetRecordingArea(bounds, active);
            padHint.Text = countingDown ? Ui.T("倒计时结束后在此操作真实键盘、鼠标和滚轮。", "After the countdown, use your physical keyboard and mouse here.") :
                !areaRecording ? Ui.T("外部录制 · 请切换到目标应用。此处显示实时动作。", "External recording · switch to the target app. Live actions appear here.") :
                active ? Ui.T("● 区域录制中 · 直接按键或点击、滚动 · Ctrl+Shift+F12停止", "● Recording here · press, click or scroll · Ctrl+Shift+F12 stops") :
                Ui.T("已暂停 · 点击此区域继续；其他区域输入不会录入。", "Paused · click here to resume; other areas are excluded.");
        }
        private void UpdateLiveRecording()
        {
            if (!recording) return;
            UpdateRecordingRegion();
            var recorder = shell.Macros.Recorder;
            if (recorder == null || recorder.Count == renderedCount) return;
            var snapshot = recorder.Snapshot(); renderedCount = snapshot.Length;
            recordedActions.Items.Clear();
            foreach (var step in snapshot) {
                string label = step.Kind == ActionKind.Delay ? step.Number + " ms" :
                    step.Kind == ActionKind.Keyboard ? ((System.Windows.Forms.Keys)step.KeyCode).ToString() : MouseRecordingLabel(step.Mouse);
                if (step.Kind != ActionKind.Delay) label += step.Press == PressMode.Down ? " ↓" : step.Press == PressMode.Up ? " ↑" : "";
                recordedActions.Items.Add(Ui.Text(label, 14));
            }
            if (recordedActions.Items.Count > 0) recordedActions.ScrollIntoView(recordedActions.Items[recordedActions.Items.Count - 1]);
        }
        private static string MouseRecordingLabel(MouseAction action)
        {
            switch (action) {
                case MouseAction.Left: return Ui.T("鼠标左键", "Left click");
                case MouseAction.Right: return Ui.T("鼠标右键", "Right click");
                case MouseAction.Middle: return Ui.T("滚轮中键", "Middle click");
                case MouseAction.X1: return Ui.T("侧键 1", "Side button 1");
                case MouseAction.X2: return Ui.T("侧键 2", "Side button 2");
                case MouseAction.WheelUp: return Ui.T("滚轮上滑", "Wheel up");
                case MouseAction.WheelDown: return Ui.T("滚轮下滑", "Wheel down");
                default: return action.ToString();
            }
        }
    }
}
