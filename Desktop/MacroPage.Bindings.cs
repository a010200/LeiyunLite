using System;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using RazerBatteryTray.Macros;

namespace RazerBatteryTray.Desktop
{
    internal sealed partial class MacroPage
    {
        private MouseBindingMap mouseMap;
        private readonly ListBox mappingLibrary = new ListBox();
        private TextBox mappingSearch;
        private TextBlock bindingStatus, mouseModel;
        private CheckBox bindingSwitch;
        private Grid mappingLayout;
        private Border macroShelf;
        private bool refreshingBindings;
        private FrameworkElement BuildBindingWorkspace()
        {
            var root = new DockPanel();
            bindingSwitch = Ui.Toggle(Ui.T("启用宏绑定", "Enable macro bindings"), shell.ActiveMacros.BindingsEnabled, enabled => {
                if (refreshingBindings) return;
                shell.BindingChange(c => c.SetBindingsEnabled(enabled), l => l.BindingsEnabled = enabled);
            });
            bindingStatus = Ui.Text("", 12, Ui.Muted);
            var retry = Ui.Button(Ui.T("重试保存停用状态", "Retry safety save"), () => shell.BindingChange(c => c.RetrySafetySave(), l => { }));
            retry.Name = "RetryBindingSave";
            var advanced = Ui.Button(Ui.T("键盘与高级绑定", "Keyboard / advanced"), OpenAdvancedBindings);
            var top = Ui.Stack(Ui.Row(bindingSwitch, advanced), bindingStatus, retry);
            DockPanel.SetDock(top, Dock.Top); root.Children.Add(top);
            mappingSearch = Ui.Input(""); mappingSearch.Height = 36; mappingSearch.MinHeight = 0;
            mappingSearch.ToolTip = Ui.T("搜索宏名称", "Search macros"); mappingSearch.TextChanged += (s, e) => RefreshBindingMap();
            var shelf = new DockPanel(); var shelfTop = Ui.Stack(Ui.Text(Ui.T("宏库", "Macro library"), 20), Ui.Text(Ui.T("拖到右侧按键以分配", "Drag onto a mouse button"), 12, Ui.Muted), mappingSearch,
                Ui.Button("+ " + Ui.T("新建宏", "New macro"), () => { SelectTab(false); NewMacro(); }));
            DockPanel.SetDock(shelfTop, Dock.Top); shelf.Children.Add(shelfTop); shelf.Children.Add(mappingLibrary);
            macroShelf = Ui.Card(shelf); macroShelf.Margin = new Thickness(0, 0, 16, 0); macroShelf.Padding = new Thickness(14);
            mouseMap = new MouseBindingMap(shell.IsMacroSaved); mouseMap.Assign += OpenMouseBinding;
            mouseModel = Ui.Text("", 16); mouseModel.Margin = new Thickness(0, 0, 0, 4);
            var modelPanel = new DockPanel(); var modelTitle = Ui.Stack(mouseModel, Ui.Text(Ui.T("通用键位示意 · 软件级绑定，非板载映射", "Generic layout · software bindings, not onboard"), 12, Ui.Muted));
            DockPanel.SetDock(modelTitle, Dock.Top); modelPanel.Children.Add(modelTitle);
            var foot = Ui.Text(Ui.T("点击键位也可分配。原始功能由系统/驱动决定；绑定可能影响其他鼠标的同类输入。", "Click a button to assign. Original actions depend on system/driver settings; bindings may affect other mice too."), 12, Ui.Muted);
            DockPanel.SetDock(foot, Dock.Bottom); modelPanel.Children.Add(foot); modelPanel.Children.Add(mouseMap);
            var modelCard = Ui.Card(modelPanel); modelCard.Padding = new Thickness(16); modelCard.Margin = new Thickness(0);
            mappingLayout = new Grid(); mappingLayout.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(225) }); mappingLayout.ColumnDefinitions.Add(new ColumnDefinition());
            mappingLayout.RowDefinitions.Add(new RowDefinition()); mappingLayout.RowDefinitions.Add(new RowDefinition { Height = new GridLength(0) });
            mappingLayout.Children.Add(macroShelf); Grid.SetColumn(modelCard, 1); mappingLayout.Children.Add(modelCard);
            root.Children.Add(mappingLayout);
            SizeChanged += (s, e) => {
                bool compact = ActualWidth < 800;
                mappingLayout.ColumnDefinitions[0].Width = compact ? new GridLength(1, GridUnitType.Star) : new GridLength(225);
                mappingLayout.ColumnDefinitions[1].Width = compact ? new GridLength(0) : new GridLength(1, GridUnitType.Star);
                mappingLayout.Height = compact ? 820 : Math.Max(480, ActualHeight - 220);
                mappingLayout.RowDefinitions[0].Height = compact ? new GridLength(280) : new GridLength(1, GridUnitType.Star);
                mappingLayout.RowDefinitions[1].Height = compact ? new GridLength(1, GridUnitType.Star) : new GridLength(0);
                Grid.SetColumn(modelCard, compact ? 0 : 1); Grid.SetRow(modelCard, compact ? 1 : 0);
                macroShelf.Margin = compact ? new Thickness(0, 0, 0, 12) : new Thickness(0, 0, 16, 0);
            };
            Point start = new Point(); string dragId = null;
            mappingLibrary.PreviewMouseLeftButtonDown += (s, e) => { start = e.GetPosition(mappingLibrary); var row = ItemsControl.ContainerFromElement(mappingLibrary, e.OriginalSource as DependencyObject) as ListBoxItem; dragId = row == null ? null : row.Tag as string; };
            mappingLibrary.PreviewMouseMove += (s, e) => {
                if (e.LeftButton != MouseButtonState.Pressed || dragId == null) return; var p = e.GetPosition(mappingLibrary);
                if (Math.Abs(p.X - start.X) < SystemParameters.MinimumHorizontalDragDistance && Math.Abs(p.Y - start.Y) < SystemParameters.MinimumVerticalDragDistance) return;
                string id = dragId; dragId = null;
                if (!shell.IsMacroSaved(id)) { shell.Notice(Ui.T("请先保存宏的修改，再分配按键。", "Save the macro before assigning it.")); return; }
                DragDrop.DoDragDrop(mappingLibrary, new DataObject(MouseBindingMap.MacroFormat, id), DragDropEffects.Link);
            };
            mappingLibrary.MouseDoubleClick += (s, e) => { var row = mappingLibrary.SelectedItem as ListBoxItem; if (row != null) { ReloadLibrary((string)row.Tag); SelectTab(false); } };
            return Ui.Scroll(root);
        }
        private void RefreshBindingMap()
        {
            if (mouseMap == null) return;
            var active = shell.ActiveMacros; refreshingBindings = true;
            bindingSwitch.IsChecked = active.BindingsEnabled; refreshingBindings = false;
            bindingStatus.Text = shell.SafetySavePending ? Ui.T("停用已生效，但保存失败。重启可能恢复旧配置，请重试保存。", "Disabled now, but saving failed. Old settings may return on restart. Retry saving.") : Ui.T("此页显示已生效配置；确认绑定即保存，解除绑定立即生效。", "This page shows active settings. Confirm to save; unbinding takes effect immediately.");
            ((FrameworkElement)((Panel)bindingStatus.Parent).Children[2]).Visibility = shell.SafetySavePending ? Visibility.Visible : Visibility.Collapsed;
            mouseModel.Text = string.IsNullOrWhiteSpace(shell.Reading.DeviceName) ? Ui.T("鼠标按键分配", "Mouse assignments") : shell.Reading.DeviceName;
            mouseMap.Refresh(active);
            string selected = mappingLibrary.SelectedItem == null ? null : (string)((ListBoxItem)mappingLibrary.SelectedItem).Tag;
            mappingLibrary.Items.Clear();
            foreach (var macro in shell.Draft.Macros.Where(m => m.Name.IndexOf(mappingSearch.Text, StringComparison.CurrentCultureIgnoreCase) >= 0)) {
                bool saved = shell.IsMacroSaved(macro.Id); var links = active.Bindings.Where(b => b.MacroId == macro.Id).ToArray();
                var text = Ui.Text(macro.Name, 14); text.TextTrimming = TextTrimming.CharacterEllipsis; text.TextWrapping = TextWrapping.NoWrap;
                var hint = Ui.Text(!saved ? Ui.T("未保存 / 无动作", "Unsaved / empty") : links.Length == 0 ? Ui.T("未分配", "Unassigned") : links.Length == 1 ? TriggerNames[(int)links[0].Trigger] : links.Length + Ui.T(" 个绑定", " bindings"), 11, Ui.Muted);
                var row = new ListBoxItem { Tag = macro.Id, Content = Ui.Stack(text, hint), Padding = new Thickness(9, 10, 9, 8), ToolTip = macro.Name + "\n" + hint.Text };
                mappingLibrary.Items.Add(row); if (macro.Id == selected) mappingLibrary.SelectedItem = row;
            }
            if (mappingLibrary.Items.Count == 0) mappingLibrary.Items.Add(new ListBoxItem { IsEnabled = false, Content = Ui.Text(Ui.T("暂无匹配的宏", "No matching macros"), 13, Ui.Muted) });
        }
        private void OpenAdvancedBindings()
        {
            var old = bindings.Parent as Panel; if (old != null) old.Children.Remove(bindings);
            bindings.MinHeight = 150; bindings.MaxHeight = 360;
            shell.OpenDrawer(Ui.T("键盘与高级绑定", "Keyboard / advanced bindings"), Ui.Stack(
                Ui.Text(Ui.T("显示所有已生效绑定，含组合键。完成即保存；删除立即生效。", "All active bindings, including chords. Confirm saves; removal is immediate."), 13, Ui.Muted),
                Ui.Row(Ui.Button("+ " + Ui.T("新增", "Add"), () => EditBinding(false)), Ui.Button(Ui.T("编辑", "Edit"), () => EditBinding(true)), Ui.Button(Ui.T("解除绑定", "Unbind"), DeleteBinding)), bindings,
                Ui.Button(Ui.T("关闭", "Close"), shell.CloseDrawer)));
        }
        private void OpenMouseBinding(TriggerKind trigger, string macroId)
        {
            var active = shell.ActiveMacros;
            var current = active.Bindings.Find(b => b.Trigger == trigger && b.Modifiers == KeyModifiers.None);
            var available = active.Macros.Where(m => shell.IsMacroSaved(m.Id)).ToArray();
            var choice = new ElasticComboBox { ItemsSource = available, Margin = new Thickness(0, 0, 0, 12) };
            choice.SelectedItem = available.FirstOrDefault(m => m.Id == (macroId ?? (current == null ? null : current.MacroId))) ?? available.FirstOrDefault();
            var run = Ui.Combo(MacroValidation.IsWheel(trigger) ? new[] { Modes[0], Modes[2] } : Modes, current == null ? 0 : MacroValidation.IsWheel(trigger) ? (current.Mode == RunMode.Toggle ? 1 : 0) : (int)current.Mode);
            var behavior = Ui.Combo(new[] { Ui.T("替代原功能", "Replace original input"), Ui.T("保留原功能并执行宏", "Keep original input + macro") }, current == null ? (trigger == TriggerKind.Left ? 1 : 0) : current.SuppressOriginal ? 0 : 1);
            var acknowledge = new ElasticCheckBox { Content = Ui.T("我了解替代左键会影响普通点击；Ctrl+Shift+F12 可停止宏。", "I understand replacing left click affects normal input; Ctrl+Shift+F12 stops macros.") };
            Action updateRisk = () => acknowledge.Visibility = trigger == TriggerKind.Left && behavior.SelectedIndex == 0 ? Visibility.Visible : Visibility.Collapsed;
            behavior.SelectionChanged += (s, e) => updateRisk(); updateRisk();
            var error = Ui.Text("", 12, Ui.Muted);
            var content = Ui.Stack(Ui.Text(MouseBindingMap.DefaultName(trigger), 22), Ui.Text(current == null ? Ui.T("目前使用原始功能", "Currently using original input") : Ui.T("当前分配：", "Assigned: ") + active.Find(current.MacroId).Name, 13, Ui.Muted),
                Ui.Text(Ui.T("选择已保存的宏", "Saved macro")), choice, Ui.Text(Ui.T("执行方式", "Run mode")), run, behavior, acknowledge,
                Ui.Text(Ui.T("确认后仅保存本次绑定。已有分配将被替换，其他宏草稿不受影响。", "Confirmation saves only this assignment and replaces any existing assignment. Other drafts are unchanged."), 12, Ui.Muted), error);
            content.Children.Add(Ui.Button(current != null ? Ui.T("确认替换绑定", "Confirm assignment") : Ui.T("确认绑定", "Confirm binding"), () => {
                var macro = choice.SelectedItem as MacroDefinition;
                if (macro == null || !shell.IsMacroSaved(macro.Id)) { error.Text = Ui.T("请先在“我的宏”保存一个有效宏。", "Save a valid macro in My macros first."); return; }
                if (acknowledge.Visibility == Visibility.Visible && acknowledge.IsChecked != true) { error.Text = Ui.T("请先确认左键替代风险。", "Acknowledge the left-click risk first."); return; }
                var binding = new MacroBinding { Id = current == null ? Guid.NewGuid().ToString("N") : current.Id, MacroId = macro.Id, Trigger = trigger, Mode = MacroValidation.IsWheel(trigger) ? (run.SelectedIndex == 1 ? RunMode.Toggle : RunMode.Once) : (RunMode)run.SelectedIndex, SuppressOriginal = behavior.SelectedIndex == 0 };
                if (shell.SaveBinding(binding, true)) shell.CloseDrawer(); else error.Text = Ui.T("保存失败，未应用新绑定。请检查提示后重试。", "Saving failed; assignment not applied. Check the message and retry.");
            }, true));
            if (current != null) {
                content.Children.Add(Ui.Button(current.Enabled ? Ui.T("立即停用", "Disable now") : Ui.T("重新启用", "Enable"), () => { if (shell.SetSavedBinding(current.Id, !current.Enabled)) shell.CloseDrawer(); else error.Text = Ui.T("操作未完整保存，请查看停用状态提示。", "Operation not fully saved; check safety status."); }));
                content.Children.Add(Ui.Button(Ui.T("解除绑定 · 恢复原始输入", "Unbind · restore original input"), () => { shell.RemoveSavedBinding(current.Id); shell.CloseDrawer(); }));
            }
            content.Children.Add(Ui.Button(Ui.T("取消", "Cancel"), shell.CloseDrawer));
            shell.OpenDrawer(Ui.T("鼠标按键分配", "Mouse assignment"), content);
        }
    }
}
