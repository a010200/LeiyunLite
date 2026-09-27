using System;
using System.Drawing;
using System.Windows.Forms;
using RazerBatteryTray.Macros;

namespace RazerBatteryTray
{
    internal sealed class MacroBindingDialog : Form
    {
        public MacroBinding Result { get; private set; }
        public MacroBindingDialog(MacroLibrary library, MacroBinding original = null)
        {
            Text = Theme.Title + " · 按键绑定"; Font = new Font("Microsoft YaHei UI", 9F);
            AutoScaleMode = AutoScaleMode.Dpi; ClientSize = new Size(620, 450); StartPosition = FormStartPosition.CenterParent;
            MinimumSize = Size;
            var grid = new TableLayoutPanel { Dock = DockStyle.Fill, Padding = new Padding(18), ColumnCount = 2, RowCount = 9 };
            grid.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 135)); grid.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
            Controls.Add(grid);
            var macro = new ComboBox { DropDownStyle = ComboBoxStyle.DropDownList, Width = 340 };
            foreach (var item in library.Macros) macro.Items.Add(item);
            if (macro.Items.Count > 0) macro.SelectedIndex = 0;
            var trigger = MacroLabels.Combo(MacroLabels.Triggers); var key = MacroLabels.KeysCombo();
            var mode = MacroLabels.Combo(MacroLabels.Modes);
            var modifiers = new FlowLayoutPanel { AutoSize = true, Dock = DockStyle.Fill };
            var ctrl = new CheckBox { Text = "Ctrl", AutoSize = true }; var shift = new CheckBox { Text = "Shift", AutoSize = true };
            var alt = new CheckBox { Text = "Alt", AutoSize = true }; var win = new CheckBox { Text = "Win", AutoSize = true };
            modifiers.Controls.AddRange(new Control[] { ctrl, shift, alt, win });
            var suppress = new CheckBox { Text = "拦截原按键 / 滚轮的默认动作", AutoSize = true };
            var enabled = new CheckBox { Text = "启用这个绑定", Checked = true, AutoSize = true };
            Control[] inputs = { macro, trigger, key, modifiers, mode, suppress, enabled };
            string[] labels = { "执行哪个宏", "触发方式", "键盘键", "组合修饰键", "执行模式", "原始输入", "状态" };
            for (int i = 0; i < inputs.Length; i++)
            {
                grid.RowStyles.Add(new RowStyle(SizeType.Absolute, 38));
                grid.Controls.Add(new Label { Text = labels[i], AutoSize = true, Padding = new Padding(0, 5, 0, 0) }, 0, i);
                grid.Controls.Add(inputs[i], 1, i);
            }
            var hint = new Label { Text = "绑定对所有鼠标 / 键盘生效，软件退出后失效。\nCtrl+Shift+F12：立即停止所有宏。编辑宏时绑定暂停。\n滚轮支持执行一次或开关执行，不支持按住模式。", Dock = DockStyle.Fill };
            grid.RowStyles.Add(new RowStyle(SizeType.Percent, 100)); grid.Controls.Add(hint, 0, 7); grid.SetColumnSpan(hint, 2);
            var footer = new FlowLayoutPanel { Dock = DockStyle.Fill, FlowDirection = FlowDirection.RightToLeft };
            var ok = new Button { Text = "确定", Width = 90 }; var cancel = new Button { Text = "取消", Width = 90, DialogResult = DialogResult.Cancel };
            footer.Controls.Add(ok); footer.Controls.Add(cancel); grid.Controls.Add(footer, 0, 8); grid.SetColumnSpan(footer, 2);
            grid.RowStyles.Add(new RowStyle(SizeType.Absolute, 40)); CancelButton = cancel;
            if (original != null)
            {
                foreach (MacroDefinition item in macro.Items) if (item.Id == original.MacroId) macro.SelectedItem = item;
                trigger.SelectedIndex = (int)original.Trigger; MacroLabels.SelectKey(key, original.KeyCode);
                mode.SelectedIndex = (int)original.Mode; enabled.Checked = original.Enabled; suppress.Checked = original.SuppressOriginal;
                ctrl.Checked = (original.Modifiers & KeyModifiers.Control) != 0; shift.Checked = (original.Modifiers & KeyModifiers.Shift) != 0;
                alt.Checked = (original.Modifiers & KeyModifiers.Alt) != 0; win.Checked = (original.Modifiers & KeyModifiers.Windows) != 0;
            }
            else MacroLabels.SelectKey(key, 117);
            Action changed = () => { key.Enabled = trigger.SelectedIndex == 0; if (trigger.SelectedIndex >= 6 && mode.SelectedIndex == 1) mode.SelectedIndex = 0; };
            trigger.SelectedIndexChanged += (s, e) => changed(); mode.SelectedIndexChanged += (s, e) => changed(); changed();
            ok.Click += (s, e) => {
                if (macro.SelectedItem == null || key.SelectedItem == null) return;
                Result = new MacroBinding { Id = original == null ? Guid.NewGuid().ToString("N") : original.Id,
                    MacroId = ((MacroDefinition)macro.SelectedItem).Id, Trigger = (TriggerKind)trigger.SelectedIndex,
                    KeyCode = ((MacroLabels.KeyChoice)key.SelectedItem).Code, Mode = (RunMode)mode.SelectedIndex,
                    SuppressOriginal = suppress.Checked, Enabled = enabled.Checked,
                    Modifiers = (ctrl.Checked ? KeyModifiers.Control : 0) | (shift.Checked ? KeyModifiers.Shift : 0) |
                        (alt.Checked ? KeyModifiers.Alt : 0) | (win.Checked ? KeyModifiers.Windows : 0) };
                var draft = library.Clone(); draft.Bindings.RemoveAll(b => b.Id == Result.Id); draft.Bindings.Add(Result);
                try { MacroValidation.Validate(draft); DialogResult = DialogResult.OK; }
                catch (Exception ex) { MessageBox.Show(this, ex.Message, Theme.Title); }
            };
            Theme.Apply(this); ok.BackColor = Theme.Green; ok.ForeColor = Theme.Black;
        }
    }
}
