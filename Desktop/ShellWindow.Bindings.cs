using System;
using System.Linq;
using RazerBatteryTray.Macros;

namespace RazerBatteryTray.Desktop
{
    internal sealed partial class ShellWindow
    {
        private MacroLibrary demoActive = new MacroLibrary();
        internal MacroLibrary ActiveMacros { get { return Demo ? demoActive.Clone() : Macros.Snapshot(); } }
        internal bool SafetySavePending { get { return !Demo && Macros.SafetySavePending; } }
        internal void SyncBindingDraft()
        {
            var active = ActiveMacros; Draft.Bindings = active.Bindings; Draft.BindingsEnabled = active.BindingsEnabled;
        }
        internal bool BindingChange(Action<MacroController> change, Action<MacroLibrary> preview)
        {
            try
            {
                if (Demo) { var next = demoActive.Clone(); preview(next); MacroValidation.Validate(next); demoActive = next; }
                else change(Macros);
                Notice(Demo ? Ui.T("演示：仅在内存中更改，不发送输入。", "Demo: memory only, no input sent.") : Ui.T("绑定已保存并立即生效。", "Binding saved and applied immediately."));
                return true;
            }
            catch (Exception ex)
            {
                Notice((SafetySavePending ? Ui.T("本次已停用，但未能保存；重启可能恢复旧配置。请重试保存：", "Stopped for this session, but saving failed; restarting may restore old settings. Retry: ") : "") + ex.Message);
                return false;
            }
            finally { SyncBindingDraft(); if (macroPage != null) macroPage.RefreshRows(); }
        }
        internal bool RemoveSavedBinding(string id)
        {
            return BindingChange(c => c.RemoveBinding(id), l => l.Bindings.RemoveAll(b => b.Id == id));
        }
        internal bool SetSavedBinding(string id, bool enabled)
        {
            return BindingChange(c => c.SetBindingEnabled(id, enabled), l => l.Bindings.Find(b => b.Id == id).Enabled = enabled);
        }
        internal bool SaveBinding(MacroBinding binding, bool replace)
        {
            return BindingChange(c => c.ApplyBinding(binding, replace), l => {
                var macro = l.Find(binding.MacroId); if (macro == null || macro.Steps.Count == 0) throw new InvalidOperationException(Ui.T("请先保存一个包含动作的宏。", "Save a non-empty macro first."));
                l.Bindings.RemoveAll(b => b.Id == binding.Id || (replace && b.Signature == binding.Signature)); l.Bindings.Add(binding);
            });
        }
        internal bool IsMacroSaved(string id)
        {
            var a = ActiveMacros.Find(id); var b = Draft.Find(id);
            if (a == null || b == null || a.Name != b.Name || a.Steps.Count == 0 || a.Steps.Count != b.Steps.Count) return false;
            return a.Steps.Zip(b.Steps, (x, y) => x.Kind == y.Kind && x.Press == y.Press && x.KeyCode == y.KeyCode && x.Mouse == y.Mouse && x.Number == y.Number && x.Value == y.Value && x.Arguments == y.Arguments).All(x => x);
        }
    }
}
