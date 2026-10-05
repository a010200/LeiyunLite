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
        public static bool English;
        private static bool reducedMotion;
        public static bool ReducedMotion { get { return reducedMotion; } set { reducedMotion = value; if (value) { UiMotion.SettleAll(); ElasticSwitch.RefreshAll(); } } }
        // Legacy callers use Light only to select a surface; Fluent replaces that theme.
        public static bool Light { get { return ThemeManager.Current == "fluent"; } }
        private static readonly Dictionary<string, SolidColorBrush> palette = new Dictionary<string, SolidColorBrush>(StringComparer.OrdinalIgnoreCase);
        private static readonly Dictionary<string, ThemeColor> colors = new Dictionary<string, ThemeColor>(StringComparer.OrdinalIgnoreCase);
        private sealed class ThemeColor : INotifyPropertyChanged
        {
            private Color value;
            public Color Value { get { return value; } set { this.value = value; if (PropertyChanged != null) PropertyChanged(this, new PropertyChangedEventArgs("Value")); } }
            public event PropertyChangedEventHandler PropertyChanged;
        }
        public static readonly Brush Accent = Brush("Accent"), Background = Brush("PageBackground"), Frame = Brush("WindowBackground"), Muted = Brush("TextSecondary"), Foreground = Brush("TextPrimary");
        public static readonly Brush Ink = Brush("AccentText");
        internal static Brush Border { get { return Brush("Divider"); } }
        public static readonly Brush BatteryForeground = Brush("SelectionIndicator");
        public static bool Motion { get { return !ReducedMotion && SystemParameters.ClientAreaAnimation; } }
        public static Brush Brush(string hex)
        {
            SolidColorBrush b;
            hex = ThemeTokens.Role(hex);
            if (!palette.TryGetValue(hex, out b)) { var color = new ThemeColor { Value = ThemeTokens.ColorFor(hex, ThemeManager.Current) }; b = new SolidColorBrush(); BindingOperations.SetBinding(b, SolidColorBrush.ColorProperty, new Binding("Value") { Source = color }); palette.Add(hex, b); colors.Add(hex, color); }
            return b;
        }
        public static void ApplyTheme(string theme)
        {
            ThemeManager.Apply(theme);
        }
        internal static void RefreshPalette(string theme) { foreach (var pair in colors) pair.Value.Value = ThemeTokens.ColorFor(pair.Key, theme); }
        public static void InstallResources(Application app)
        {
            ThemeManager.Install(app);
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
            if (primary) { b.SetResourceReference(FrameworkElement.StyleProperty, "PrimaryButton"); }
            b.Click += (s, e) => click();
            b.PreviewMouseLeftButtonDown += (s, e) => UiMotion.Press(b, true);
            b.PreviewMouseLeftButtonUp += (s, e) => UiMotion.Press(b, false);
            b.MouseLeave += (s, e) => UiMotion.Press(b, false);
            b.LostMouseCapture += (s, e) => UiMotion.Press(b, false);
            b.PreviewKeyDown += (s, e) => { if (e.Key == System.Windows.Input.Key.Space || e.Key == System.Windows.Input.Key.Enter) UiMotion.Press(b, true); };
            b.PreviewKeyUp += (s, e) => { if (e.Key == System.Windows.Input.Key.Space || e.Key == System.Windows.Input.Key.Enter) UiMotion.Press(b, false); };
            b.LostKeyboardFocus += (s, e) => UiMotion.Press(b, false);
            b.IsVisibleChanged += (s, e) => { if (!b.IsVisible) UiMotion.Stop(b); };
            if (primary) MagneticMotion.Attach(b, UiMotion.MotionTokens.PrimaryMagneticMax);
            return b;
        }
        public static StackPanel Stack(params UIElement[] items) { var p = new StackPanel(); foreach (var item in items) p.Children.Add(item); return p; }
        public static WrapPanel Row(params UIElement[] items) { var p = new WrapPanel(); foreach (var item in items) p.Children.Add(item); return p; }
        public static Border Card(UIElement child) { var card = new Border { Background = Brush("CardBackground"), BorderBrush = Brush("CardBorder"), BorderThickness = new Thickness(1), CornerRadius = new CornerRadius(10), Padding = new Thickness(22), Margin = new Thickness(0, 0, 0, 16), Child = child }; card.MouseEnter += (s, e) => card.Background = Brush("CardHoverBackground"); card.MouseLeave += (s, e) => card.Background = Brush("CardBackground"); return card; }
        public static Button PrimaryButton(string title, Action action) { return Button(title, action, true); }
        public static Button SecondaryButton(string title, Action action) { return Button(title, action); }
        public static Button DangerButton(string title, Action action) { var b = Button(title, action); b.SetResourceReference(FrameworkElement.StyleProperty, "DangerButton"); return b; }
        public static TextBlock SectionTitle(string title) { return Text(title, 20); }
        public static FrameworkElement InfoTip(string title, string explanation) { return UiFeedback.Tip(title, explanation); }
        public static FrameworkElement WarningTip(string title, string explanation) { return UiFeedback.Tip(title, explanation, true); }
        public static Button IconButton(string icon, string title, Action action) { var b = Button(icon, action); b.ToolTip = title; return b; }
        public static UiTabStrip TabStrip(params Button[] tabs) { return new UiTabStrip(tabs); }
        public static Expander Expander(string title, UIElement content, bool expanded = false) { return new Expander { Header = title, Content = content, IsExpanded = expanded }; }
        public static Border StatusBar(string text) { var card = Card(Text(text, 12, Muted)); card.Padding = new Thickness(12, 8, 12, 8); return card; }
        public static ScrollViewer Scroll(UIElement child) { return new ScrollViewer { Content = child, VerticalScrollBarVisibility = ScrollBarVisibility.Auto, HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled, Padding = new Thickness(0, 0, 12, 0) }; }
        public static ComboBox Combo(string[] values, int index = 0) { return new ElasticComboBox { ItemsSource = values, SelectedIndex = index, MinWidth = 160, Margin = new Thickness(0, 0, 0, 12) }; }
        public static TextBox Input(string value = "") { return new TextBox { Text = value, Margin = new Thickness(0, 0, 0, 12) }; }
        public static CheckBox Toggle(string title, bool state, Action<bool> change) { var c = new ElasticCheckBox { Content = title, IsChecked = state }; c.Click += (s, e) => { bool value = c.IsChecked == true; try { change(value); } catch { c.IsChecked = !value; } }; return c; }
        public static void Enter(FrameworkElement page, bool drawer = false)
        {
            UiMotion.Enter(page, drawer);
        }
        public static void Stop(FrameworkElement element)
        {
            UiMotion.Stop(element);
        }
    }
}
