using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Media;
using System.Windows.Media.Animation;

namespace RazerBatteryTray.Desktop
{
    internal static class Ui
    {
        public static bool English, ReducedMotion;
        public static bool Light { get; private set; }
        private static readonly Dictionary<string, SolidColorBrush> palette = new Dictionary<string, SolidColorBrush>(StringComparer.OrdinalIgnoreCase);
        private static readonly Dictionary<string, ThemeColor> colors = new Dictionary<string, ThemeColor>(StringComparer.OrdinalIgnoreCase);
        private sealed class ThemeColor : INotifyPropertyChanged
        {
            private Color value;
            public Color Value { get { return value; } set { this.value = value; if (PropertyChanged != null) PropertyChanged(this, new PropertyChangedEventArgs("Value")); } }
            public event PropertyChangedEventHandler PropertyChanged;
        }
        private static readonly Dictionary<string, string> lightColors = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase) {
            {"#040404", "#FAFAFA"}, {"#141414", "#F2F2F2"}, {"#191919", "#FFFFFF"}, {"#202020", "#FFFFFF"},
            {"#222222", "#FFFFFF"}, {"#0F0F0F", "#FFFFFF"}, {"#303030", "#DADADA"}, {"#353535", "#DADADA"},
            {"#444444", "#878787"}, {"#484848", "#B8B8B8"}, {"#343434", "#DADADA"}, {"#505050", "#A0A0A0"},
            {"#333333", "#ECECEC"}, {"#292929", "#ECECEC"}, {"#253A21", "#E6E6E6"}, {"#34682B", "#BCEFB3"},
            {"#F3F3F3", "#1B1B1B"}, {"#AAAAAA", "#5F5F5F"}, {"#BBBBBB", "#5F5F5F"}, {"#D2D2D2", "#353535"}, {"#CCCCCC", "#606060"}
        };
        public static Brush Accent = Brush("#44D62C"), Background = Brush("#141414"), Frame = Brush("#040404"), Muted = Brush("#AAAAAA"), Foreground = Brush("#F3F3F3");
        public static readonly Brush Ink = new SolidColorBrush(Color.FromRgb(0x14, 0x14, 0x14));
        internal static Brush Border { get { return Brush("#353535"); } }
        private static readonly ThemeColor batteryTone = new ThemeColor { Value = Color.FromRgb(0x44, 0xD6, 0x2C) };
        public static readonly Brush BatteryForeground = BoundBrush(batteryTone);
        private static Brush BoundBrush(ThemeColor color) { var brush = new SolidColorBrush(); BindingOperations.SetBinding(brush, SolidColorBrush.ColorProperty, new Binding("Value") { Source = color }); return brush; }
        public static bool Motion { get { return !ReducedMotion && SystemParameters.ClientAreaAnimation; } }
        public static Brush Brush(string hex)
        {
            SolidColorBrush b;
            if (!palette.TryGetValue(hex, out b)) { string target; var color = new ThemeColor { Value = (Color)ColorConverter.ConvertFromString(Light && lightColors.TryGetValue(hex, out target) ? target : hex) }; b = new SolidColorBrush(); BindingOperations.SetBinding(b, SolidColorBrush.ColorProperty, new Binding("Value") { Source = color }); palette.Add(hex, b); colors.Add(hex, color); }
            return b;
        }
        public static void ApplyTheme(string theme)
        {
            Light = theme == "light";
            batteryTone.Value = (Color)ColorConverter.ConvertFromString(Light ? "#1B1B1B" : "#44D62C");
            foreach (var pair in colors) { string target; pair.Value.Value = (Color)ColorConverter.ConvertFromString(Light && lightColors.TryGetValue(pair.Key, out target) ? target : pair.Key); }
            ElasticSwitch.RefreshAll();
        }
        public static void InstallResources(Application app)
        {
            foreach (string key in lightColors.Keys) app.Resources["C" + key.Substring(1)] = Brush(key);
            app.Resources["Accent"] = Accent; app.Resources["Ink"] = Ink;
        }
        public static string T(string zh, string en) { return English ? en : zh; }
        public static TextBlock Text(string value, double size = 14, Brush color = null)
        {
            return new TextBlock { Text = value, FontSize = size, Foreground = color ?? Foreground,
                FontFamily = new FontFamily("Segoe UI, Microsoft YaHei UI"), TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 0, 0, 8) };
        }
        public static Button Button(string title, Action click, bool primary = false)
        {
            var b = new Button { Content = title, Margin = new Thickness(0, 0, 8, 8) };
            if (primary) { b.Background = Accent; b.Foreground = Ink; b.BorderBrush = Accent; }
            b.Click += (s, e) => click();
            var scale = new ScaleTransform(1, 1); b.RenderTransform = scale; b.RenderTransformOrigin = new Point(0.5, 0.5);
            Action<double> animate = target => {
                if (ReducedMotion || !SystemParameters.ClientAreaAnimation) return;
                var animation = new DoubleAnimation(target, TimeSpan.FromMilliseconds(100)) { EasingFunction = new CubicEase { EasingMode = EasingMode.EaseOut } };
                scale.BeginAnimation(ScaleTransform.ScaleXProperty, animation, HandoffBehavior.SnapshotAndReplace);
                scale.BeginAnimation(ScaleTransform.ScaleYProperty, animation, HandoffBehavior.SnapshotAndReplace);
            };
            b.PreviewMouseLeftButtonDown += (s, e) => animate(0.98);
            b.PreviewMouseLeftButtonUp += (s, e) => animate(1);
            b.MouseLeave += (s, e) => animate(1);
            b.IsVisibleChanged += (s, e) => { if (!b.IsVisible) { scale.BeginAnimation(ScaleTransform.ScaleXProperty, null); scale.BeginAnimation(ScaleTransform.ScaleYProperty, null); } };
            return b;
        }
        public static StackPanel Stack(params UIElement[] items) { var p = new StackPanel(); foreach (var item in items) p.Children.Add(item); return p; }
        public static WrapPanel Row(params UIElement[] items) { var p = new WrapPanel(); foreach (var item in items) p.Children.Add(item); return p; }
        public static Border Card(UIElement child) { return new Border { Background = Brush("#191919"), BorderBrush = Brush("#303030"), BorderThickness = new Thickness(1), CornerRadius = new CornerRadius(10), Padding = new Thickness(22), Margin = new Thickness(0, 0, 0, 16), Child = child }; }
        public static ScrollViewer Scroll(UIElement child) { return new ScrollViewer { Content = child, VerticalScrollBarVisibility = ScrollBarVisibility.Auto, HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled, Padding = new Thickness(0, 0, 12, 0) }; }
        public static ComboBox Combo(string[] values, int index = 0) { return new ElasticComboBox { ItemsSource = values, SelectedIndex = index, MinWidth = 160, Margin = new Thickness(0, 0, 0, 12) }; }
        public static TextBox Input(string value = "") { return new TextBox { Text = value, Margin = new Thickness(0, 0, 0, 12) }; }
        public static CheckBox Toggle(string title, bool state, Action<bool> change) { var c = new ElasticCheckBox { Content = title, IsChecked = state }; c.Click += (s, e) => { bool value = c.IsChecked == true; try { change(value); } catch { c.IsChecked = !value; } }; return c; }
        public static void Enter(FrameworkElement page, bool drawer = false)
        {
            Stop(page); if (ReducedMotion || !SystemParameters.ClientAreaAnimation) return;
            var translation = new TranslateTransform(); page.RenderTransform = translation;
            var ease = new CubicEase { EasingMode = EasingMode.EaseOut };
            var move = new DoubleAnimation(drawer ? 22 : 12, 0, TimeSpan.FromMilliseconds(200)) { EasingFunction = ease, FillBehavior = FillBehavior.Stop };
            translation.BeginAnimation(drawer ? TranslateTransform.XProperty : TranslateTransform.YProperty, move, HandoffBehavior.SnapshotAndReplace);
            page.BeginAnimation(UIElement.OpacityProperty, new DoubleAnimation(0.35, 1, TimeSpan.FromMilliseconds(180)) { FillBehavior = FillBehavior.Stop }, HandoffBehavior.SnapshotAndReplace);
        }
        public static void Stop(FrameworkElement element)
        {
            element.BeginAnimation(UIElement.OpacityProperty, null); element.Opacity = 1;
            var t = element.RenderTransform as TranslateTransform;
            if (t != null) { t.BeginAnimation(TranslateTransform.XProperty, null); t.BeginAnimation(TranslateTransform.YProperty, null); t.X = t.Y = 0; }
        }
    }
}
