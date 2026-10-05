using System;
using System.Collections.Generic;
using System.Runtime.CompilerServices;
using System.Windows;
using System.Windows.Controls;
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
        internal static class MotionTokens
        {
            internal const double ButtonPressedScale = .985, HandlePressedScale = .975;
            internal const double PrimaryMagneticMax = 2, DrawerHandleMagneticMax = 4;
            internal const double DrawerEnterOffset = 16, PageEnterOffset = 10, DropdownOffset = 6;
        }
        internal sealed class MotionTransform
        {
            internal readonly TranslateTransform Entry = new TranslateTransform(), Magnetic = new TranslateTransform();
            internal readonly ScaleTransform Press = new ScaleTransform(1, 1);
            internal readonly TransformGroup Group = new TransformGroup();
            internal MotionTransform(FrameworkElement element)
            {
                // Preserve any existing transform; owned channels are never replaced.
                if (element.RenderTransform != null && !element.RenderTransform.Value.IsIdentity) Group.Children.Add(element.RenderTransform);
                Group.Children.Add(Press); Group.Children.Add(Magnetic); Group.Children.Add(Entry);
                element.RenderTransform = Group;
            }
        }
        private static readonly ConditionalWeakTable<FrameworkElement, MotionTransform> transforms = new ConditionalWeakTable<FrameworkElement, MotionTransform>();
        internal static MotionTransform Transform(FrameworkElement element) { return transforms.GetValue(element, e => new MotionTransform(e)); }
        internal static void SpringTo(FrameworkElement owner, Animatable target, DependencyProperty property, double final, SpringPreset preset, double? start = null, bool animate = true, double? rest = null)
        {
            SpringMotion.To(SpringMotion.Get(owner, target, property, preset, rest), final, start, animate);
        }
        internal static FrameworkElement ButtonVisual(Button button)
        {
            button.ApplyTemplate();
            return button.Template == null ? null : button.Template.FindName("Chrome", button) as FrameworkElement ?? button.Template.FindName("HandleChrome", button) as FrameworkElement;
        }
        // Compatibility for the existing caption controls; their owner explicitly
        // stops the transform. Core Button/Toggle paths use owner-bound springs.
        internal static void Press(ScaleTransform scale, bool pressed) { To(scale, ScaleTransform.ScaleXProperty, pressed ? MotionTokens.ButtonPressedScale : 1, Fast); To(scale, ScaleTransform.ScaleYProperty, pressed ? MotionTokens.ButtonPressedScale : 1, Fast); }
        internal static void Press(Button button, bool pressed, double scale = MotionTokens.ButtonPressedScale)
        {
            var visual = ButtonVisual(button); if (visual == null) return;
            visual.RenderTransformOrigin = new Point(.5, .5);
            var parts = Transform(visual);
            SpringTo(button, parts.Press, ScaleTransform.ScaleXProperty, pressed && Ui.Motion ? scale : 1, SpringPreset.Snappy, rest: 1);
            SpringTo(button, parts.Press, ScaleTransform.ScaleYProperty, pressed && Ui.Motion ? scale : 1, SpringPreset.Snappy, rest: 1);
        }
        internal static void Enter(FrameworkElement element, bool drawer = false)
        {
            var move = Transform(element).Entry;
            SpringTo(element, move, drawer ? TranslateTransform.XProperty : TranslateTransform.YProperty, 0, SpringPreset.Smooth,
                drawer ? MotionTokens.DrawerEnterOffset : MotionTokens.PageEnterOffset, rest: 0); Fade(element);
        }
        internal static void MoveIndicator(FrameworkElement element, bool selected) { element.Visibility = selected ? Visibility.Visible : Visibility.Hidden; if (selected) Enter(element); else Stop(element); }
        internal static void PulseOnce(FrameworkElement element) { Fade(element, .55); }
        internal static void Stop(FrameworkElement element)
        {
            SpringMotion.Stop(element);
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
            SpringMotion.SettleAll();
            foreach (var clock in clocks.ToArray()) { var target = clock.Target.Target as Animatable; if (target != null && !target.IsFrozen) { target.BeginAnimation(clock.Property, null); target.SetValue(clock.Property, clock.Final); } }
            foreach (var target in fades.ToArray()) { var element = target.Target.Target as FrameworkElement; if (element != null) { element.BeginAnimation(UIElement.OpacityProperty, null); element.Opacity = target.Final; Complete(target); } }
            clocks.RemoveAll(x => !x.Target.IsAlive); fades.RemoveAll(x => !x.Target.IsAlive);
        }
    }
}
