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
                var move = UiMotion.Transform(surface).Entry;
                // Popup has a separate HWND/visual tree; lifecycle still belongs
                // to the ComboBox's app window, including deactivation.
                SpringMotion.WindowContext(surface, combo);
                surface.BeginAnimation(UIElement.OpacityProperty, null); surface.Opacity = 1;
                if (Ui.Motion)
                {
                    surface.UpdateLayout();
                    bool above = PresentationSource.FromVisual(surface) != null && PresentationSource.FromVisual(combo) != null && surface.PointToScreen(new Point()).Y < combo.PointToScreen(new Point()).Y;
                    UiMotion.SpringTo(surface, move, TranslateTransform.YProperty, 0, SpringPreset.Smooth, above ? UiMotion.MotionTokens.DropdownOffset : -UiMotion.MotionTokens.DropdownOffset, rest: 0);
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
            var rotate = arrow.RenderTransform as RotateTransform;
            if (rotate == null || rotate.IsFrozen) { rotate = new RotateTransform(); arrow.RenderTransformOrigin = new Point(.5, .5); arrow.RenderTransform = rotate; }
            UiMotion.SpringTo(combo, rotate, RotateTransform.AngleProperty, open ? 180 : 0, SpringPreset.Snappy);
        }
    }
}
