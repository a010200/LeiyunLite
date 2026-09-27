using System;
using System.Windows;
using System.Windows.Media;

namespace RazerBatteryTray.Desktop
{
    internal sealed class MouseOrientation : FrameworkElement
    {
        public static readonly DependencyProperty AngleProperty = DependencyProperty.Register("Angle", typeof(double), typeof(MouseOrientation), new FrameworkPropertyMetadata(0.0, FrameworkPropertyMetadataOptions.AffectsRender));
        public double Angle { get { return (double)GetValue(AngleProperty); } set { SetValue(AngleProperty, value); } }
        public MouseOrientation() { Height = 188; }
        protected override void OnRender(DrawingContext dc)
        {
            base.OnRender(dc); double x = ActualWidth / 2, y = ActualHeight / 2;
            var axis = new Pen(Ui.Brush("#666666"), 1) { DashStyle = DashStyles.Dot };
            dc.DrawLine(axis, new Point(x - 98, y), new Point(x + 98, y)); dc.DrawLine(axis, new Point(x, 2), new Point(x, ActualHeight - 2));
            dc.PushTransform(new TranslateTransform(x, y)); dc.PushTransform(new RotateTransform(Angle));
            var body = Geometry.Parse("M -34,-45 C -35,-68 35,-68 34,-45 L 37,32 C 38,78 -38,78 -37,32 Z");
            dc.DrawGeometry(Ui.Brush("#191919"), new Pen(Ui.Brush("#CCCCCC"), 5), body);
            dc.DrawLine(new Pen(Ui.Brush("#606060"), 1.5), new Point(0, -58), new Point(0, -13));
            dc.DrawRoundedRectangle(Ui.Accent, null, new Rect(-3, -41, 6, 20), 3, 3);
            dc.Pop(); dc.Pop();
        }
    }
}
