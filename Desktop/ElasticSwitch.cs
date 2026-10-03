using System;
using System.Collections.Generic;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Shapes;

namespace RazerBatteryTray.Desktop
{
    internal sealed class ElasticCheckBox : CheckBox
    {
        public ElasticCheckBox() { SetResourceReference(StyleProperty, typeof(CheckBox)); IsVisibleChanged += (s, e) => ElasticSwitch.Initialize(this); }
        public override void OnApplyTemplate() { base.OnApplyTemplate(); ElasticSwitch.Initialize(this); }
    }
    // One template and one animation policy for every CheckBox, including binding editors.
    internal static class ElasticSwitch
    {
        private static bool installed;
        private static readonly List<WeakReference> live = new List<WeakReference>();
        internal static void Initialize(CheckBox control) { Update(control, false); }
        internal static void Install()
        {
            if (installed) return; installed = true;
            EventManager.RegisterClassHandler(typeof(CheckBox), FrameworkElement.LoadedEvent, new RoutedEventHandler((s, e) => { var c = (CheckBox)s; live.RemoveAll(w => !w.IsAlive || ReferenceEquals(w.Target, c)); live.Add(new WeakReference(c)); Update(c, false); }));
            EventManager.RegisterClassHandler(typeof(CheckBox), FrameworkElement.UnloadedEvent, new RoutedEventHandler((s, e) => Update((CheckBox)s, false)));
            EventManager.RegisterClassHandler(typeof(CheckBox), CheckBox.CheckedEvent, new RoutedEventHandler((s, e) => Update((CheckBox)s, true)));
            EventManager.RegisterClassHandler(typeof(CheckBox), CheckBox.UncheckedEvent, new RoutedEventHandler((s, e) => Update((CheckBox)s, true)));
            EventManager.RegisterClassHandler(typeof(CheckBox), UIElement.PreviewMouseLeftButtonDownEvent, new MouseButtonEventHandler((s, e) => Press((CheckBox)s, true)), true);
            EventManager.RegisterClassHandler(typeof(CheckBox), UIElement.PreviewMouseLeftButtonUpEvent, new MouseButtonEventHandler((s, e) => Press((CheckBox)s, false)), true);
            EventManager.RegisterClassHandler(typeof(CheckBox), UIElement.LostMouseCaptureEvent, new MouseEventHandler((s, e) => Press((CheckBox)s, false)), true);
            EventManager.RegisterClassHandler(typeof(CheckBox), UIElement.PreviewKeyDownEvent, new KeyEventHandler((s, e) => { if (e.Key == Key.Space) Press((CheckBox)s, true); }), true);
            EventManager.RegisterClassHandler(typeof(CheckBox), UIElement.PreviewKeyUpEvent, new KeyEventHandler((s, e) => { if (e.Key == Key.Space) Press((CheckBox)s, false); }), true);
            SystemParameters.StaticPropertyChanged += (s, e) => { if (Application.Current != null) Application.Current.Dispatcher.BeginInvoke(new Action(RefreshAll)); };
        }
        internal static void RefreshAll() { live.RemoveAll(w => !w.IsAlive); foreach (var w in live.ToArray()) { var c = w.Target as CheckBox; if (c != null) Update(c, false); } }
        private static T Part<T>(CheckBox c, string name) where T : class
        {
            if (c.Template == null) return null;
            var dot = c.Template.FindName("Dot", c) as Ellipse;
            if (dot != null && !ReferenceEquals(dot.Tag, c))
            {
                // Loose-XAML template Freezables can be shared. Each thumb owns its clocks.
                var group = new TransformGroup(); group.Children.Add(new ScaleTransform(1, 1, 7, 7)); group.Children.Add(new TranslateTransform()); dot.RenderTransform = group; dot.Tag = c;
                live.RemoveAll(w => !w.IsAlive || ReferenceEquals(w.Target, c)); live.Add(new WeakReference(c));
            }
            if (dot != null && name == "DotMove") return ((TransformGroup)dot.RenderTransform).Children[1] as T;
            if (dot != null && name == "DotScale") return ((TransformGroup)dot.RenderTransform).Children[0] as T;
            return c.Template.FindName(name, c) as T;
        }
        private static void Press(CheckBox c, bool down)
        {
            var scale = Part<ScaleTransform>(c, "DotScale"); if (scale == null || !c.IsEnabled) return;
            double from = scale.ScaleX, to = down && Ui.Motion ? 1.12 : 1;
            UiMotion.To(scale, ScaleTransform.ScaleXProperty, to, UiMotion.Fast, from);
        }
        private static void Update(CheckBox c, bool animate)
        {
            var move = Part<TranslateTransform>(c, "DotMove"); var track = Part<Border>(c, "Toggle"); var dot = Part<Ellipse>(c, "Dot");
            if (move == null || track == null || dot == null) return;
            bool on = c.IsChecked == true; double from = move.X, target = on ? 18 : 0;
            var old = track.Background as SolidColorBrush; Color initial = old == null ? Colors.Gray : old.Color;
            Color final = ((SolidColorBrush)(on ? Ui.Accent : Ui.Brush("ToggleOff"))).Color;
            move.BeginAnimation(TranslateTransform.XProperty, null); move.X = target;
            var fill = new SolidColorBrush(final); track.Background = fill; dot.Fill = on ? Ui.Ink : Ui.Brush("ToggleThumb");
            if (!animate || !Ui.Motion || !c.IsLoaded || !c.IsVisible) { Press(c, false); return; }
            UiMotion.To(move, TranslateTransform.XProperty, target, UiMotion.Normal, from);
            UiMotion.ColorTo(fill, initial, final);
        }
    }
}
