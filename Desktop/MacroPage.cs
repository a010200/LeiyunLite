using System;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using RazerBatteryTray.Macros;

namespace RazerBatteryTray.Desktop
{
    internal sealed partial class MacroPage : UserControl
    {
        private readonly ShellWindow shell;
        private readonly ComboBox library = new ElasticComboBox { MinWidth = 190, MaxWidth = 310, Margin = new Thickness(0, 0, 8, 8) };
        private readonly ListBox steps = new ListBox(), bindings = new ListBox();
        private readonly StackPanel editor = new StackPanel();
        private bool refreshing;
        internal MacroDefinition Selected { get { return library.SelectedItem as MacroDefinition; } }
        internal string[] Actions { get { return Ui.English ? new[] { "Delay", "Keyboard", "Mouse", "Call macro", "Launch", "Run command", "Text", "Loop start", "Loop end" } : MacroLabels.Actions; } }
        internal string[] Presses { get { return Ui.English ? new[] { "Tap / click", "Down", "Up" } : MacroLabels.Presses; } }
        internal string[] Mice { get { return Ui.English ? new[] { "Left", "Right", "Middle", "Side X1", "Side X2", "Wheel up", "Wheel down" } : MacroLabels.Mice; } }
        internal string[] TriggerNames { get { return Ui.English ? new[] { "Keyboard / chord", "Mouse left", "Mouse right", "Mouse middle", "Side X1", "Side X2", "Wheel up", "Wheel down" } : MacroLabels.Triggers; } }
        internal string[] Modes { get { return Ui.English ? new[] { "Once", "Repeat while held", "Toggle start / stop" } : MacroLabels.Modes; } }
        internal MacroPage(ShellWindow shell) { this.shell = shell; BuildWorkspace(); }
        internal void ReloadLibrary(string select)
        {
            refreshing = true; library.ItemsSource = null; library.ItemsSource = shell.Draft.Macros;
            library.SelectedItem = shell.Draft.Find(select) ?? shell.Draft.Macros.FirstOrDefault(); refreshing = false; ShowSelected();
        }
        private void ShowSelected()
        {
            if (refreshing) return;
            CloseInlineEditor(); RefreshRows(); UpdateDirty();
        }
        internal void RefreshRows()
        {
            refreshing = true; int index = steps.SelectedIndex; steps.Items.Clear(); bindings.Items.Clear(); var macro = Selected;
            int depth = 0;
            if (macro != null) for (int i = 0; i < macro.Steps.Count; i++)
            {
                var step = macro.Steps[i]; if (step.Kind == ActionKind.LoopEnd) depth = Math.Max(0, depth - 1);
                string detail = Ui.English ? StepDetail(step) : MacroLabels.Detail(step, shell.Draft);
                var text = Ui.Text((i + 1).ToString("00") + "   " + Actions[(int)step.Kind] + "   ·   " + detail, 13);
                text.TextWrapping = TextWrapping.NoWrap; text.TextTrimming = TextTrimming.CharacterEllipsis; text.Margin = new Thickness(depth * 16, 0, 0, 0);
                steps.Items.Add(new ListBoxItem { Content = text, Tag = step, ToolTip = detail, Padding = new Thickness(10, 12, 10, 12) });
                if (step.Kind == ActionKind.LoopStart) depth++;
            }
            if (index >= 0 && index < steps.Items.Count) steps.SelectedIndex = index;
            var active = shell.ActiveMacros;
            foreach (var binding in active.Bindings)
            {
                string trigger = (binding.Modifiers == KeyModifiers.None ? "" : binding.Modifiers + " + ") + (binding.Trigger == TriggerKind.Keyboard ? MacroLabels.KeyName(binding.KeyCode) : TriggerNames[(int)binding.Trigger]);
                var target = active.Find(binding.MacroId);
                bindings.Items.Add(new ListBoxItem { Content = BindingColumns(trigger, target == null ? Ui.T("缺失宏", "Missing macro") : target.Name, Modes[(int)binding.Mode], binding.SuppressOriginal ? Ui.T("是", "Yes") : Ui.T("否", "No"), binding.Enabled ? Ui.T("启用", "On") : Ui.T("停用", "Off")), Tag = binding });
            }
            refreshing = false; UpdateDirty(); RefreshBindingMap();
        }
        private string StepDetail(MacroStep s)
        {
            switch (s.Kind) {
                case ActionKind.Delay: return s.Number + " ms";
                case ActionKind.Keyboard: return MacroLabels.KeyName(s.KeyCode) + " · " + Presses[(int)s.Press];
                case ActionKind.Mouse: return Mice[(int)s.Mouse] + " · " + (s.Mouse >= MouseAction.WheelUp ? s.Number + " notches" : Presses[(int)s.Press]);
                case ActionKind.CallMacro: var target = shell.Draft.Find(s.Value); return target == null ? "Missing macro" : target.Name;
                case ActionKind.LoopStart: return s.Number + " iterations";
                case ActionKind.LoopEnd: return "End loop";
                default: return (s.Value + " " + s.Arguments).Replace("\n", " ↵ ");
            }
        }
        private void NewMacro() { if (!CommitPending()) return; var m = new MacroDefinition { Name = Ui.T("新宏 ", "New macro ") + (shell.Draft.Macros.Count + 1) }; shell.Draft.Macros.Add(m); shell.DraftDirty = true; ReloadLibrary(m.Id); }
        private void CopyMacro() { if (!CommitPending() || Selected == null) return; var m = shell.Draft.Clone().Find(Selected.Id); m.Id = Guid.NewGuid().ToString("N"); m.Name += Ui.T(" 副本", " copy"); shell.Draft.Macros.Add(m); shell.DraftDirty = true; ReloadLibrary(m.Id); }
        private void DeleteMacro()
        {
            if (!CommitPending()) return;
            var m = Selected; if (m == null) return;
            if (shell.Draft.Macros.Any(x => x.Id != m.Id && x.Steps.Any(s => s.Kind == ActionKind.CallMacro && s.Value == m.Id))) { shell.Notice(Ui.T("此宏被其他宏引用，请先移除调用动作。", "Other macros call this one. Remove those calls first.")); return; }
            shell.OpenDrawer(Ui.T("删除宏？", "Delete macro?"), Ui.Stack(Ui.Text(m.Name, 21), Ui.Text(Ui.T("立即删除此宏及其绑定，并停止正在执行的宏；其他未保存草稿不会被应用。", "Deletes this macro and its bindings immediately, and stops playback. Other drafts are not applied."), 13, Ui.Muted), Ui.Button(Ui.T("确认删除", "Delete"), () => {
                shell.BindingChange(c => c.RemoveMacro(m.Id), l => { l.Bindings.RemoveAll(b => b.MacroId == m.Id); l.Macros.RemoveAll(x => x.Id == m.Id); });
                if (shell.ActiveMacros.Find(m.Id) != null) return;
                shell.Draft.Macros.Remove(m); shell.DraftDirty = true; shell.CloseDrawer(); ReloadLibrary(null);
            }), Ui.Button(Ui.T("取消", "Cancel"), shell.CloseDrawer)));
        }
        private void MoveStep(int direction) { MoveBlock(steps.SelectedIndex, direction); }
        private void DeleteStep() { DeleteBlock(); }
        private void DeleteBinding() { var item = bindings.SelectedItem as ListBoxItem; if (item == null) return; shell.RemoveSavedBinding(((MacroBinding)item.Tag).Id); }
        private void Preview()
        {
            if (!CommitPending()) return;
            var m = Selected; if (m == null) return;
            shell.OpenDrawer(Ui.T("试运行宏", "Test macro"), Ui.Stack(Ui.Text(m.Name, 21), Ui.Text(Ui.T("点击开始后有 3 秒时间切换到目标窗口。宏可能输入文字、点击鼠标或启动你定义的程序。Ctrl+Shift+F12 紧急停止。", "After starting, you have 3 seconds to focus the target window. The macro may type, click or launch your configured programs. Ctrl+Shift+F12 stops it."), 14, Ui.Muted),
                Ui.Button(Ui.T("开始试运行", "Start test run"), () => { try { if (shell.Demo) { shell.Notice(Ui.T("安全预览不发送输入、不运行命令。", "Safe preview does not send input or launch commands.")); return; } MacroValidation.Validate(shell.Draft); shell.CloseDrawer(); if (!shell.Macros.Preview(shell.Draft, m.Id)) shell.Notice(Ui.T("另一个宏仍在运行，请先停止。", "Another macro is running; stop it first.")); } catch (Exception ex) { shell.Notice(ex.Message); } }, true), Ui.Button(Ui.T("取消", "Cancel"), shell.CloseDrawer)));
        }
        private void EditStep(bool existing)
        {
            if (!CommitPending()) return;
            var macro = Selected; int index = steps.SelectedIndex;
            if (macro == null || (existing && index < 0)) return;
            var source = existing ? macro.Steps[index] : new MacroStep { Kind = addingKind, Number = addingKind == ActionKind.LoopStart ? 2 : addingKind == ActionKind.Mouse ? 1 : 100 };
            var step = new MacroStep { Kind = source.Kind, Press = source.Press, KeyCode = source.KeyCode, Mouse = source.Mouse, Number = source.Number, Value = source.Value, Arguments = source.Arguments };
            var kind = Ui.Combo(Actions, (int)step.Kind); var fields = new StackPanel();
            var number = Ui.Input(step.Number.ToString()); var value = Ui.Input(step.Value); var arguments = Ui.Input(step.Arguments);
            var press = Ui.Combo(Presses, (int)step.Press); var mouse = Ui.Combo(Mice, (int)step.Mouse); var key = Keys(step.KeyCode);
            var targets = new ElasticComboBox { ItemsSource = shell.Draft.Macros.Where(m => m.Id != macro.Id).ToList(), SelectedValuePath = "Id", SelectedValue = step.Value, Margin = new Thickness(0, 0, 0, 12) };
            Action rebuild = () => {
                fields.Children.Clear(); var k = (ActionKind)kind.SelectedIndex;
                if (k == ActionKind.Delay || k == ActionKind.LoopStart || k == ActionKind.Mouse) { fields.Children.Add(Ui.Text(k == ActionKind.Delay ? Ui.T("延迟（毫秒）", "Delay (ms)") : k == ActionKind.LoopStart ? Ui.T("循环次数", "Iterations") : Ui.T("滚轮格数（点击动作忽略此值）", "Wheel notches (ignored for clicks)"), 13)); fields.Children.Add(number); }
                if (k == ActionKind.Keyboard) { fields.Children.Add(Ui.Text(Ui.T("按键", "Key"))); fields.Children.Add(key); fields.Children.Add(press); }
                if (k == ActionKind.Mouse) { fields.Children.Add(mouse); fields.Children.Add(press); }
                if (k == ActionKind.CallMacro) { fields.Children.Add(Ui.Text(Ui.T("选择要调用的宏", "Select macro to call"))); fields.Children.Add(targets); }
                if (k == ActionKind.Text || k == ActionKind.Launch || k == ActionKind.Command)
                {
                    fields.Children.Add(Ui.Text(k == ActionKind.Text ? Ui.T("文本内容", "Text") : Ui.T("程序路径 / 启动目标", "Program path / launch target")));
                    value.AcceptsReturn = k == ActionKind.Text; value.MinHeight = k == ActionKind.Text ? 120 : 38; value.TextWrapping = TextWrapping.Wrap; fields.Children.Add(value);
                    if (k != ActionKind.Text) { fields.Children.Add(Ui.Text(Ui.T("参数", "Arguments"))); fields.Children.Add(arguments); fields.Children.Add(Ui.Text(Ui.T("只运行你信任的程序。命令项填写可执行文件，不会自动套用命令行解释器。", "Only run programs you trust. Enter an executable; commands are not automatically passed through a shell."), 12, Ui.Muted)); }
                }
                if (k == ActionKind.LoopEnd) fields.Children.Add(Ui.Text(Ui.T("必须与前面的循环开始配对。", "Must match an earlier loop start.")));
            };
            kind.SelectionChanged += (s, e) => rebuild(); rebuild();
            var error = Ui.Text("", 12, Ui.Foreground);
            PresentEditor(existing ? Ui.T("动作属性", "Action properties") : Ui.T("添加动作", "Add action"), Ui.Stack(kind, fields, error,
                Ui.Button(Ui.T("完成", "Done"), () => {
                    step.Kind = (ActionKind)kind.SelectedIndex; int n;
                    if (step.Kind == ActionKind.Delay || step.Kind == ActionKind.LoopStart || step.Kind == ActionKind.Mouse) {
                        int min = step.Kind == ActionKind.Delay ? 0 : 1, max = step.Kind == ActionKind.Delay ? 600000 : 1000;
                        if (!int.TryParse(number.Text, out n) || n < min || n > max) { error.Text = Ui.T("数值范围：", "Range: ") + min + "–" + max; return; } step.Number = n;
                    }
                    step.Press = (PressMode)press.SelectedIndex; step.Mouse = (MouseAction)mouse.SelectedIndex; step.KeyCode = ((MacroLabels.KeyChoice)key.SelectedItem).Code;
                    step.Value = step.Kind == ActionKind.CallMacro ? targets.SelectedValue as string : value.Text; step.Arguments = arguments.Text;
                    if ((step.Kind == ActionKind.CallMacro || step.Kind == ActionKind.Launch || step.Kind == ActionKind.Command) && string.IsNullOrWhiteSpace(step.Value)) { error.Text = Ui.T("请填写或选择目标。", "Enter or select a target."); return; }
                    if (existing && (source.Kind == ActionKind.LoopStart || source.Kind == ActionKind.LoopEnd || step.Kind == ActionKind.LoopStart || step.Kind == ActionKind.LoopEnd) && source.Kind != step.Kind) { error.Text = Ui.T("循环标记请成组添加或删除，不能转换为其他动作。", "Add or delete loops as pairs; do not convert loop markers."); return; }
                    if (!existing && step.Kind == ActionKind.LoopEnd) { error.Text = Ui.T("请选择循环开始；结束标记会自动配对添加。", "Choose Loop start; an end marker is inserted automatically."); return; }
                    int inserted = existing ? index : index < 0 ? macro.Steps.Count : index + 1;
                    if (existing) macro.Steps[index] = step;
                    else { macro.Steps.Insert(inserted, step); if (step.Kind == ActionKind.LoopStart) macro.Steps.Insert(inserted + 1, new MacroStep { Kind = ActionKind.LoopEnd }); }
                    shell.DraftDirty = true; CloseStepEditor(); RefreshRows(); refreshing = true; steps.SelectedIndex = inserted; refreshing = false;
                }, true), Ui.Button(Ui.T("取消", "Cancel"), CloseStepEditor)));
        }
        private static ComboBox Keys(int selected)
        {
            var keys = new ElasticComboBox { Margin = new Thickness(0, 0, 0, 12) };
            for (int i = 8; i <= 254; i++) if (Enum.IsDefined(typeof(System.Windows.Forms.Keys), i)) { var item = new MacroLabels.KeyChoice(i); keys.Items.Add(item); if (i == selected) keys.SelectedItem = item; }
            if (keys.SelectedItem == null) { var item = new MacroLabels.KeyChoice(selected); keys.Items.Add(item); keys.SelectedItem = item; } return keys;
        }
        private void EditBinding(bool existing)
        {
            var row = bindings.SelectedItem as ListBoxItem;
            var active = shell.ActiveMacros;
            var macro = existing && row != null ? active.Find(((MacroBinding)row.Tag).MacroId) : (Selected == null ? null : active.Find(Selected.Id));
            macro = macro ?? active.Macros.FirstOrDefault();
            if (macro == null || (existing && row == null)) { shell.Notice(Ui.T("请先保存宏，再添加绑定。", "Save a macro before assigning it.")); return; }
            var original = existing ? (MacroBinding)row.Tag : new MacroBinding { MacroId = macro.Id };
            var available = active.Macros.Where(m => shell.IsMacroSaved(m.Id)).ToArray();
            var targetMacro = new ElasticComboBox { ItemsSource = available, SelectedItem = available.FirstOrDefault(m => m.Id == macro.Id) ?? available.FirstOrDefault(), Margin = new Thickness(0, 0, 0, 12) };
            var trigger = Ui.Combo(TriggerNames, (int)original.Trigger); var key = Keys(original.KeyCode); var mode = Ui.Combo(Modes, (int)original.Mode);
            var modifiers = new[] { new ElasticCheckBox { Content = "Ctrl", IsChecked = (original.Modifiers & KeyModifiers.Control) != 0 }, new ElasticCheckBox { Content = "Shift", IsChecked = (original.Modifiers & KeyModifiers.Shift) != 0 }, new ElasticCheckBox { Content = "Alt", IsChecked = (original.Modifiers & KeyModifiers.Alt) != 0 }, new ElasticCheckBox { Content = "Win", IsChecked = (original.Modifiers & KeyModifiers.Windows) != 0 } };
            var suppress = new ElasticCheckBox { Content = Ui.T("阻止按键 / 滚轮原始动作", "Suppress the original input"), IsChecked = original.SuppressOriginal };
            var enabled = new ElasticCheckBox { Content = existing ? Ui.T("启用此绑定（关闭立即停用）", "Enable binding (uncheck disables immediately)") : Ui.T("启用此绑定", "Enable this binding"), IsChecked = original.Enabled };
            var acknowledge = new ElasticCheckBox { Content = Ui.T("我确认允许替代鼠标左键的原始点击。", "I confirm replacing original left clicks.") };
            var error = Ui.Text("", 12, Ui.Foreground);
            Action update = () => { key.IsEnabled = trigger.SelectedIndex == 0; if (trigger.SelectedIndex >= 6 && mode.SelectedIndex == 1) mode.SelectedIndex = 0; };
            trigger.SelectionChanged += (s, e) => update(); update();
            if (existing) enabled.Unchecked += (s, e) => shell.SetSavedBinding(original.Id, false);
            shell.OpenDrawer(existing ? Ui.T("编辑绑定", "Edit binding") : Ui.T("新增绑定", "New binding"), Ui.Stack(Ui.Text(Ui.T("选择宏", "Macro"), 14), targetMacro, trigger, key, Ui.Text(Ui.T("同时按住的修饰键", "Required modifiers")), Ui.Stack(modifiers), Ui.Text(Ui.T("触发方式", "Run mode")), mode, enabled, suppress, acknowledge,
                Ui.Text(Ui.T("注意：阻止左键原始动作会影响普通点击；滚轮不支持“按住重复”。紧急停止键始终保留。", "Suppressing left click affects normal clicks. Wheel triggers cannot repeat while held. The emergency stop chord is always reserved."), 12, Ui.Muted), error,
                Ui.Button(Ui.T("确认绑定", "Confirm binding"), () => {
                    var selected = targetMacro.SelectedItem as MacroDefinition;
                    if (selected == null || !shell.IsMacroSaved(selected.Id)) { error.Text = Ui.T("请先保存宏的修改。", "Save macro changes first."); return; }
                    if (trigger.SelectedIndex == (int)TriggerKind.Left && suppress.IsChecked == true && acknowledge.IsChecked != true) { error.Text = Ui.T("请先确认替代左键的风险。", "Acknowledge the left-click risk first."); return; }
                    var b = new MacroBinding { Id = original.Id, MacroId = ((MacroDefinition)targetMacro.SelectedItem).Id, Trigger = (TriggerKind)trigger.SelectedIndex, KeyCode = ((MacroLabels.KeyChoice)key.SelectedItem).Code, Mode = (RunMode)mode.SelectedIndex, Enabled = enabled.IsChecked == true, SuppressOriginal = suppress.IsChecked == true };
                    for (int i = 0; i < 4; i++) if (modifiers[i].IsChecked == true) b.Modifiers |= (KeyModifiers)(1 << i);
                    var candidate = shell.ActiveMacros; candidate.Bindings.RemoveAll(x => x.Id == b.Id); candidate.Bindings.Add(b);
                    try { MacroValidation.Validate(candidate); } catch (Exception ex) { error.Text = ex.Message; return; }
                    if (shell.SaveBinding(b, false)) { shell.CloseDrawer(); RefreshRows(); } else error.Text = Ui.T("保存失败，新绑定未应用。", "Saving failed; new binding not applied.");
                }, true), Ui.Button(Ui.T("取消", "Cancel"), shell.CloseDrawer)));
        }
    }
}
