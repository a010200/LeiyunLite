using System;
using System.Drawing;
using System.Windows.Forms;
using RazerBatteryTray.Macros;

namespace RazerBatteryTray
{
    internal sealed class MacroStepDialog : Form
    {
        private readonly ComboBox kind = MacroLabels.Combo(MacroLabels.Actions);
        private readonly ComboBox key = MacroLabels.KeysCombo();
        private readonly ComboBox mouse = MacroLabels.Combo(MacroLabels.Mice);
        private readonly ComboBox press = MacroLabels.Combo(MacroLabels.Presses);
        private readonly ComboBox targetMacro = new ComboBox { DropDownStyle = ComboBoxStyle.DropDownList, Width = 280 };
        private readonly NumericUpDown number = new NumericUpDown { Minimum = 0, Maximum = 600000, Width = 280 };
        private readonly TextBox value = new TextBox { Multiline = true, ScrollBars = ScrollBars.Vertical, Height = 100, Dock = DockStyle.Fill };
        private readonly TextBox arguments = new TextBox { Dock = DockStyle.Fill };
        private readonly Label numberLabel = new Label { AutoSize = true, Text = "毫秒 / 次数" };
        private readonly Label valueLabel = new Label { AutoSize = true, Text = "文本 / 目标路径" };
        private readonly Label help = new Label { AutoSize = false, Height = 60, Dock = DockStyle.Fill };
        public MacroStep Result { get; private set; }
        public MacroStepDialog(MacroLibrary library, string currentId, MacroStep step = null)
        {
            Text = Theme.Title + " · 编辑动作";
            Font = new Font("Microsoft YaHei UI", 9F); AutoScaleMode = AutoScaleMode.Dpi;
            ClientSize = new Size(620, 550); MinimumSize = Size; StartPosition = FormStartPosition.CenterParent;
            var layout = new TableLayoutPanel { Dock = DockStyle.Fill, Padding = new Padding(18), ColumnCount = 2, RowCount = 11 };
            layout.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 145)); layout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
            Controls.Add(layout);
            Add(layout, "动作类型", kind, 0); Add(layout, "键盘键", key, 1); Add(layout, "鼠标动作", mouse, 2); Add(layout, "操作方式", press, 3);
            layout.Controls.Add(numberLabel, 0, 4); layout.Controls.Add(number, 1, 4);
            Add(layout, "被调用的宏", targetMacro, 5);
            foreach (var macro in library.Macros) if (macro.Id != currentId) targetMacro.Items.Add(macro);
            if (targetMacro.Items.Count > 0) targetMacro.SelectedIndex = 0;
            layout.Controls.Add(valueLabel, 0, 6); layout.Controls.Add(value, 1, 6); layout.RowStyles.Clear();
            for (int i = 0; i < 6; i++) layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 35));
            layout.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
            layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 35)); layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 38));
            layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 62)); layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 40));
            Add(layout, "参数（可选）", arguments, 7);
            var browse = new Button { Text = "选择程序或文件…", AutoSize = true };
            browse.Click += (s, e) => { using (var dialog = new OpenFileDialog()) if (dialog.ShowDialog(this) == DialogResult.OK) value.Text = dialog.FileName; };
            layout.Controls.Add(browse, 1, 8);
            layout.Controls.Add(help, 0, 9); layout.SetColumnSpan(help, 2);
            var buttons = new FlowLayoutPanel { Dock = DockStyle.Fill, FlowDirection = FlowDirection.RightToLeft };
            var ok = new Button { Text = "确定", Width = 90 }; var cancel = new Button { Text = "取消", DialogResult = DialogResult.Cancel, Width = 90 };
            buttons.Controls.Add(ok); buttons.Controls.Add(cancel); layout.Controls.Add(buttons, 0, 10); layout.SetColumnSpan(buttons, 2);
            CancelButton = cancel;
            if (step != null)
            {
                kind.SelectedIndex = (int)step.Kind; key.SelectedItem = null; MacroLabels.SelectKey(key, step.KeyCode);
                mouse.SelectedIndex = (int)step.Mouse; press.SelectedIndex = (int)step.Press;
                number.Value = Math.Max(0, Math.Min(600000, step.Number)); value.Text = step.Value; arguments.Text = step.Arguments;
                foreach (MacroDefinition macro in targetMacro.Items) if (macro.Id == step.Value) targetMacro.SelectedItem = macro;
            }
            else number.Value = 100;
            kind.SelectedIndexChanged += (s, e) => {
                if ((ActionKind)kind.SelectedIndex == ActionKind.Mouse || (ActionKind)kind.SelectedIndex == ActionKind.LoopStart) number.Value = 1;
                UpdateFields();
            };
            UpdateFields();
            ok.Click += (s, e) => {
                var selectedKind = (ActionKind)kind.SelectedIndex;
                if (selectedKind == ActionKind.CallMacro && targetMacro.SelectedItem == null) { MessageBox.Show(this, "请先创建另一个可调用的宏。"); return; }
                Result = new MacroStep { Kind = selectedKind, KeyCode = key.SelectedItem == null ? 65 : ((MacroLabels.KeyChoice)key.SelectedItem).Code,
                    Mouse = (MouseAction)mouse.SelectedIndex, Press = (PressMode)press.SelectedIndex, Number = (int)number.Value,
                    Value = selectedKind == ActionKind.CallMacro ? ((MacroDefinition)targetMacro.SelectedItem).Id : value.Text,
                    Arguments = arguments.Text };
                // Validate a temporary macro, balancing a standalone loop marker for editing.
                var validation = library.Clone(); var current = validation.Find(currentId); current.Steps.Clear(); current.Steps.Add(Result);
                if (Result.Kind == ActionKind.LoopStart) current.Steps.Add(new MacroStep { Kind = ActionKind.LoopEnd });
                if (Result.Kind == ActionKind.LoopEnd) current.Steps.Insert(0, new MacroStep { Kind = ActionKind.LoopStart, Number = 1 });
                // Full-library validation runs when saving; other drafts can have unfinished loops.
                var single = new MacroLibrary(); single.Macros.Add(current);
                if (Result.Kind != ActionKind.CallMacro)
                {
                    try { MacroValidation.Validate(single); } catch (Exception ex) { MessageBox.Show(this, ex.Message, Theme.Title); return; }
                }
                DialogResult = DialogResult.OK;
            };
            Theme.Apply(this);
            ok.BackColor = Theme.Green; ok.ForeColor = Theme.Black;
        }
        private void UpdateFields()
        {
            var type = (ActionKind)kind.SelectedIndex;
            key.Enabled = type == ActionKind.Keyboard;
            mouse.Enabled = type == ActionKind.Mouse;
            press.Enabled = type == ActionKind.Keyboard || type == ActionKind.Mouse;
            number.Enabled = type == ActionKind.Delay || type == ActionKind.Mouse || type == ActionKind.LoopStart;
            numberLabel.Text = type == ActionKind.Delay ? "延迟（毫秒）" : type == ActionKind.LoopStart ? "循环次数" : "滚轮格数";
            if (type == ActionKind.Mouse && number.Value > 1000) number.Value = 1;
            targetMacro.Enabled = type == ActionKind.CallMacro;
            value.Enabled = type == ActionKind.Text || type == ActionKind.Launch || type == ActionKind.Command;
            arguments.Enabled = type == ActionKind.Launch || type == ActionKind.Command;
            help.Text = type == ActionKind.LoopStart || type == ActionKind.LoopEnd ? "用“循环开始”和“循环结束”包围需要重复的动作。保存时会检查配对。" :
                type == ActionKind.Command ? "填写可执行程序（如 cmd.exe）和参数（如 /c echo hello）。命令启动后不会等待其退出。" :
                type == ActionKind.Keyboard ? "组合键：例如 Ctrl 按下 → C 点按 → Ctrl 松开。按键事件不会自动记录。" :
                type == ActionKind.Text ? "直接输入 Unicode 文本，不占用剪贴板。请在运行前让目标输入框获得焦点。" :
                "动作按列表顺序执行；延迟、循环和调用其他宏可自由组合。";
        }
        private static void Add(TableLayoutPanel layout, string text, Control control, int row)
        { layout.Controls.Add(new Label { Text = text, AutoSize = true, Padding = new Padding(0, 5, 0, 0) }, 0, row); layout.Controls.Add(control, 1, row); }
    }
}
