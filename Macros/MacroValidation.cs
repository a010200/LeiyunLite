using System;
using System.Collections.Generic;

namespace RazerBatteryTray.Macros
{
    internal static class MacroValidation
    {
        public static void Validate(MacroLibrary library)
        {
            if (library == null || library.Version != 1 || library.Macros == null || library.Bindings == null)
                throw new InvalidOperationException("宏配置格式不受支持。");
            if (library.Macros.Count > 100 || library.Bindings.Count > 200) throw new InvalidOperationException("最多保存 100 个宏和 200 个绑定。");
            var ids = new HashSet<string>();
            foreach (var macro in library.Macros)
            {
                if (macro == null || string.IsNullOrWhiteSpace(macro.Id) || !ids.Add(macro.Id)) throw new InvalidOperationException("宏 ID 重复或无效。");
                if (string.IsNullOrWhiteSpace(macro.Name) || macro.Name.Length > 80) throw new InvalidOperationException("宏名称需要 1～80 个字符。");
                if (macro.Steps == null || macro.Steps.Count > 500) throw new InvalidOperationException(macro.Name + "：最多 500 个动作。");
                int depth = 0;
                foreach (var step in macro.Steps)
                {
                    if (step == null || !Enum.IsDefined(typeof(ActionKind), step.Kind) || !Enum.IsDefined(typeof(PressMode), step.Press))
                        throw new InvalidOperationException("动作类型无效。");
                    if (step.Kind == ActionKind.Delay && (step.Number < 0 || step.Number > 600000)) throw new InvalidOperationException("延迟范围为 0～600000 毫秒。");
                    if (step.Kind == ActionKind.Keyboard && (step.KeyCode < 8 || step.KeyCode > 254)) throw new InvalidOperationException("请选择有效的键盘键。");
                    if (step.Kind == ActionKind.Mouse && (!Enum.IsDefined(typeof(MouseAction), step.Mouse) || step.Number < 1 || step.Number > 1000))
                        throw new InvalidOperationException("鼠标滚动格数范围为 1～1000。");
                    if (step.Kind == ActionKind.LoopStart)
                    {
                        if (step.Number < 1 || step.Number > 1000 || ++depth > 8) throw new InvalidOperationException("循环次数范围为 1～1000，最多嵌套 8 层。");
                    }
                    if (step.Kind == ActionKind.LoopEnd && --depth < 0) throw new InvalidOperationException(macro.Name + "：循环结束前缺少循环开始。");
                    if ((step.Kind == ActionKind.Launch || step.Kind == ActionKind.Command || step.Kind == ActionKind.CallMacro) && string.IsNullOrWhiteSpace(step.Value))
                        throw new InvalidOperationException("请填写启动目标、命令程序或被调用的宏。");
                    if ((step.Value ?? "").Length > 20000 || (step.Arguments ?? "").Length > 20000) throw new InvalidOperationException("动作文本过长。");
                }
                if (depth != 0) throw new InvalidOperationException(macro.Name + "：循环开始与结束必须配对。");
            }
            var depths = new Dictionary<string, int>();
            foreach (var macro in library.Macros) CheckCalls(library, macro, new HashSet<string>(), depths);
            var bindingIds = new HashSet<string>(); var triggers = new HashSet<string>();
            foreach (var binding in library.Bindings)
            {
                if (binding == null || string.IsNullOrWhiteSpace(binding.Id) || !bindingIds.Add(binding.Id)) throw new InvalidOperationException("绑定 ID 重复或无效。");
                if (library.Find(binding.MacroId) == null) throw new InvalidOperationException("绑定引用的宏不存在。");
                if (!Enum.IsDefined(typeof(TriggerKind), binding.Trigger) || !Enum.IsDefined(typeof(RunMode), binding.Mode) || ((int)binding.Modifiers & ~15) != 0)
                    throw new InvalidOperationException("绑定类型无效。");
                if (binding.Trigger == TriggerKind.Keyboard && (binding.KeyCode < 8 || binding.KeyCode > 254 || IsModifier(binding.KeyCode)))
                    throw new InvalidOperationException("触发键不能只是 Ctrl、Alt、Shift 或 Win，请选择一个普通键。");
                if (binding.Trigger == TriggerKind.Keyboard && binding.KeyCode == 123 && (binding.Modifiers & (KeyModifiers.Control | KeyModifiers.Shift)) == (KeyModifiers.Control | KeyModifiers.Shift))
                    throw new InvalidOperationException("Ctrl+Shift+F12 保留为全局停止快捷键。");
                if (IsWheel(binding.Trigger) && binding.Mode == RunMode.WhileHeld) throw new InvalidOperationException("滚轮不支持按住重复模式。");
                if (binding.Enabled && !triggers.Add(binding.Signature)) throw new InvalidOperationException("同一个触发键组合只能启用一个绑定。");
            }
        }
        private static int CheckCalls(MacroLibrary library, MacroDefinition macro, HashSet<string> path, Dictionary<string, int> depths)
        {
            int depth;
            if (depths.TryGetValue(macro.Id, out depth)) return depth;
            if (!path.Add(macro.Id)) throw new InvalidOperationException("宏之间不能递归调用。");
            depth = 1;
            foreach (var step in macro.Steps)
                if (step.Kind == ActionKind.CallMacro)
                {
                    var target = library.Find(step.Value);
                    if (target == null) throw new InvalidOperationException(macro.Name + "：被调用的宏不存在。");
                    depth = Math.Max(depth, 1 + CheckCalls(library, target, path, depths));
                }
            path.Remove(macro.Id);
            if (depth > 16) throw new InvalidOperationException("宏调用深度不能超过 16 层。");
            depths.Add(macro.Id, depth); return depth;
        }
        internal static bool IsModifier(int key) { return key == 16 || key == 17 || key == 18 || key == 91 || key == 92 || (key >= 160 && key <= 165); }
        internal static bool IsWheel(TriggerKind kind) { return kind == TriggerKind.WheelUp || kind == TriggerKind.WheelDown; }
    }
}
