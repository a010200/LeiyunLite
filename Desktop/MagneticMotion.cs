using System;
using System.Runtime.CompilerServices;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;

namespace RazerBatteryTray.Desktop
{
    // App-local routed pointer events only. The transparent template root owns
    // the fixed hit area; only its non-hit-testable chrome is translated.
    internal sealed class MagneticMotion
    {
        private static readonly ConditionalWeakTable<Button, MagneticMotion> attached = new ConditionalWeakTable<Button, MagneticMotion>();
        private readonly Button button;
        private SpringMotion.Binding x, y;
        private FrameworkElement visual;
        private double maximum;
        private MagneticMotion(Button button, double maximum)
        {
            this.button = button; this.maximum = maximum;
            button.Loaded += (s, e) => Initialize();
            button.MouseEnter += Move; button.MouseMove += Move;
            button.MouseLeave += (s, e) => Leave();
            button.SizeChanged += (s, e) => Leave();
            button.IsEnabledChanged += (s, e) => { if (!button.IsEnabled) Leave(); };
        }
        internal static void Attach(Button button, double maximum)
        {
            MagneticMotion motion;
            if (attached.TryGetValue(button, out motion)) motion.maximum = maximum;
            else { motion = new MagneticMotion(button, maximum); attached.Add(button, motion); }
            if (button.IsLoaded) motion.Initialize();
        }
        private void Initialize()
        {
            var chrome = UiMotion.ButtonVisual(button); if (chrome == null || ReferenceEquals(chrome, visual)) return;
            visual = chrome; var parts = UiMotion.Transform(chrome);
            x = SpringMotion.Get(button, parts.Magnetic, System.Windows.Media.TranslateTransform.XProperty, SpringPreset.Magnetic, 0);
            y = SpringMotion.Get(button, parts.Magnetic, System.Windows.Media.TranslateTransform.YProperty, SpringPreset.Magnetic, 0);
        }
        private void Move(object sender, MouseEventArgs e)
        {
            if (x == null || !button.IsEnabled || !x.Life.Allowed) return;
            UpdateTarget(e.GetPosition(button));
        }
        internal void UpdateTarget(Point pointer)
        {
            if (x == null || !button.IsEnabled || !x.Life.Allowed) return;
            double halfWidth = button.ActualWidth / 2, halfHeight = button.ActualHeight / 2;
            if (halfWidth <= 0 || halfHeight <= 0) return;
            double nx = Math.Max(-1, Math.Min(1, (pointer.X - halfWidth) / halfWidth));
            double ny = Math.Max(-1, Math.Min(1, (pointer.Y - halfHeight) / halfHeight));
            // Smooth falloff from the center; cap the vector length, not each axis.
            double radius = Math.Sqrt(nx * nx + ny * ny), gain = radius == 0 ? 0 : Math.Min(1, radius) / radius;
            SpringMotion.To(x, nx * maximum * gain); SpringMotion.To(y, ny * maximum * gain);
        }
        internal void Leave() { if (x != null) { SpringMotion.To(x, 0); SpringMotion.To(y, 0); } }
        internal static MagneticMotion For(Button button) { MagneticMotion motion; return attached.TryGetValue(button, out motion) ? motion : null; }
    }
}
