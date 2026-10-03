using System;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Media;
using System.Windows.Media.Animation;

namespace RazerBatteryTray.Desktop
{
    internal sealed class ElasticComboBox : ComboBox
    {
        private Popup popup;
        private int generation;
        internal ElasticComboBox() { SetResourceReference(StyleProperty, typeof(ComboBox)); }
        public override void OnApplyTemplate()
        {
            if (popup != null) popup.Opened -= PopupOpened;
            base.OnApplyTemplate();
            popup = Template.FindName("PART_Popup", this) as Popup;
            if (popup != null) popup.Opened += PopupOpened;
        }
        private void PopupOpened(object sender, EventArgs e)
        {
            int request = ++generation;
            Dispatcher.BeginInvoke(System.Windows.Threading.DispatcherPriority.Loaded, new Action(() => { if (request == generation && IsDropDownOpen) ElasticDropdown.Open(this); }));
        }
        protected override void OnDropDownClosed(EventArgs e) { generation++; ElasticDropdown.Close(this); base.OnDropDownClosed(e); }
    }
    internal static class ElasticDropdown
    {
        private static T Find<T>(DependencyObject root, string name) where T : FrameworkElement
        {
            if (root == null) return null;
            var element = root as T; if (element != null && element.Name == name) return element;
            // Popup.Opened may precede the first visual layout; Border.Child is already available.
            var border = root as Border;
            if (border != null && border.Child != null) { var found = Find<T>(border.Child, name); if (found != null) return found; }
            for (int i = 0; i < VisualTreeHelper.GetChildrenCount(root); i++) { var found = Find<T>(VisualTreeHelper.GetChild(root, i), name); if (found != null) return found; }
            return null;
        }
        internal static void Open(ComboBox combo)
        {
            var popup = combo.Template.FindName("PART_Popup", combo) as Popup;
            if (popup == null) return;
                var surface = combo.Template.FindName("DropSurface", combo) as Border ?? Find<Border>(popup.Child, "DropSurface");
                if (surface == null) return;
                // Never mutate a shared template Freezable.
                var move = new TranslateTransform(); surface.RenderTransform = move;
                surface.BeginAnimation(UIElement.OpacityProperty, null); surface.Opacity = 1;
                if (Ui.Motion)
                {
                    surface.UpdateLayout();
                    bool above = PresentationSource.FromVisual(surface) != null && PresentationSource.FromVisual(combo) != null && surface.PointToScreen(new Point()).Y < combo.PointToScreen(new Point()).Y;
                    UiMotion.To(move, TranslateTransform.YProperty, 0, UiMotion.Normal, above ? 5 : -5);
                    UiMotion.Fade(surface, .35, UiMotion.Fast);
                }
                Arrow(combo, true);
        }
        internal static void Close(ComboBox combo)
        {
            var popup = combo.Template.FindName("PART_Popup", combo) as Popup;
            if (popup != null) { var surface = Find<Border>(popup.Child, "DropSurface"); if (surface != null) Ui.Stop(surface); }
            Arrow(combo, false);
        }
        private static void Arrow(ComboBox combo, bool open)
        {
            var arrow = Find<FrameworkElement>(combo, "DropArrow"); if (arrow == null) return;
            var old = arrow.RenderTransform as RotateTransform; double from = old == null ? 0 : old.Angle;
            var rotate = new RotateTransform(open ? 180 : 0); arrow.RenderTransformOrigin = new Point(.5, .5); arrow.RenderTransform = rotate;
            UiMotion.To(rotate, RotateTransform.AngleProperty, rotate.Angle, UiMotion.Normal, from);
        }
    }
}
