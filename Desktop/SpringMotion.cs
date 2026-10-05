using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Runtime.CompilerServices;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Animation;

namespace RazerBatteryTray.Desktop
{
    internal struct SpringPreset
    {
        internal readonly double Mass, Stiffness, Damping;
        internal SpringPreset(double mass, double stiffness, double damping) { Mass = mass; Stiffness = stiffness; Damping = damping; }
        internal static readonly SpringPreset Snappy = new SpringPreset(1, 900, 48);
        internal static readonly SpringPreset Smooth = new SpringPreset(1, 400, 26);
        internal static readonly SpringPreset Magnetic = new SpringPreset(1, 650, 50);
    }
    internal sealed class SpringState
    {
        internal double Position, Velocity, Target;
        internal readonly double Mass, Stiffness, Damping;
        internal SpringState(double position, SpringPreset preset) { Position = Target = position; Mass = preset.Mass; Stiffness = preset.Stiffness; Damping = preset.Damping; }
        internal void Retarget(double target) { Target = target; }
        // Exact damped oscillator solution. All three presets are underdamped;
        // a delayed frame remains stable, without Euler catch-up loops.
        internal bool Advance(double seconds)
        {
            double decay = Damping / (2 * Mass), frequency = Math.Sqrt(Stiffness / Mass - decay * decay);
            double displacement = Position - Target, b = (Velocity + decay * displacement) / frequency;
            double t = Math.Max(0, seconds), exponential = Math.Exp(-decay * t), cos = Math.Cos(frequency * t), sin = Math.Sin(frequency * t);
            double wave = displacement * cos + b * sin;
            Position = Target + exponential * wave;
            Velocity = exponential * ((b * frequency * cos - displacement * frequency * sin) - decay * wave);
            if (Math.Abs(Position - Target) < .0001 && Math.Abs(Velocity) < .002) { Snap(); return true; }
            return false;
        }
        internal void Snap() { Position = Target; Velocity = 0; }
    }
    internal static class SpringMotion
    {
        internal sealed class Binding
        {
            internal Animatable Target;
            internal DependencyProperty Property;
            internal Lifetime Life;
            internal SpringState State;
            internal double? Rest;
            internal bool Active;
            internal void Apply() { Target.SetValue(Property, State.Position); }
        }
        internal sealed class Lifetime
        {
            internal FrameworkElement Owner;
            internal Window Window;
            internal readonly List<Binding> Bindings = new List<Binding>();
            internal Lifetime(FrameworkElement owner)
            {
                Owner = owner;
                owner.Loaded += Loaded;
                owner.Unloaded += (s, e) => Reset();
                owner.IsVisibleChanged += (s, e) => { if (!owner.IsVisible) Reset(); };
                Connect();
            }
            private void Loaded(object sender, RoutedEventArgs e) { Connect(); }
            private void Connect()
            {
                Window = System.Windows.Window.GetWindow(Owner);
                if (Window != null) windows.GetValue(Window, w => new WindowLife(w));
            }
            internal bool Allowed { get { return Ui.Motion && Owner.IsLoaded && Owner.IsVisible && (Window == null || Window.IsActive && Window.WindowState != WindowState.Minimized); } }
            internal void Reset()
            {
                foreach (var binding in Bindings) { if (binding.Rest.HasValue) binding.State.Retarget(binding.Rest.Value); Stop(binding); }
            }
        }
        private sealed class WindowLife
        {
            internal WindowLife(Window window)
            {
                window.Deactivated += (s, e) => ResetWindow(window);
                window.StateChanged += (s, e) => { if (window.WindowState == WindowState.Minimized) ResetWindow(window); };
                window.Closed += (s, e) => ResetWindow(window);
            }
        }
        private static readonly ConditionalWeakTable<FrameworkElement, Lifetime> owners = new ConditionalWeakTable<FrameworkElement, Lifetime>();
        private static readonly ConditionalWeakTable<Window, WindowLife> windows = new ConditionalWeakTable<Window, WindowLife>();
        private static readonly List<WeakReference> lives = new List<WeakReference>();
        private static readonly List<Binding> active = new List<Binding>();
        private static long lastTick;
        private static TimeSpan lastRenderingTime;
        internal static bool RenderingSubscribed { get; private set; }
        internal static int ActiveCount { get { return active.Count; } }
        static SpringMotion()
        {
            SystemParameters.StaticPropertyChanged += (s, e) => {
                if (e.PropertyName == "ClientAreaAnimation" && !SystemParameters.ClientAreaAnimation && Application.Current != null)
                    Application.Current.Dispatcher.BeginInvoke(new Action(UiMotion.SettleAll));
            };
        }
        internal static Lifetime Own(FrameworkElement owner)
        {
            Lifetime life;
            if (!owners.TryGetValue(owner, out life)) {
                life = new Lifetime(owner); owners.Add(owner, life);
                lives.RemoveAll(w => !w.IsAlive); lives.Add(new WeakReference(life));
            }
            return life;
        }
        internal static void WindowContext(FrameworkElement owner, FrameworkElement context)
        {
            var life = Own(owner); life.Window = Window.GetWindow(context);
            if (life.Window != null) windows.GetValue(life.Window, w => new WindowLife(w));
        }
        internal static Binding Get(FrameworkElement owner, Animatable target, DependencyProperty property, SpringPreset preset, double? rest = null)
        {
            var life = Own(owner);
            foreach (var binding in life.Bindings) if (ReferenceEquals(binding.Target, target) && binding.Property == property) return binding;
            var result = new Binding { Life = life, Target = target, Property = property, Rest = rest, State = new SpringState((double)target.GetValue(property), preset) };
            life.Bindings.Add(result); return result;
        }
        internal static void To(Binding binding, double target, double? start = null, bool animate = true)
        {
            // Start is used only for a new entrance. Retarget never resets velocity.
            if (start.HasValue && !binding.Active) { binding.State.Position = start.Value; binding.State.Velocity = 0; }
            binding.State.Retarget(target);
            if (!animate || !binding.Life.Allowed) { if (binding.Rest.HasValue) binding.State.Retarget(binding.Rest.Value); Stop(binding); return; }
            if (Math.Abs(binding.State.Position - target) < .0001 && Math.Abs(binding.State.Velocity) < .002) { Stop(binding); return; }
            if (!binding.Active) { binding.Active = true; active.Add(binding); }
            if (!RenderingSubscribed) {
                lastTick = Stopwatch.GetTimestamp(); lastRenderingTime = TimeSpan.MinValue;
                CompositionTarget.Rendering += Render; RenderingSubscribed = true;
            }
        }
        private static void Render(object sender, EventArgs e)
        {
            var rendering = e as RenderingEventArgs;
            if (rendering != null) { if (rendering.RenderingTime == lastRenderingTime) return; lastRenderingTime = rendering.RenderingTime; }
            long now = Stopwatch.GetTimestamp(); double elapsed = (now - lastTick) / (double)Stopwatch.Frequency; lastTick = now;
            Advance(elapsed);
        }
        internal static void Advance(double elapsed)
        {
            for (int i = active.Count - 1; i >= 0; i--) {
                var binding = active[i];
                if (!binding.Life.Allowed) { if (binding.Rest.HasValue) binding.State.Retarget(binding.Rest.Value); binding.State.Snap(); }
                else if (!binding.State.Advance(elapsed)) { binding.Apply(); continue; }
                binding.Apply(); binding.Active = false; active.RemoveAt(i);
            }
            DetachWhenIdle();
        }
        internal static void Stop(Binding binding) { binding.State.Snap(); binding.Apply(); if (binding.Active) { binding.Active = false; active.Remove(binding); } DetachWhenIdle(); }
        private static void DetachWhenIdle() { if (active.Count == 0 && RenderingSubscribed) { CompositionTarget.Rendering -= Render; RenderingSubscribed = false; } }
        internal static void Stop(FrameworkElement owner) { Lifetime life; if (owners.TryGetValue(owner, out life)) life.Reset(); }
        private static void ResetWindow(Window window) { foreach (var weak in lives) { var life = weak.Target as Lifetime; if (life != null && ReferenceEquals(life.Window, window)) life.Reset(); } }
        internal static void SettleAll() { foreach (var weak in lives) { var life = weak.Target as Lifetime; if (life != null) life.Reset(); } lives.RemoveAll(w => !w.IsAlive); DetachWhenIdle(); }
    }
}
