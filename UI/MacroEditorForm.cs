using System;
using System.Drawing;
using System.Windows.Forms;
using RazerBatteryTray.Macros;

namespace RazerBatteryTray
{
    internal sealed class MacroEditorForm : Form
    {
        private readonly MacroController controller;
        private MacroLibrary library;
        private readonly ListBox macroList = new ListBox { Dock = DockStyle.Fill, IntegralHeight = false, BorderStyle = BorderStyle.FixedSingle };
        private readonly TextBox nameBox = new TextBox { Dock = DockStyle.Fill };
        private readonly ListView steps = new ListView { Dock = DockStyle.Fill, View = View.Details, FullRowSelect = true, HideSelection = false, MultiSelect = false };
        private readonly ListView bindings = new ListView { Dock = DockStyle.Fill, View = View.Details, FullRowSelect = true, HideSelection = false, MultiSelect = false };
        private readonly CheckBox enableBindings = new CheckBox { Text = "启用全部按键绑定", AutoSize = true };
        private readonly Label status = new Label { Dock = DockStyle.Fill, AutoEllipsis = true, TextAlign = ContentAlignment.MiddleLeft };
        private readonly Label saveState = new Label { AutoSize = true, Text = "已保存" };
        private readonly System.Windows.Forms.Timer statusTimer = new System.Windows.Forms.Timer { Interval = 200 };
        private bool loading, dirty;
        private MacroDefinition Selected { get { return macroList.SelectedItem as MacroDefinition; } }
        internal MacroEditorForm(MacroController controller)
        {
            this.controller = controller; library = controller.Snapshot();
            status.Text = controller.Status + " · 编辑期间绑定暂停";
            Text = Theme.Title + " · 宏与按键绑定"; Font = new Font("Microsoft YaHei UI", 9F); AutoScaleMode = AutoScaleMode.Dpi;
            ClientSize = new Size(1030, 650); MinimumSize = new Size(850, 580); StartPosition = FormStartPosition.CenterScreen;
            var root = new TableLayoutPanel { Dock = DockStyle.Fill, Padding = new Padding(18), ColumnCount = 1, RowCount = 4 };
            root.RowStyles.Add(new RowStyle(SizeType.Absolute, 55)); root.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
            root.RowStyles.Add(new RowStyle(SizeType.Absolute, 46)); root.RowStyles.Add(new RowStyle(SizeType.Absolute, 35)); Controls.Add(root);
            var heading = new Label { Text = "宏与按键绑定", Font = new Font(Font.FontFamily, 19F, FontStyle.Bold), Dock = DockStyle.Fill };
            root.Controls.Add(heading, 0, 0);
            var tabs = new TabControl { Dock = DockStyle.Fill };
            var macrosTab = new TabPage("宏编辑"); var bindingsTab = new TabPage("按键 / 滚轮绑定");
            tabs.TabPages.Add(macrosTab); tabs.TabPages.Add(bindingsTab); root.Controls.Add(tabs, 0, 1);
            var split = new SplitContainer { Width = 900, Dock = DockStyle.Fill, SplitterDistance = 220, FixedPanel = FixedPanel.Panel1, Panel1MinSize = 190 };
            macrosTab.Controls.Add(split);
            var left = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 1, RowCount = 2, Padding = new Padding(8) };
            left.RowStyles.Add(new RowStyle(SizeType.Percent, 100)); left.RowStyles.Add(new RowStyle(SizeType.Absolute, 42));
            left.Controls.Add(macroList, 0, 0);
            var leftButtons = new FlowLayoutPanel { Dock = DockStyle.Fill, WrapContents = false };
            leftButtons.Controls.Add(Button("新建", () => NewMacro(), 56)); leftButtons.Controls.Add(Button("复制", () => CopyMacro(), 56)); leftButtons.Controls.Add(Button("删除", () => DeleteMacro(), 56));
            left.Controls.Add(leftButtons, 0, 1); split.Panel1.Controls.Add(left);
            var right = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 1, RowCount = 4, Padding = new Padding(8) };
            right.RowStyles.Add(new RowStyle(SizeType.Absolute, 35)); right.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
            right.RowStyles.Add(new RowStyle(SizeType.Absolute, 42)); right.RowStyles.Add(new RowStyle(SizeType.Absolute, 45)); split.Panel2.Controls.Add(right);
            right.Controls.Add(nameBox, 0, 0); right.Controls.Add(steps, 0, 1);
            steps.Columns.Add("序号", 50); steps.Columns.Add("动作", 110); steps.Columns.Add("内容", 410);
            var stepButtons = new FlowLayoutPanel { Dock = DockStyle.Fill, WrapContents = false };
            stepButtons.Controls.Add(Button("添加动作", () => EditStep(false), 85)); stepButtons.Controls.Add(Button("编辑", () => EditStep(true), 60));
            stepButtons.Controls.Add(Button("删除", () => DeleteStep(), 60)); stepButtons.Controls.Add(Button("上移", () => MoveStep(-1), 60)); stepButtons.Controls.Add(Button("下移", () => MoveStep(1), 60));
            right.Controls.Add(stepButtons, 0, 2);
            right.Controls.Add(new Label { Text = "按顺序执行。循环开始 / 结束需要配对。试运行有 3 秒准备时间，请切换到目标窗口。", Dock = DockStyle.Fill }, 0, 3);
            var bindingPanel = new TableLayoutPanel { Dock = DockStyle.Fill, RowCount = 3, ColumnCount = 1, Padding = new Padding(8) };
            bindingPanel.RowStyles.Add(new RowStyle(SizeType.Absolute, 34)); bindingPanel.RowStyles.Add(new RowStyle(SizeType.Percent, 100)); bindingPanel.RowStyles.Add(new RowStyle(SizeType.Absolute, 42));
            bindingPanel.Controls.Add(enableBindings, 0, 0); bindingPanel.Controls.Add(bindings, 0, 1);
            bindings.Columns.Add("状态", 65); bindings.Columns.Add("触发键", 190); bindings.Columns.Add("宏", 200); bindings.Columns.Add("执行方式", 220); bindings.Columns.Add("原始动作", 120);
            var bindingButtons = new FlowLayoutPanel { Dock = DockStyle.Fill };
            bindingButtons.Controls.Add(Button("新增绑定", () => EditBinding(false), 90)); bindingButtons.Controls.Add(Button("编辑绑定", () => EditBinding(true), 90)); bindingButtons.Controls.Add(Button("删除绑定", () => DeleteBinding(), 90));
            bindingPanel.Controls.Add(bindingButtons, 0, 2); bindingsTab.Controls.Add(bindingPanel);
            var commands = new FlowLayoutPanel { Dock = DockStyle.Fill, WrapContents = false };
            var save = Button("保存并应用", () => Save(), 110); commands.Controls.Add(save);
            commands.Controls.Add(Button("试运行选中宏", () => Preview(), 130)); commands.Controls.Add(Button("停止所有宏", () => controller.Stop(), 110));
            commands.Controls.Add(saveState); root.Controls.Add(commands, 0, 2); root.Controls.Add(status, 0, 3);
            macroList.SelectedIndexChanged += (s, e) => ShowSelected();
            nameBox.TextChanged += (s, e) => {
                if (loading || Selected == null) return;
                Selected.Name = nameBox.Text; MarkDirty();
            };
            nameBox.Leave += (s, e) => ReloadMacros(Selected == null ? null : Selected.Id);
            enableBindings.CheckedChanged += (s, e) => { if (!loading) { library.BindingsEnabled = enableBindings.Checked; MarkDirty(); } };
            steps.DoubleClick += (s, e) => EditStep(true); bindings.DoubleClick += (s, e) => EditBinding(true);
            statusTimer.Tick += (s, e) => status.Text = controller.Status + (Visible ? " · 编辑期间绑定暂停" : "");
            statusTimer.Start();
            VisibleChanged += (s, e) => controller.SuspendBindings(Visible);
            FormClosing += OnClosing;
            Theme.Apply(this); save.BackColor = Theme.Green; save.ForeColor = Theme.Black; heading.ForeColor = Theme.Green;
            ReloadMacros(null); ReloadBindings();
        }
        private Button Button(string text, Action action, int width)
        { var button = new Button { Text = text, Width = width, Height = 31 }; button.Click += (s, e) => { try { action(); } catch (Exception ex) { MessageBox.Show(this, ex.Message, Theme.Title); } }; return button; }
        private void MarkDirty() { dirty = true; saveState.Text = "未保存"; saveState.ForeColor = Theme.Green; }
        private void ReloadMacros(string id)
        {
            loading = true;
            macroList.Items.Clear(); foreach (var macro in library.Macros) macroList.Items.Add(macro);
            if (id != null) macroList.SelectedItem = library.Find(id);
            if (macroList.SelectedIndex < 0 && macroList.Items.Count > 0) macroList.SelectedIndex = 0;
            loading = false; ShowSelected();
        }
        private void ShowSelected()
        {
            loading = true; nameBox.Text = Selected == null ? "" : Selected.Name; nameBox.Enabled = Selected != null; loading = false;
            ReloadSteps(-1);
        }
        private void ReloadSteps(int select)
        {
            steps.Items.Clear(); if (Selected == null) return;
            int depth = 0;
            for (int i = 0; i < Selected.Steps.Count; i++)
            {
                var step = Selected.Steps[i]; if (step.Kind == ActionKind.LoopEnd) depth = Math.Max(0, depth - 1);
                var row = new ListViewItem((i + 1).ToString()); row.SubItems.Add(new string(' ', depth * 2) + MacroLabels.Actions[(int)step.Kind]);
                row.SubItems.Add(MacroLabels.Detail(step, library)); steps.Items.Add(row);
                if (step.Kind == ActionKind.LoopStart) depth++;
            }
            if (select >= 0 && select < steps.Items.Count) { steps.Items[select].Selected = true; steps.EnsureVisible(select); }
        }
        private void ReloadBindings()
        {
            loading = true; enableBindings.Checked = library.BindingsEnabled; loading = false;
            bindings.Items.Clear();
            foreach (var binding in library.Bindings)
            {
                var macro = library.Find(binding.MacroId);
                var row = new ListViewItem(binding.Enabled ? "启用" : "关闭") { Tag = binding };
                row.SubItems.Add(MacroLabels.Binding(binding)); row.SubItems.Add(macro == null ? "（宏不存在）" : macro.Name);
                row.SubItems.Add(MacroLabels.Modes[(int)binding.Mode]); row.SubItems.Add(binding.SuppressOriginal ? "拦截" : "保留"); bindings.Items.Add(row);
            }
        }
        private void NewMacro()
        { var macro = new MacroDefinition { Name = "新宏 " + (library.Macros.Count + 1) }; library.Macros.Add(macro); ReloadMacros(macro.Id); MarkDirty(); nameBox.Focus(); nameBox.SelectAll(); }
        private void CopyMacro()
        {
            if (Selected == null) return;
            var copy = library.Clone().Find(Selected.Id); copy.Id = Guid.NewGuid().ToString("N"); copy.Name += " 副本";
            library.Macros.Add(copy); ReloadMacros(copy.Id); MarkDirty();
        }
        private void DeleteMacro()
        {
            if (Selected == null) return;
            var id = Selected.Id;
            if (library.Bindings.Exists(b => b.MacroId == id) || library.Macros.Exists(m => m.Steps.Exists(s => s.Kind == ActionKind.CallMacro && s.Value == id)))
                throw new InvalidOperationException("这个宏仍被绑定或被其他宏调用，请先移除相应引用。");
            if (MessageBox.Show(this, "删除宏“" + Selected.Name + "”？", Theme.Title, MessageBoxButtons.YesNo) != DialogResult.Yes) return;
            library.Macros.Remove(Selected); ReloadMacros(null); MarkDirty();
        }
        private void EditStep(bool edit)
        {
            if (Selected == null) { NewMacro(); }
            int index = steps.SelectedIndices.Count == 0 ? -1 : steps.SelectedIndices[0];
            if (edit && index < 0) return;
            using (var dialog = new MacroStepDialog(library, Selected.Id, edit ? Selected.Steps[index] : null))
                if (dialog.ShowDialog(this) == DialogResult.OK)
                {
                    if (edit) Selected.Steps[index] = dialog.Result;
                    else { index = index < 0 ? Selected.Steps.Count : index + 1; Selected.Steps.Insert(index, dialog.Result); }
                    ReloadSteps(index); MarkDirty();
                }
        }
        private void DeleteStep()
        { if (Selected == null || steps.SelectedIndices.Count == 0) return; int i = steps.SelectedIndices[0]; Selected.Steps.RemoveAt(i); ReloadSteps(Math.Min(i, Selected.Steps.Count - 1)); MarkDirty(); }
        private void MoveStep(int offset)
        {
            if (Selected == null || steps.SelectedIndices.Count == 0) return;
            int i = steps.SelectedIndices[0], next = i + offset; if (next < 0 || next >= Selected.Steps.Count) return;
            var value = Selected.Steps[i]; Selected.Steps.RemoveAt(i); Selected.Steps.Insert(next, value); ReloadSteps(next); MarkDirty();
        }
        private void EditBinding(bool edit)
        {
            if (library.Macros.Count == 0) throw new InvalidOperationException("请先创建一个宏。");
            if (edit && bindings.SelectedItems.Count == 0) return;
            var original = edit ? (MacroBinding)bindings.SelectedItems[0].Tag : null;
            using (var dialog = new MacroBindingDialog(library, original))
                if (dialog.ShowDialog(this) == DialogResult.OK)
                {
                    if (original != null) library.Bindings.Remove(original); library.Bindings.Add(dialog.Result); ReloadBindings(); MarkDirty();
                }
        }
        private void DeleteBinding()
        { if (bindings.SelectedItems.Count == 0) return; library.Bindings.Remove((MacroBinding)bindings.SelectedItems[0].Tag); ReloadBindings(); MarkDirty(); }
        private bool Save()
        {
            try { controller.Save(library); dirty = false; saveState.Text = "已保存"; saveState.ForeColor = Color.WhiteSmoke; ReloadMacros(Selected == null ? null : Selected.Id); ReloadBindings(); return true; }
            catch (Exception ex) { MessageBox.Show(this, ex.Message, Theme.Title); return false; }
        }
        private void Preview()
        {
            if (Selected == null || Selected.Steps.Count == 0) throw new InvalidOperationException("请先添加动作。");
            if (controller.IsRunning) throw new InvalidOperationException("请先停止当前宏。");
            MacroValidation.Validate(library);
            string id = Selected.Id;
            Hide();
            if (!controller.Preview(library, id)) { Show(); throw new InvalidOperationException("宏未能启动。"); }
        }
        private void OnClosing(object sender, FormClosingEventArgs e)
        {
            if (!dirty || e.CloseReason != CloseReason.UserClosing) return;
            var answer = MessageBox.Show(this, "保存本次宏和绑定修改？", Theme.Title, MessageBoxButtons.YesNoCancel);
            if (answer == DialogResult.Cancel || (answer == DialogResult.Yes && !Save())) e.Cancel = true;
        }
        protected override void Dispose(bool disposing)
        {
            if (disposing) { statusTimer.Dispose(); controller.SuspendBindings(false); }
            base.Dispose(disposing);
        }
    }
}
