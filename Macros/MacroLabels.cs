using System;
using System.Windows.Forms;
using RazerBatteryTray.Macros;

namespace RazerBatteryTray
{
    internal static class MacroLabels
    {
        public static readonly string[] Actions = { "延迟", "键盘功能", "鼠标功能", "调用宏", "启动", "运行命令", "文本功能", "循环开始", "循环结束" };
        public static readonly string[] Presses = { "点按 / 点击", "按下", "松开" };
        public static readonly string[] Mice = { "左键", "右键", "中键", "侧键 X1（后退）", "侧键 X2（前进）", "滚轮向上", "滚轮向下" };
        public static readonly string[] Triggers = { "键盘键 / 组合键", "鼠标左键", "鼠标右键", "鼠标中键", "侧键 X1（后退）", "侧键 X2（前进）", "滚轮向上", "滚轮向下" };
        public static readonly string[] Modes = { "执行一次", "按住重复，松开停止", "触发开始，再次触发停止" };
        public static string KeyName(int key) { return ((Keys)key).ToString(); }
        public static string Binding(MacroBinding binding)
        {
            string prefix = "";
            if ((binding.Modifiers & KeyModifiers.Control) != 0) prefix += "Ctrl+";
            if ((binding.Modifiers & KeyModifiers.Shift) != 0) prefix += "Shift+";
            if ((binding.Modifiers & KeyModifiers.Alt) != 0) prefix += "Alt+";
            if ((binding.Modifiers & KeyModifiers.Windows) != 0) prefix += "Win+";
            return prefix + (binding.Trigger == TriggerKind.Keyboard ? KeyName(binding.KeyCode) : Triggers[(int)binding.Trigger]);
        }
        public static string Detail(MacroStep step, MacroLibrary library)
        {
            switch (step.Kind)
            {
                case ActionKind.Delay: return step.Number + " ms";
                case ActionKind.Keyboard: return KeyName(step.KeyCode) + " · " + Presses[(int)step.Press];
                case ActionKind.Mouse: return Mice[(int)step.Mouse] + " · " +
                    (step.Mouse >= MouseAction.WheelUp ? step.Number + " 格" : Presses[(int)step.Press]);
                case ActionKind.CallMacro: var target = library.Find(step.Value); return target == null ? "（宏不存在）" : target.Name;
                case ActionKind.LoopStart: return "重复 " + step.Number + " 次";
                case ActionKind.LoopEnd: return "返回对应的循环开始";
                case ActionKind.Text: return (step.Value ?? "").Replace("\r", "").Replace("\n", " ↵ ");
                default: return step.Value + " " + step.Arguments;
            }
        }
        public static ComboBox Combo(string[] items)
        {
            var box = new ComboBox { DropDownStyle = ComboBoxStyle.DropDownList, Width = 280 };
            box.Items.AddRange(items); box.SelectedIndex = 0; return box;
        }
        public static ComboBox KeysCombo()
        {
            var box = new ComboBox { DropDownStyle = ComboBoxStyle.DropDownList, Width = 280 };
            // Unique virtual-key codes, with readable names supplied by WinForms.
            for (int key = 8; key <= 254; key++)
                if (Enum.IsDefined(typeof(Keys), key)) box.Items.Add(new KeyChoice(key));
            SelectKey(box, 65); return box;
        }
        public static void SelectKey(ComboBox box, int key)
        {
            foreach (KeyChoice item in box.Items) if (item.Code == key) { box.SelectedItem = item; return; }
        }
        public sealed class KeyChoice
        {
            public readonly int Code;
            public KeyChoice(int code) { Code = code; }
            public override string ToString() { return KeyName(Code); }
        }
    }
}
