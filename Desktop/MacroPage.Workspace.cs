using System;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using RazerBatteryTray.Macros;

namespace RazerBatteryTray.Desktop
{
    internal sealed partial class MacroPage
    {
        private readonly TextBlock dirty = Ui.Text("", 12, Ui.Muted);
        private Grid workspace;
        private FrameworkElement bindingWorkspace;
        private FrameworkElement recordingPalette, recordingTools;
        private Border properties;
        private Button myTab, bindingTab, saveButton, recordButton, testButton, stopButton, newButton;
        private Border myUnderline, bindingUnderline;
        private Grid libraryTools;
        private StackPanel runTools;
        private TextBlock emptyHint;
        private ActionKind addingKind;
        private Point dragStart;
        private int dragIndex = -1;
        private bool inlineOpen;
        private bool pendingEdit;
        private Button pendingDone;
        private MacroDefinition editingMacro;
        internal bool HasPendingEdit { get { return pendingEdit; } }
        internal void DrawerClosed() { if (!inlineOpen) { pendingEdit = false; pendingDone = null; UpdateDirty(); } }
        internal bool CommitPending()
        {
            if (!pendingEdit || pendingDone == null) return true;
            pendingDone.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            if (pendingEdit) { shell.Notice(Ui.T("请先修正动作属性中的错误，或取消编辑。", "Correct the action properties or cancel editing first.")); return false; }
            return true;
        }
        private void BuildWorkspace()
        {
            var root = new Grid(); root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto }); root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto }); root.RowDefinitions.Add(new RowDefinition());
            var more = Tool(Ui.T("更多 ···", "More ···"), () => shell.OpenDrawer(Ui.T("管理宏", "Manage macro"), Ui.Stack(
                Ui.Button("+ " + Ui.T("新建宏", "New macro"), () => { shell.CloseDrawer(); NewMacro(); }),
                Ui.Button(Ui.T("重命名", "Rename"), Rename),
                Ui.Button(Ui.T("复制宏", "Duplicate"), () => { shell.CloseDrawer(); CopyMacro(); }), Ui.Button(Ui.T("删除宏", "Delete"), DeleteMacro))));
            saveButton = Tool(Ui.T("保存并应用", "Save and apply"), () => { shell.SaveMacros(); UpdateDirty(); }, true);
            var titleRow = new DockPanel { Margin = new Thickness(0, 0, 0, 14) };
            var saving = Horizontal(dirty, saveButton); saving.VerticalAlignment = VerticalAlignment.Center;
            dirty.Margin = new Thickness(0, 0, 16, 0); dirty.VerticalAlignment = VerticalAlignment.Center;
            DockPanel.SetDock(saving, Dock.Right); titleRow.Children.Add(saving);
            var title = Ui.Text(Ui.T("宏与绑定", "Macros and bindings"), 30); title.Margin = new Thickness(0); titleRow.Children.Add(title);
            myTab = Tool(Ui.T("我的宏", "My macros"), () => SelectTab(false)); bindingTab = Tool(Ui.T("按键绑定", "Key bindings"), () => SelectTab(true));
            myUnderline = new Border { Height = 3, Background = Ui.Accent, CornerRadius = new CornerRadius(1.5), Margin = new Thickness(12, 0, 20, 0) };
            bindingUnderline = new Border { Height = 3, Background = Ui.Accent, CornerRadius = new CornerRadius(1.5), Margin = new Thickness(12, 0, 20, 0) };
            var tabs = Horizontal(Ui.Stack(myTab, myUnderline), Ui.Stack(bindingTab, bindingUnderline)); tabs.Margin = new Thickness(0, 0, 0, 18);
            root.Children.Add(Ui.Stack(titleRow, tabs));
            recordButton = Tool("● " + Ui.T("录制", "Record"), OpenRecording);
            testButton = Tool(Ui.T("试运行", "Test run"), Preview);
            stopButton = Tool(Ui.T("停止", "Stop"), () => { if (IsRecording) EndRecording(false); else if (shell.Macros != null) shell.Macros.Stop(); });
            stopButton.ToolTip = "Ctrl + Shift + F12";
            runTools = Horizontal(recordButton, testButton, stopButton); runTools.HorizontalAlignment = HorizontalAlignment.Right;
            libraryTools = new Grid { HorizontalAlignment = HorizontalAlignment.Left }; libraryTools.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto }); libraryTools.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto }); libraryTools.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            library.MinWidth = 130; library.Width = 250; library.Height = 36; library.Margin = new Thickness(0, 0, 8, 0); library.HorizontalAlignment = HorizontalAlignment.Left;
            newButton = Tool("+ " + Ui.T("新建宏", "New macro"), NewMacro); Grid.SetColumn(newButton, 1); Grid.SetColumn(more, 2);
            libraryTools.Children.Add(library); libraryTools.Children.Add(newButton); libraryTools.Children.Add(more);
            var toolbar = new Grid { Margin = new Thickness(0, 0, 0, 16) }; toolbar.ColumnDefinitions.Add(new ColumnDefinition()); toolbar.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            libraryTools.Margin = new Thickness(0, 0, 24, 0); toolbar.Children.Add(libraryTools); Grid.SetColumn(runTools, 1); toolbar.Children.Add(runTools);
            var toolsAndStatus = Ui.Stack(toolbar, BuildRecordingStatus()); Grid.SetRow(toolsAndStatus, 1); root.Children.Add(toolsAndStatus);
            workspace = new Grid(); workspace.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(138) }); workspace.ColumnDefinitions.Add(new ColumnDefinition()); workspace.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(250) }); Grid.SetRow(workspace, 2); root.Children.Add(workspace);
            var palette = Ui.Stack(Ui.Text(Ui.T("添加动作", "Add action"), 15));
            for (int i = 0; i < 8; i++) { var kind = (ActionKind)i; var b = Ui.Button("+  " + Actions[i], () => { if (Selected == null) { shell.Notice(Ui.T("请先新建一个宏。", "Create a macro first.")); return; } addingKind = kind; EditStep(false); }); b.Padding = new Thickness(10, 11, 8, 11); b.HorizontalContentAlignment = HorizontalAlignment.Left; b.Background = Brushes.Transparent; b.BorderThickness = new Thickness(0); palette.Children.Add(b); }
            var paletteCard = Ui.Card(palette); recordingPalette = paletteCard; paletteCard.Padding = new Thickness(10, 14, 4, 10); paletteCard.Margin = new Thickness(0, 0, 10, 0); workspace.Children.Add(paletteCard);
            var timeline = new DockPanel(); var timelineTools = Ui.Row(Ui.Button("↑", () => MoveStep(-1)), Ui.Button("↓", () => MoveStep(1)), Ui.Button(Ui.T("编辑", "Edit"), () => EditStep(true)), Ui.Button(Ui.T("删除", "Delete"), DeleteStep));
            DockPanel.SetDock(timelineTools, Dock.Bottom); timeline.Children.Add(timelineTools);
            recordingTools = timelineTools;
            var tip = Ui.Text(Ui.T("动作序列 · 拖动排序", "Sequence · drag to reorder"), 13, Ui.Muted); DockPanel.SetDock(tip, Dock.Top); timeline.Children.Add(tip);
            var sequence = new Grid(); sequence.Children.Add(steps);
            emptyHint = Ui.Text(Ui.T("从左侧添加动作，或开始录制", "Add an action on the left, or start recording"), 14, Ui.Muted);
            emptyHint.HorizontalAlignment = HorizontalAlignment.Center; emptyHint.VerticalAlignment = VerticalAlignment.Center; emptyHint.IsHitTestVisible = false;
            sequence.Children.Add(emptyHint); sequence.Children.Add(BuildRecordingPad()); timeline.Children.Add(sequence);
            var timelineCard = Ui.Card(timeline); timelineCard.Padding = new Thickness(12); timelineCard.Margin = new Thickness(0); Grid.SetColumn(timelineCard, 1); workspace.Children.Add(timelineCard);
            properties = Ui.Card(Ui.Scroll(editor)); properties.Padding = new Thickness(16); properties.Margin = new Thickness(10, 0, 0, 0); Grid.SetColumn(properties, 2); workspace.Children.Add(properties);
            bindingWorkspace = BuildBindingWorkspace(); Grid.SetRow(bindingWorkspace, 2); root.Children.Add(bindingWorkspace);
            library.SelectionChanged += (s, e) => { if (refreshing) return; if (!CommitPending()) { refreshing = true; library.SelectedItem = editingMacro; refreshing = false; return; } ShowSelected(); }; steps.MouseDoubleClick += (s, e) => EditStep(true); bindings.MouseDoubleClick += (s, e) => EditBinding(true);
            steps.SelectionChanged += (s, e) => { if (!refreshing && steps.SelectedIndex >= 0 && ActualWidth >= 850) EditStep(true); };
            steps.AllowDrop = true;
            steps.PreviewMouseLeftButtonDown += (s, e) => { dragStart = e.GetPosition(steps); var row = ItemsControl.ContainerFromElement(steps, e.OriginalSource as DependencyObject) as ListBoxItem; dragIndex = row == null ? -1 : steps.Items.IndexOf(row); };
            steps.PreviewMouseMove += (s, e) => { if (dragIndex < 0 || e.LeftButton != MouseButtonState.Pressed) return; var point = e.GetPosition(steps); if (Math.Abs(point.X - dragStart.X) < SystemParameters.MinimumHorizontalDragDistance && Math.Abs(point.Y - dragStart.Y) < SystemParameters.MinimumVerticalDragDistance) return; int source = dragIndex; dragIndex = -1; DragDrop.DoDragDrop(steps, new DataObject("LeiyunStep", source), DragDropEffects.Move); };
            steps.Drop += (s, e) => { if (!e.Data.GetDataPresent("LeiyunStep")) return; var row = ItemsControl.ContainerFromElement(steps, e.OriginalSource as DependencyObject) as ListBoxItem; int to = row == null ? steps.Items.Count : steps.Items.IndexOf(row); Reorder((int)e.Data.GetData("LeiyunStep"), to); e.Handled = true; };
            SizeChanged += (s, e) => UpdatePropertyLayout();
            Content = root; ReloadLibrary(null); SelectTab(false); InitializeRecording();
        }
        private void SelectTab(bool binding)
        {
            if (!CommitPending()) return;
            saveButton.Visibility = dirty.Visibility = binding ? Visibility.Collapsed : Visibility.Visible;
            RefreshBindingMap();
            libraryTools.Visibility = runTools.Visibility = binding ? Visibility.Collapsed : Visibility.Visible;
            workspace.Visibility = binding ? Visibility.Collapsed : Visibility.Visible; bindingWorkspace.Visibility = binding ? Visibility.Visible : Visibility.Collapsed;
            myTab.Background = bindingTab.Background = Brushes.Transparent; myTab.BorderThickness = bindingTab.BorderThickness = new Thickness(0);
            myTab.Foreground = binding ? Ui.Muted : Ui.Foreground; bindingTab.Foreground = binding ? Ui.Foreground : Ui.Muted;
            myUnderline.Opacity = binding ? 0 : 1; bindingUnderline.Opacity = binding ? 1 : 0;
            Ui.Enter(binding ? bindingWorkspace : workspace);
        }
        private static StackPanel Horizontal(params UIElement[] items) { var panel = new StackPanel { Orientation = Orientation.Horizontal }; foreach (var item in items) panel.Children.Add(item); return panel; }
        private static Button Tool(string label, Action action, bool primary = false) { var b = Ui.Button(label, action, primary); b.Height = 36; b.Padding = new Thickness(12, 0, 12, 0); b.Margin = new Thickness(0, 0, 8, 0); return b; }
        private static UIElement BindingColumns(params string[] values)
        {
            var row = new Grid(); double[] widths = { 1.4, 1.2, 1.4, .55, .55 };
            for (int i = 0; i < values.Length; i++) { row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(widths[i], GridUnitType.Star) }); var text = Ui.Text(values[i], 13); text.Margin = new Thickness(4, 7, 4, 7); text.TextWrapping = TextWrapping.NoWrap; text.TextTrimming = TextTrimming.CharacterEllipsis; text.ToolTip = values[i]; Grid.SetColumn(text, i); row.Children.Add(text); } return row;
        }
        private void UpdateDirty() { dirty.Text = shell.DraftDirty || pendingEdit ? Ui.T("● 未保存", "● Unsaved") : Ui.T("已保存", "Saved"); UpdateRecordingControls(); }
        private void Rename()
        {
            if (!CommitPending()) return;
            var macro = Selected; if (macro == null) return; var input = Ui.Input(macro.Name);
            shell.OpenDrawer(Ui.T("重命名宏", "Rename macro"), Ui.Stack(input, Ui.Button(Ui.T("完成", "Done"), () => { if (string.IsNullOrWhiteSpace(input.Text)) return; macro.Name = input.Text.Trim(); shell.DraftDirty = true; shell.CloseDrawer(); ReloadLibrary(macro.Id); }, true)));
        }
        private void PresentEditor(string title, UIElement content)
        {
            pendingEdit = false; editingMacro = Selected; pendingDone = ((Panel)content).Children.OfType<Button>().First();
            content.AddHandler(TextBox.TextChangedEvent, new TextChangedEventHandler((s, e) => { pendingEdit = true; UpdateDirty(); }));
            content.AddHandler(System.Windows.Controls.Primitives.Selector.SelectionChangedEvent, new SelectionChangedEventHandler((s, e) => { pendingEdit = true; UpdateDirty(); }));
            if (ActualWidth < 850) { shell.OpenDrawer(title, content); return; }
            editor.Children.Clear(); editor.Children.Add(Ui.Text(title, 18)); editor.Children.Add(content); inlineOpen = true; UpdatePropertyLayout();
        }
        private void UpdatePropertyLayout() { if (newButton != null) { newButton.Visibility = ActualWidth >= 820 ? Visibility.Visible : Visibility.Collapsed; library.Width = ActualWidth >= 820 ? 250 : 160; } bool wide = ActualWidth >= 850 || inlineOpen; workspace.ColumnDefinitions[2].Width = new GridLength(wide ? 250 : 0); properties.Visibility = wide ? Visibility.Visible : Visibility.Collapsed; }
        private void CloseInlineEditor() { editor.Children.Clear(); editor.Children.Add(Ui.Text(Ui.T("动作属性", "Action properties"), 18)); editor.Children.Add(Ui.Text(Ui.T("选择动作后编辑参数，点击完成写入草稿。", "Select an action, edit its parameters, then click Done to update the draft."), 13, Ui.Muted)); inlineOpen = false; UpdatePropertyLayout(); }
        private void CloseStepEditor() { pendingEdit = false; pendingDone = null; if (shell.DrawerOpen) shell.CloseDrawer(); CloseInlineEditor(); UpdateDirty(); }
        private int BlockEnd(int start)
        {
            if (Selected == null || start < 0 || start >= Selected.Steps.Count) return start;
            if (Selected.Steps[start].Kind != ActionKind.LoopStart) return start;
            int depth = 0; for (int i = start; i < Selected.Steps.Count; i++) { if (Selected.Steps[i].Kind == ActionKind.LoopStart) depth++; if (Selected.Steps[i].Kind == ActionKind.LoopEnd && --depth == 0) return i; } return start;
        }
        private int BlockStart(int end)
        {
            if (end < 0 || Selected.Steps[end].Kind != ActionKind.LoopEnd) return end;
            int depth = 0; for (int i = end; i >= 0; i--) { if (Selected.Steps[i].Kind == ActionKind.LoopEnd) depth++; if (Selected.Steps[i].Kind == ActionKind.LoopStart && --depth == 0) return i; } return end;
        }
        private void MoveBlock(int index, int direction)
        {
            if (Selected == null || index < 0) return; int from = BlockStart(index), end = BlockEnd(from);
            int to = direction < 0 ? BlockStart(from - 1) : end + 1 >= Selected.Steps.Count ? Selected.Steps.Count : BlockEnd(end + 1) + 1; Reorder(from, to);
        }
        private void Reorder(int from, int to)
        {
            if (!CommitPending()) return;
            if (Selected == null || from < 0 || from >= Selected.Steps.Count || to < 0 || to > Selected.Steps.Count) return;
            from = BlockStart(from); int end = BlockEnd(from); if (to >= from && to <= end + 1) return;
            var block = Selected.Steps.GetRange(from, end - from + 1); Selected.Steps.RemoveRange(from, block.Count); if (to > end) to -= block.Count; Selected.Steps.InsertRange(to, block);
            shell.DraftDirty = true; CloseInlineEditor(); RefreshRows(); refreshing = true; steps.SelectedIndex = to; refreshing = false;
        }
        private void DeleteBlock()
        {
            if (!CommitPending()) return;
            if (Selected == null || steps.SelectedIndex < 0) return; int start = BlockStart(steps.SelectedIndex), end = BlockEnd(start); Selected.Steps.RemoveRange(start, end - start + 1);
            shell.DraftDirty = true; CloseInlineEditor(); RefreshRows();
        }
    }
}
