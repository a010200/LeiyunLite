using System;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Threading;
using System.Windows.Media;
using System.Windows.Controls.Primitives;

namespace RazerBatteryTray.Desktop
{
    internal static class UiFeedback
    {
        internal static FrameworkElement Tip(string title, string explanation, bool warning = false)
        {
            // Risk wording stays in the tooltip; all explanation anchors share one neutral visual.
            var icon = Ui.Text("ⓘ", 14, Ui.Brush("TextSecondary"));
            icon.Margin = new Thickness(5, 0, 0, 0); icon.VerticalAlignment = VerticalAlignment.Center;
            icon.Focusable = true;
            var tooltip = new ToolTip { Content = Ui.Stack(Ui.Text(title, 14), Ui.Text(explanation, 12, Ui.Muted)), MaxWidth = 320, PlacementTarget = icon, Placement = PlacementMode.Bottom, StaysOpen = true };
            icon.ToolTip = tooltip; ToolTipService.SetInitialShowDelay(icon, 350); ToolTipService.SetShowDuration(icon, 10000);
            // Own only these explicit Info/Warning tips so closing can fade before the Popup disappears.
            ToolTipService.SetIsEnabled(icon, false);
            var session = new TipSession(icon, tooltip); icon.Tag = session;
            icon.MouseEnter += (s, e) => session.Wait();
            icon.GotKeyboardFocus += (s, e) => session.Wait();
            icon.MouseLeave += (s, e) => { if (!icon.IsKeyboardFocusWithin) session.Close(); };
            icon.LostKeyboardFocus += (s, e) => { if (!icon.IsMouseOver) session.Close(); };
            icon.Unloaded += (s, e) => session.CloseImmediately();
            return icon;
        }
        internal sealed class TipSession
        {
            private readonly FrameworkElement anchor;
            private readonly ToolTip tooltip;
            private readonly DispatcherTimer timer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(350) };
            internal TipSession(FrameworkElement anchor, ToolTip tooltip) { this.anchor = anchor; this.tooltip = tooltip; timer.Tick += (s, e) => { timer.Stop(); if (anchor.IsMouseOver || anchor.IsKeyboardFocusWithin) Open(); }; }
            internal void Wait() { timer.Stop(); if (tooltip.IsOpen) Open(); else timer.Start(); }
            internal void Open() { timer.Stop(); UiMotion.Stop(tooltip); tooltip.IsOpen = true; UiMotion.Fade(tooltip); }
            internal void Close() { timer.Stop(); if (tooltip.IsOpen) UiMotion.FadeOut(tooltip, CloseImmediately); }
            internal void CloseImmediately() { timer.Stop(); tooltip.IsOpen = false; UiMotion.Stop(tooltip); }
        }
    }
    internal sealed class UiSnackbar : Border
    {
        private readonly TextBlock text = Ui.Text("", 13);
        private readonly DispatcherTimer timer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(3) };
        internal UiSnackbar()
        {
            Background = Ui.Brush("CardBackground"); BorderBrush = Ui.Brush("CardBorder"); BorderThickness = new Thickness(1);
            CornerRadius = new CornerRadius(8); Padding = new Thickness(16, 10, 16, 10); Margin = new Thickness(0, 8, 0, 12);
            text.Margin = new Thickness(0); Child = text; Visibility = Visibility.Collapsed;
            timer.Tick += (s, e) => Clear(); Unloaded += (s, e) => timer.Stop();
        }
        internal void Show(string message)
        {
            timer.Stop(); text.Text = message ?? "";
            Visibility = string.IsNullOrEmpty(text.Text) ? Visibility.Collapsed : Visibility.Visible;
            if (Visibility == Visibility.Visible) { UiMotion.Enter(this); timer.Start(); }
        }
        internal void Clear() { timer.Stop(); UiMotion.Stop(this); text.Text = ""; Visibility = Visibility.Collapsed; }
    }
    internal sealed class UiTabStrip : Grid
    {
        private readonly Button[] tabs;
        private readonly Border indicator = new Border { Height = 3, Background = Ui.Brush("SelectionIndicator"), CornerRadius = new CornerRadius(1.5), HorizontalAlignment = HorizontalAlignment.Left, VerticalAlignment = VerticalAlignment.Bottom, IsHitTestVisible = false };
        private readonly TranslateTransform movement = new TranslateTransform();
        private int selected;
        internal UiTabStrip(params Button[] buttons)
        {
            tabs = buttons; var row = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 0, 0, 7) };
            foreach (var button in buttons) row.Children.Add(button);
            Children.Add(row); indicator.RenderTransform = movement; Children.Add(indicator);
            SizeChanged += (s, e) => Select(selected, false);
        }
        internal void Select(int index, bool animate = true)
        {
            selected = index; double x = 12;
            for (int i = 0; i < tabs.Length; i++) { tabs[i].Foreground = i == index ? Ui.Foreground : Ui.Muted; if (i < index) x += tabs[i].ActualWidth + tabs[i].Margin.Left + tabs[i].Margin.Right; }
            indicator.Width = Math.Max(24, tabs[index].ActualWidth - 24);
            if (animate) UiMotion.To(movement, TranslateTransform.XProperty, x);
            else UiMotion.To(movement, TranslateTransform.XProperty, x, 0);
        }
    }
}
