using System;
using System.Collections.Generic;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Shapes;
using RazerBatteryTray.Macros;

namespace RazerBatteryTray.Desktop
{
    // Original, device-independent schematic. It does not imply onboard or per-device mapping.
    internal sealed class MouseBindingMap : Viewbox
    {
        internal const string MacroFormat = "LeiyunLite.SavedMacroId";
        private readonly Dictionary<TriggerKind, TextBlock> names = new Dictionary<TriggerKind, TextBlock>();
        private readonly Dictionary<TriggerKind, TextBlock> details = new Dictionary<TriggerKind, TextBlock>();
        private readonly Dictionary<TriggerKind, Button> labels = new Dictionary<TriggerKind, Button>();
        private readonly Func<string, bool> canBind;
        internal event Action<TriggerKind, string> Assign;
        internal MouseBindingMap(Func<string, bool> canBind)
        {
            this.canBind = canBind; Stretch = Stretch.Uniform; StretchDirection = StretchDirection.DownOnly;
            HorizontalAlignment = HorizontalAlignment.Stretch; VerticalAlignment = VerticalAlignment.Center;
            var scene = new Canvas { Width = 640, Height = 470 }; Child = scene;
            scene.Children.Add(new MouseSchematic { Width = 640, Height = 470, IsHitTestVisible = false });
            Add(scene, TriggerKind.Left, 12, 146, 278, 162, false);
            Add(scene, TriggerKind.X2, 12, 242, 243, 237, false);
            Add(scene, TriggerKind.X1, 12, 338, 242, 277, false);
            Add(scene, TriggerKind.WheelUp, 448, 35, 320, 113, true);
            Add(scene, TriggerKind.Right, 448, 131, 362, 162, true);
            Add(scene, TriggerKind.WheelDown, 448, 227, 320, 167, true);
            Add(scene, TriggerKind.Middle, 448, 323, 320, 140, true);
        }
        internal static string DefaultName(TriggerKind key)
        {
            switch (key) {
                case TriggerKind.Left: return Ui.T("左键点击", "Left click");
                case TriggerKind.Right: return Ui.T("右键点击", "Right click");
                case TriggerKind.Middle: return Ui.T("中键点击", "Middle click");
                case TriggerKind.X1: return Ui.T("侧键 X1 · 后退", "Side X1 · Back");
                case TriggerKind.X2: return Ui.T("侧键 X2 · 前进", "Side X2 · Forward");
                case TriggerKind.WheelUp: return Ui.T("向上滚动", "Scroll up");
                default: return Ui.T("向下滚动", "Scroll down");
            }
        }
        private void Add(Canvas scene, TriggerKind key, double x, double y, double px, double py, bool right)
        {
            var line = new Polyline { Stroke = Ui.Border, StrokeThickness = 1.2, IsHitTestVisible = false };
            double start = right ? x : x + 180, elbow = right ? 426 : 213;
            line.Points = new PointCollection { new Point(start, y + 30), new Point(elbow, y + 30), new Point(elbow, py), new Point(px, py) }; scene.Children.Add(line);
            var title = Ui.Text(DefaultName(key), 15); title.TextWrapping = TextWrapping.NoWrap; title.TextTrimming = TextTrimming.CharacterEllipsis; title.Margin = new Thickness(0, 0, 0, 4);
            var detail = Ui.Text(Ui.T("原始功能", "Original input"), 11, Ui.Muted); detail.Margin = new Thickness(0); detail.TextWrapping = TextWrapping.NoWrap; detail.TextTrimming = TextTrimming.CharacterEllipsis;
            var label = Ui.Button("", () => Request(key, null)); label.Content = Ui.Stack(title, detail); label.Width = 180; label.Height = 65; label.Margin = new Thickness(0); label.Padding = new Thickness(10, 7, 10, 7); label.HorizontalContentAlignment = HorizontalAlignment.Left;
            names[key] = title; details[key] = detail; labels[key] = label;
            Canvas.SetLeft(label, x); Canvas.SetTop(label, y); scene.Children.Add(label);
            var pin = Ui.Button("", () => Request(key, null)); pin.Width = pin.Height = 26; pin.Padding = new Thickness(0); pin.Margin = new Thickness(0); pin.Background = Ui.Brush("#141414");
            pin.Content = new Ellipse { Width = 7, Height = 7, Fill = Ui.Accent }; pin.ToolTip = DefaultName(key);
            Canvas.SetLeft(pin, px - 13); Canvas.SetTop(pin, py - 13); scene.Children.Add(pin);
            foreach (var target in new[] { label, pin }) {
                target.AllowDrop = true;
                target.DragOver += (s, e) => {
                    var id = e.Data.GetDataPresent(MacroFormat) ? e.Data.GetData(MacroFormat) as string : null;
                    bool valid = id != null && canBind(id); e.Effects = valid ? DragDropEffects.Link : DragDropEffects.None; e.Handled = true;
                    label.BorderBrush = valid ? Ui.Accent : Ui.Border; label.BorderThickness = new Thickness(valid ? 2 : 1);
                };
                target.DragLeave += (s, e) => { label.BorderBrush = Ui.Border; label.BorderThickness = new Thickness(1); };
                target.Drop += (s, e) => {
                    label.BorderBrush = Ui.Border; label.BorderThickness = new Thickness(1);
                    var id = e.Data.GetDataPresent(MacroFormat) ? e.Data.GetData(MacroFormat) as string : null;
                    e.Effects = id != null && canBind(id) ? DragDropEffects.Link : DragDropEffects.None; e.Handled = true;
                    if (e.Effects != DragDropEffects.None) Request(key, id);
                };
                System.Windows.Automation.AutomationProperties.SetName(target, DefaultName(key));
            }
        }
        private void Request(TriggerKind key, string id) { var handler = Assign; if (handler != null) handler(key, id); }
        internal void Refresh(MacroLibrary active)
        {
            foreach (var key in names.Keys) {
                var binding = active.Bindings.Find(b => b.Trigger == key && b.Modifiers == KeyModifiers.None);
                bool running = binding != null && binding.Enabled && active.BindingsEnabled;
                var macro = binding == null ? null : active.Find(binding.MacroId);
                names[key].Text = running && macro != null ? macro.Name : DefaultName(key);
                names[key].Foreground = running ? Ui.Accent : Ui.Foreground;
                details[key].Text = running ? DefaultName(key) + " · " + (binding.SuppressOriginal ? Ui.T("替代", "Replace") : Ui.T("保留", "Keep")) : binding != null ? Ui.T("已停用 · ", "Disabled · ") + (macro == null ? "—" : macro.Name) : Ui.T("原始功能", "Original input");
                if (active.Bindings.Exists(b => b.Trigger == key && b.Modifiers != KeyModifiers.None)) details[key].Text += Ui.T(" · 另有组合绑定", " · Modified bindings");
                labels[key].ToolTip = names[key].Text + "\n" + details[key].Text + "\n" + Ui.T("点击编辑，或把左侧宏拖到这里。", "Click to edit, or drop a macro here.");
            }
        }
    }
    internal sealed class MouseSchematic : FrameworkElement
    {
        protected override void OnRender(DrawingContext dc)
        {
            var body = Geometry.Parse("M 263,81 C 281,61 359,61 380,81 C 405,108 404,173 400,224 C 400,266 419,337 394,382 C 367,430 275,430 250,382 C 227,341 242,283 240,236 C 238,184 237,111 263,81 Z");
            var shade = new LinearGradientBrush(); shade.StartPoint = new Point(0, 0); shade.EndPoint = new Point(1, .8);
            shade.GradientStops.Add(new GradientStop(Color.FromRgb(126, 130, 134), 0)); shade.GradientStops.Add(new GradientStop(Color.FromRgb(73, 78, 82), .45)); shade.GradientStops.Add(new GradientStop(Color.FromRgb(37, 41, 44), 1));
            dc.DrawGeometry(shade, new Pen(Ui.Brush("#909699"), 1.4), body);
            var seam = new Pen(Ui.Brush("#272b2d"), 2);
            dc.DrawLine(seam, new Point(320, 74), new Point(320, 215));
            dc.DrawGeometry(null, seam, Geometry.Parse("M 243,217 Q 320,232 401,217"));
            dc.DrawRoundedRectangle(Ui.Brush("#151718"), new Pen(Ui.Brush("#a0a4a6"), 1), new Rect(307, 100, 26, 80), 11, 11);
            for (int i = 0; i < 9; i++) dc.DrawLine(new Pen(Ui.Brush("#666c70"), 1.4), new Point(312, 111 + 7 * i), new Point(328, 111 + 7 * i));
            dc.DrawRoundedRectangle(Ui.Brush("#242829"), new Pen(Ui.Brush("#737a7d"), 1), new Rect(234, 218, 9, 34), 4, 4);
            dc.DrawRoundedRectangle(Ui.Brush("#242829"), new Pen(Ui.Brush("#737a7d"), 1), new Rect(234, 262, 9, 32), 4, 4);
            dc.DrawLine(new Pen(Ui.Accent, 3), new Point(315, 355), new Point(325, 355));
        }
    }
}
