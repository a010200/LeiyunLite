using System.Drawing;
using System.Windows.Forms;
using System.Runtime.InteropServices;

namespace RazerBatteryTray
{
    internal static class Theme
    {
        public static readonly Color Green = ColorTranslator.FromHtml("#44D62C");
        public static readonly Color Black = ColorTranslator.FromHtml("#141414");
        public static readonly Color Border = Color.FromArgb(62, 62, 62);
        public const string Title = AppVersion.DisplayName;
        [DllImport("dwmapi.dll")] private static extern int DwmSetWindowAttribute(System.IntPtr window, int attribute, ref int value, int size);
        private static void ApplyFrame(Form form)
        {
            try
            {
                int enabled = 1, black = 0x141414, white = 0xFFFFFF;
                DwmSetWindowAttribute(form.Handle, 20, ref enabled, 4);
                DwmSetWindowAttribute(form.Handle, 35, ref black, 4);
                DwmSetWindowAttribute(form.Handle, 36, ref white, 4);
            }
            catch { /* Older Windows versions retain the native frame. */ }
        }

        public static void Apply(Control control)
        {
            control.BackColor = Black;
            control.ForeColor = Color.WhiteSmoke;
            var form = control as Form;
            if (form != null)
            {
                try { form.Icon = Icon.ExtractAssociatedIcon(Application.ExecutablePath); } catch { }
                form.HandleCreated += (s, e) => ApplyFrame(form);
                if (form.IsHandleCreated) ApplyFrame(form);
            }
            var button = control as Button;
            if (button != null)
            {
                button.FlatStyle = FlatStyle.Flat;
                button.FlatAppearance.BorderColor = Border;
                button.FlatAppearance.MouseOverBackColor = Color.FromArgb(45, 45, 45);
            }
            foreach (Control child in control.Controls) Apply(child);
        }
    }
}
