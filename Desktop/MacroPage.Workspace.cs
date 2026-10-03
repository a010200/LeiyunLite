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
        private FrameworkElement paletteCard;
        private Button compactPaletteButton;
        private Grid workspaceRoot, toolbar;
        private Button myTab, bindingTab, saveButton, recordButton, testButton, stopButton, newButton;
        private UiTabStrip tabs;
        private Grid libraryTools;
        private StackPanel runTools;
        private TextBlock emptyHint;
        private TextBlock inputDiagnostic;
        private Action<string> inputDiagnosticHandler;
        private bool diagnosticsSubscribed;
        private Border saveBar;
        private bool saveFailed;
        private Button propertyButton;
        private Border insertionLine;
        private ActionKind addingKind;
        private Point dragStart;
        private int dragIndex = -1;
        private bool inlineOpen;
        internal bool IsBindingsTab { get { return bindingWorkspace != null && bindingWorkspace.Visibility == Visibility.Visible; } }
        internal int SelectedStepIndex { get { return steps.SelectedIndex; } }
        internal void RestoreStep(int index) { refreshing = true; steps.SelectedIndex = index >= 0 && index < steps.Items.Count ? index : -1; refreshing = false; }
        internal void RestoreWorkspace(string selectedMacro, bool binding) { ReloadLibrary(selectedMacro); SetTab(binding); }
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
            var root = new Grid(); workspaceRoot = root; root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto }); root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto }); root.RowDefinitions.Add(new RowDefinition()); root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            var more = Tool(Ui.T("更多 ···", "More ···"), () => shell.OpenDrawer(Ui.T("管理宏", "Manage macro"), Ui.Stack(
                Ui.Button("+ " + Ui.T("新建宏", "New macro"), () => { shell.CloseDrawer(); NewMacro(); }),
                Ui.Button(Ui.T("重命名", "Rename"), Rename),
                Ui.Button(Ui.T("复制宏", "Duplicate"), () => { shell.CloseDrawer(); CopyMacro(); }), Ui.Button(Ui.T("删除宏", "Delete"), DeleteMacro))));
            saveButton = Tool(Ui.T("保存并应用", "Save and apply"), () => SaveDraft(), true); saveButton.Name = "SaveMacro";
            var titleRow = new DockPanel { Margin = new Thickness(0, 0, 0, 14) };
            var saving = Horizontal(dirty, saveButton); saving.VerticalAlignment = VerticalAlignment.Center;
            dirty.Margin = new Thickness(0, 0, 16, 0); dirty.VerticalAlignment = VerticalAlignment.Center;
            var saveRow = new DockPanel(); DockPanel.SetDock(saving, Dock.Right); saveRow.Children.Add(saving);
            saveRow.Children.Add(Ui.Text("Ctrl + S", 12, Ui.Muted));
            saveBar = Ui.Card(saveRow); saveBar.Name = "MacroSaveBar"; saveBar.Padding = new Thickness(14, 8, 6, 8); saveBar.Margin = new Thickness(0, 12, 0, 0); Grid.SetRow(saveBar, 3); root.Children.Add(saveBar);
            var title = Ui.Text(Ui.T("宏与绑定", "Macros and bindings"), 30); title.Margin = new Thickness(0); titleRow.Children.Add(title);
            myTab = Tool(Ui.T("我的宏", "My macros"), () => SelectTab(false)); bindingTab = Tool(Ui.T("按键绑定", "Key bindings"), () => SelectTab(true));
            tabs = Ui.TabStrip(myTab, bindingTab); tabs.Margin = new Thickness(0, 0, 0, 18);
            root.Children.Add(Ui.Stack(titleRow, tabs));
            recordButton = Tool("● " + Ui.T("录制", "Record"), OpenRecording);
            testButton = Tool(Ui.T("试运行", "Test run"), Preview);
            stopButton = Tool(Ui.T("停止", "Stop"), () => { if (IsRecording) EndRecording(false); else if (shell.Macros != null) shell.Macros.Stop(); });
            stopButton.ToolTip = "Ctrl + Shift + F12";
            compactPaletteButton = Tool("+ " + Ui.T("添加动作", "Add action"), () => shell.OpenDrawer(Ui.T("添加动作", "Add action"), CreateActionPalette()));
            propertyButton = Tool(Ui.T("属性", "Properties"), () => EditStep(true));
            runTools = Horizontal(compactPaletteButton, propertyButton, recordButton, testButton, stopButton); runTools.HorizontalAlignment = HorizontalAlignment.Right;
            libraryTools = new Grid { HorizontalAlignment = HorizontalAlignment.Left }; libraryTools.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto }); libraryTools.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto }); libraryTools.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            library.MinWidth = 130; library.Width = 250; library.Height = 36; library.Margin = new Thickness(0, 0, 8, 0); library.HorizontalAlignment = HorizontalAlignment.Left;
            newButton = Tool("+ " + Ui.T("新建宏", "New macro"), NewMacro); Grid.SetColumn(newButton, 1); Grid.SetColumn(more, 2);
            libraryTools.Children.Add(library); libraryTools.Children.Add(newButton); libraryTools.Children.Add(more);
            toolbar = new Grid { Margin = new Thickness(0, 0, 0, 16) }; toolbar.ColumnDefinitions.Add(new ColumnDefinition()); toolbar.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            toolbar.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto }); toolbar.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            libraryTools.Margin = new Thickness(0, 0, 24, 0); toolbar.Children.Add(libraryTools); Grid.SetColumn(runTools, 1); toolbar.Children.Add(runTools);
            inputDiagnostic = Ui.Text(shell.Macros == null ? Ui.T("安全预览不监听输入。", "Safe preview does not monitor input.") : shell.Macros.InputDiagnostic, 12, Ui.Muted);
            inputDiagnostic.TextWrapping = TextWrapping.Wrap; inputDiagnostic.Margin = new Thickness(0, 0, 0, 10);
            var copyTiming = Tool(Ui.T("复制输入时序", "Copy input timing"), () => {
                if (shell.Macros == null) return;
                try { Clipboard.SetText(shell.Macros.CopyInputTiming()); shell.Notice(Ui.T("输入时序已复制。", "Input timing copied.")); }
                catch (Exception ex) { shell.Notice(Ui.T("复制失败：", "Copy failed: ") + ex.Message); }
            });
            copyTiming.IsEnabled = shell.Macros != null;
            inputDiagnostic.Name = "InputDiagnostic"; copyTiming.Name = "CopyInputTiming";
            inputDiagnostic.Visibility = copyTiming.Visibility = DesktopApp.Diagnostics ? Visibility.Visible : Visibility.Collapsed;
            copyTiming.HorizontalAlignment = HorizontalAlignment.Left;
            var toolsAndStatus = Ui.Stack(toolbar, inputDiagnostic, copyTiming, BuildRecordingStatus()); Grid.SetRow(toolsAndStatus, 1); root.Children.Add(toolsAndStatus);
            workspace = new Grid(); workspace.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(160) }); workspace.ColumnDefinitions.Add(new ColumnDefinition()); workspace.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(270) }); Grid.SetRow(workspace, 2); root.Children.Add(workspace);
            var paletteScroll = Ui.Scroll(CreateActionPalette()); paletteScroll.Padding = new Thickness(0);
            var actionCard = Ui.Card(paletteScroll); paletteCard = actionCard; recordingPalette = actionCard; actionCard.Padding = new Thickness(10, 14, 4, 10); actionCard.Margin = new Thickness(0, 0, 10, 0); workspace.Children.Add(actionCard);
            var timeline = new DockPanel(); var timelineTools = Ui.Row(Ui.Button("↑", () => MoveStep(-1)), Ui.Button("↓", () => MoveStep(1)), Ui.Button(Ui.T("编辑", "Edit"), () => EditStep(true)), Ui.Button(Ui.T("删除", "Delete"), DeleteStep));
            DockPanel.SetDock(timelineTools, Dock.Bottom); timeline.Children.Add(timelineTools);
            recordingTools = timelineTools;
            var tip = Ui.Text(Ui.T("动作序列 · 拖动排序", "Sequence · drag to reorder"), 13, Ui.Muted); DockPanel.SetDock(tip, Dock.Top); timeline.Children.Add(tip);
            var sequence = new Grid(); sequence.Children.Add(steps);
            insertionLine = new Border { Height = 2, Background = Ui.Accent, VerticalAlignment = VerticalAlignment.Top, IsHitTestVisible = false, Visibility = Visibility.Collapsed }; sequence.Children.Add(insertionLine);
            emptyHint = Ui.Text(Ui.T("从左侧添加动作，或开始录制", "Add an action on the left, or start recording"), 14, Ui.Muted);
            emptyHint.HorizontalAlignment = HorizontalAlignment.Center; emptyHint.VerticalAlignment = VerticalAlignment.Center; emptyHint.IsHitTestVisible = false;
            sequence.Children.Add(emptyHint); sequence.Children.Add(BuildRecordingPad()); timeline.Children.Add(sequence);
            var timelineCard = Ui.Card(timeline); timelineCard.Padding = new Thickness(12); timelineCard.Margin = new Thickness(0); Grid.SetColumn(timelineCard, 1); workspace.Children.Add(timelineCard);
            properties = Ui.Card(Ui.Scroll(editor)); properties.Padding = new Thickness(16); properties.Margin = new Thickness(10, 0, 0, 0); Grid.SetColumn(properties, 2); workspace.Children.Add(properties);
            bindingWorkspace = BuildBindingWorkspace(); Grid.SetRow(bindingWorkspace, 2); root.Children.Add(bindingWorkspace);
            library.SelectionChanged += (s, e) => { if (refreshing) return; if (!CommitPending()) { refreshing = true; library.SelectedItem = editingMacro; refreshing = false; return; } ShowSelected(); }; steps.MouseDoubleClick += (s, e) => EditStep(true); bindings.MouseDoubleClick += (s, e) => EditBinding(true);
            steps.SelectionChanged += (s, e) => { if (!refreshing && steps.SelectedIndex >= 0 && CurrentLayout == LayoutMode.Wide) EditStep(true); };
            steps.AllowDrop = true;
            steps.PreviewMouseLeftButtonDown += (s, e) => { dragStart = e.GetPosition(steps); var row = ItemsControl.ContainerFromElement(steps, e.OriginalSource as DependencyObject) as ListBoxItem; dragIndex = row == null ? -1 : steps.Items.IndexOf(row); };
            steps.PreviewMouseMove += (s, e) => { if (dragIndex < 0 || e.LeftButton != MouseButtonState.Pressed) return; var point = e.GetPosition(steps); if (Math.Abs(point.X - dragStart.X) < SystemParameters.MinimumHorizontalDragDistance && Math.Abs(point.Y - dragStart.Y) < SystemParameters.MinimumVerticalDragDistance) return; int source = dragIndex; dragIndex = -1; var row = steps.Items[source] as ListBoxItem; if (row != null) row.Opacity = .7; try { DragDrop.DoDragDrop(steps, new DataObject("LeiyunStep", source), DragDropEffects.Move); } finally { if (row != null) row.Opacity = 1; insertionLine.Visibility = Visibility.Collapsed; } };
            steps.DragOver += (s, e) => { if (!e.Data.GetDataPresent("LeiyunStep")) return; var row = ItemsControl.ContainerFromElement(steps, e.OriginalSource as DependencyObject) as ListBoxItem; double y = row == null ? Math.Min(steps.ActualHeight - 2, steps.Items.Count * 42) : row.TranslatePoint(new Point(), steps).Y; insertionLine.Margin = new Thickness(0, Math.Max(0, y), 0, 0); insertionLine.Visibility = Visibility.Visible; e.Effects = DragDropEffects.Move; e.Handled = true; };
            steps.DragLeave += (s, e) => insertionLine.Visibility = Visibility.Collapsed;
            steps.Drop += (s, e) => { insertionLine.Visibility = Visibility.Collapsed; if (!e.Data.GetDataPresent("LeiyunStep")) return; var row = ItemsControl.ContainerFromElement(steps, e.OriginalSource as DependencyObject) as ListBoxItem; int to = row == null ? steps.Items.Count : steps.Items.IndexOf(row); Reorder((int)e.Data.GetData("LeiyunStep"), to); UiMotion.Fade(steps, .7, 120); e.Handled = true; };
            SizeChanged += (s, e) => UpdatePropertyLayout();
            Content = root; ReloadLibrary(null); SetTab(false); InitializeRecording(); InitializeInputDiagnostics();
        }
        private void InitializeInputDiagnostics()
        {
            if (shell.Macros == null || !DesktopApp.Diagnostics) return;
            inputDiagnosticHandler = message => Dispatcher.BeginInvoke(new Action(() => inputDiagnostic.Text = message));
            Loaded += (s, e) => { if (!diagnosticsSubscribed) { shell.Macros.InputDiagnosticChanged += inputDiagnosticHandler; diagnosticsSubscribed = true; inputDiagnostic.Text = shell.Macros.InputDiagnostic; } };
            Unloaded += (s, e) => { if (diagnosticsSubscribed) { shell.Macros.InputDiagnosticChanged -= inputDiagnosticHandler; diagnosticsSubscribed = false; } };
        }
        private void SelectTab(bool binding)
        {
            if (!CommitPending()) return;
            if (binding && !IsBindingsTab && shell.DraftDirty) {
                shell.OpenDrawer(Ui.T("宏有未保存修改", "Unsaved macro changes"), Ui.Stack(
                    Ui.Text(Ui.T("保存后才能分配最新的宏动作。", "Save before assigning the latest macro actions."), 14, Ui.Muted),
                    Ui.Button(Ui.T("保存并进入按键绑定", "Save and open bindings"), () => { if (SaveDraft()) { shell.CloseDrawer(); SetTab(true); } }, true),
                    Ui.Button(Ui.T("继续编辑", "Keep editing"), shell.CloseDrawer)));
                return;
            }
            SetTab(binding);
        }
        private void SetTab(bool binding)
        {
            saveBar.Visibility = binding ? Visibility.Collapsed : Visibility.Visible;
            RefreshBindingMap();
            libraryTools.Visibility = runTools.Visibility = binding ? Visibility.Collapsed : Visibility.Visible;
            workspace.Visibility = binding ? Visibility.Collapsed : Visibility.Visible; bindingWorkspace.Visibility = binding ? Visibility.Visible : Visibility.Collapsed;
            myTab.Background = bindingTab.Background = Brushes.Transparent; myTab.BorderThickness = bindingTab.BorderThickness = new Thickness(0);
            myTab.Foreground = binding ? Ui.Muted : Ui.Foreground; bindingTab.Foreground = binding ? Ui.Foreground : Ui.Muted;
            tabs.Select(binding ? 1 : 0);
            Ui.Enter(binding ? bindingWorkspace : workspace);
        }
        private static StackPanel Horizontal(params UIElement[] items) { var panel = new StackPanel { Orientation = Orientation.Horizontal }; foreach (var item in items) panel.Children.Add(item); return panel; }
        private static Button Tool(string label, Action action, bool primary = false) { var b = Ui.Button(label, action, primary); b.Height = 36; b.Padding = new Thickness(12, 0, 12, 0); b.Margin = new Thickness(0, 0, 8, 0); return b; }
        private static UIElement BindingColumns(params string[] values)
        {
            var row = new Grid(); double[] widths = { 1.4, 1.2, 1.4, .55, .55 };
            for (int i = 0; i < values.Length; i++) { row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(widths[i], GridUnitType.Star) }); var text = Ui.Text(values[i], 13); text.Margin = new Thickness(4, 7, 4, 7); text.TextWrapping = TextWrapping.NoWrap; text.TextTrimming = TextTrimming.CharacterEllipsis; text.ToolTip = values[i]; Grid.SetColumn(text, i); row.Children.Add(text); } return row;
        }
        internal bool SaveDraft() { if (IsRecording) return false; saveFailed = false; bool saved = shell.SaveMacros(); UpdateDirty(); return saved; }
        internal void SaveFailed() { saveFailed = true; UpdateDirty(); }
        private void UpdateDirty() { dirty.Text = recording ? Ui.T("正在录制", "Recording") : saveFailed ? Ui.T("保存失败 · 请重试", "Save failed · retry") : shell.DraftDirty || pendingEdit ? Ui.T("● 有未保存修改", "● Unsaved changes") : Ui.T("已保存", "Saved"); dirty.Foreground = saveFailed ? Ui.Brush("Danger") : Ui.Muted; UpdateRecordingControls(); }
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
            if (CurrentLayout != LayoutMode.Wide) { shell.OpenDrawer(title, content); return; }
            editor.Children.Clear(); editor.Children.Add(Ui.Text(title, 18)); editor.Children.Add(content); inlineOpen = true; UpdatePropertyLayout();
            var move = new TranslateTransform(); editor.RenderTransform = move; UiMotion.To(move, TranslateTransform.XProperty, 0, 150, 8); UiMotion.Fade(editor, 0, 150);
        }
        private LayoutMode CurrentLayout { get { return ResponsiveLayout.ForWidth(shell.ActualWidth > 0 ? shell.ActualWidth : shell.Width); } }
        private StackPanel CreateActionPalette()
        {
            var palette = Ui.Stack(Ui.Text(Ui.T("常用动作", "Common actions"), 15));
            var advanced = new StackPanel();
            for (int i = 0; i < 8; i++) {
                var kind = (ActionKind)i;
                var b = Ui.Button("+  " + Actions[i], () => { if (Selected == null) { shell.Notice(Ui.T("请先新建一个宏。", "Create a macro first.")); return; } addingKind = kind; EditStep(false); });
                b.Padding = new Thickness(8, 10, 4, 10); b.HorizontalContentAlignment = HorizontalAlignment.Left; b.Background = Brushes.Transparent; b.BorderThickness = new Thickness(0);
                if (i < 3) palette.Children.Add(b);
                else {
                    b.Foreground = Ui.Muted; b.Margin = new Thickness(0, 0, 0, 8); b.Padding = new Thickness(8, 10, 0, 10);
                    var hint = Ui.InfoTip("", kind == ActionKind.Command ? Ui.T("只运行可信的可执行程序。", "Run only trusted executables.") : Ui.T("高级动作；请检查目标与参数。", "Advanced action; check its target and parameters."));
                    hint.Margin = new Thickness(5, 0, 0, 8);
                    var row = new StackPanel { Orientation = Orientation.Horizontal }; row.Children.Add(b); row.Children.Add(hint); advanced.Children.Add(row);
                }
            }
            var group = Ui.Expander(Ui.T("高级动作", "Advanced actions"), advanced); group.IsExpanded = true; palette.Children.Add(group);
            return palette;
        }
        private void UpdatePropertyLayout()
        {
            if (workspace == null || newButton == null) return;
            bool wide = CurrentLayout == LayoutMode.Wide, compact = CurrentLayout == LayoutMode.Compact;
            newButton.Visibility = wide ? Visibility.Visible : Visibility.Collapsed; library.Width = wide ? 250 : 160;
            Grid.SetRow(runTools, wide ? 0 : 1); Grid.SetColumn(runTools, wide ? 1 : 0); Grid.SetColumnSpan(runTools, wide ? 1 : 2);
            runTools.Margin = new Thickness(0, wide ? 0 : 8, 0, 0); runTools.HorizontalAlignment = wide ? HorizontalAlignment.Right : HorizontalAlignment.Left;
            libraryTools.Margin = new Thickness(0, 0, wide ? 24 : 0, 0);
            compactPaletteButton.Visibility = compact ? Visibility.Visible : Visibility.Collapsed;
            propertyButton.Visibility = wide ? Visibility.Collapsed : Visibility.Visible;
            workspace.ColumnDefinitions[0].Width = new GridLength(compact ? 0 : 160);
            paletteCard.Visibility = compact ? Visibility.Collapsed : Visibility.Visible;
            workspace.ColumnDefinitions[2].Width = new GridLength(wide ? 270 : 0);
            properties.Visibility = wide || inlineOpen ? Visibility.Visible : Visibility.Collapsed;
            // Keep the SAME editor if resized mid-edit; never commit or lose draft input.
            Grid.SetColumn(properties, wide ? 2 : 1); Grid.SetColumnSpan(properties, wide ? 1 : 2);
            properties.Width = wide ? double.NaN : Math.Min(390, Math.Max(250, ActualWidth - 20));
            properties.HorizontalAlignment = wide ? HorizontalAlignment.Stretch : HorizontalAlignment.Right;
            Panel.SetZIndex(properties, wide ? 0 : 2);
        }
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
