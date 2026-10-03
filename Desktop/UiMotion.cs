using System;
using System.Collections.Generic;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Animation;

namespace RazerBatteryTray.Desktop
{
    internal static class UiMotion
    {
        internal const int Fast = 110, Normal = 170, Slow = 220;
        private sealed class ClockTarget { internal WeakReference Target; internal DependencyProperty Property; internal object Final; }
        private static readonly List<ClockTarget> clocks = new List<ClockTarget>();
        private static void Remember(Animatable target, DependencyProperty property, object final)
        {
            clocks.RemoveAll(x => !x.Target.IsAlive || ReferenceEquals(x.Target.Target, target) && x.Property == property);
            clocks.Add(new ClockTarget { Target = new WeakReference(target), Property = property, Final = final });
        }
        internal static void To(Animatable target, DependencyProperty property, double final, int duration = Normal, double? start = null)
        {
            double from = start ?? (double)target.GetValue(property);
            target.BeginAnimation(property, null); target.SetValue(property, final);
            Remember(target, property, final);
            if (Ui.Motion) target.BeginAnimation(property, new DoubleAnimation(from, final, TimeSpan.FromMilliseconds(duration)) { EasingFunction = new CubicEase { EasingMode = EasingMode.EaseOut }, FillBehavior = FillBehavior.Stop }, HandoffBehavior.SnapshotAndReplace);
        }
        // UIElement is not Animatable; opacity clocks have their own typed targets.
        private sealed class FadeTarget { internal WeakReference Target; internal double Final; internal Action Completed; }
        private static readonly List<FadeTarget> fades = new List<FadeTarget>();
        internal static void Fade(FrameworkElement element, double from = .35, int duration = Normal, double final = 1, Action completed = null)
        {
            element.BeginAnimation(UIElement.OpacityProperty, null); element.Opacity = final;
            fades.RemoveAll(w => !w.Target.IsAlive || ReferenceEquals(w.Target.Target, element));
            var target = new FadeTarget { Target = new WeakReference(element), Final = final, Completed = completed }; fades.Add(target);
            if (Ui.Motion) {
                var animation = new DoubleAnimation(from, final, TimeSpan.FromMilliseconds(duration)) { EasingFunction = new CubicEase { EasingMode = EasingMode.EaseOut }, FillBehavior = FillBehavior.Stop };
                animation.Completed += (s, e) => Complete(target);
                element.BeginAnimation(UIElement.OpacityProperty, animation, HandoffBehavior.SnapshotAndReplace);
            } else Complete(target);
        }
        private static void Complete(FadeTarget target) { var action = target.Completed; target.Completed = null; if (action != null) action(); }
        internal static void FadeOut(FrameworkElement element, Action completed) { Fade(element, element.Opacity, Fast, 0, completed); }
        internal static void ColorTo(SolidColorBrush brush, Color from, Color final)
        {
            brush.BeginAnimation(SolidColorBrush.ColorProperty, null); brush.Color = final; Remember(brush, SolidColorBrush.ColorProperty, final);
            if (Ui.Motion) brush.BeginAnimation(SolidColorBrush.ColorProperty, new ColorAnimation(from, final, TimeSpan.FromMilliseconds(Fast)) { FillBehavior = FillBehavior.Stop }, HandoffBehavior.SnapshotAndReplace);
        }
        internal static void Press(ScaleTransform scale, bool pressed) { To(scale, ScaleTransform.ScaleXProperty, pressed ? .98 : 1, Fast); To(scale, ScaleTransform.ScaleYProperty, pressed ? .98 : 1, Fast); }
        internal static void Enter(FrameworkElement element, bool drawer = false)
        {
            Stop(element); var move = new TranslateTransform(); element.RenderTransform = move;
            To(move, drawer ? TranslateTransform.XProperty : TranslateTransform.YProperty, 0, Slow, drawer ? 8 : 12); Fade(element);
        }
        internal static void MoveIndicator(FrameworkElement element, bool selected) { element.Visibility = selected ? Visibility.Visible : Visibility.Hidden; if (selected) Enter(element); else Stop(element); }
        internal static void PulseOnce(FrameworkElement element) { Fade(element, .55); }
        internal static void Stop(FrameworkElement element)
        {
            bool hadFade = fades.Exists(w => ReferenceEquals(w.Target.Target, element));
            fades.RemoveAll(w => !w.Target.IsAlive || ReferenceEquals(w.Target.Target, element));
            element.BeginAnimation(UIElement.OpacityProperty, null);
            // Do not override template opacity triggers (notably disabled buttons) with a local value.
            if (hadFade || !(element is System.Windows.Controls.Control)) element.Opacity = 1;
            // Stop supersedes any earlier remembered press/entry destination.
            clocks.RemoveAll(x => !x.Target.IsAlive || ReferenceEquals(x.Target.Target, element.RenderTransform));
            var translate = element.RenderTransform as TranslateTransform;
            if (translate != null) { translate.BeginAnimation(TranslateTransform.XProperty, null); translate.BeginAnimation(TranslateTransform.YProperty, null); translate.X = translate.Y = 0; }
            var scale = element.RenderTransform as ScaleTransform;
            if (scale != null) { scale.BeginAnimation(ScaleTransform.ScaleXProperty, null); scale.BeginAnimation(ScaleTransform.ScaleYProperty, null); scale.ScaleX = scale.ScaleY = 1; }
        }
        internal static void SettleAll()
        {
            foreach (var clock in clocks.ToArray()) { var target = clock.Target.Target as Animatable; if (target != null && !target.IsFrozen) { target.BeginAnimation(clock.Property, null); target.SetValue(clock.Property, clock.Final); } }
            foreach (var target in fades.ToArray()) { var element = target.Target.Target as FrameworkElement; if (element != null) { element.BeginAnimation(UIElement.OpacityProperty, null); element.Opacity = target.Final; Complete(target); } }
            clocks.RemoveAll(x => !x.Target.IsAlive); fades.RemoveAll(x => !x.Target.IsAlive);
        }
    }
}
