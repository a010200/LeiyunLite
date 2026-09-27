using System;
using System.Windows.Forms;
using RazerBatteryTray.Macros;

namespace RazerBatteryTray
{
    public partial class MainForm
    {
        private MacroController macroController;
        private MacroEditorForm macroEditor;
        private string macroError;
        private void InitializeMacros()
        {
            try { macroController = new MacroController(new MacroStore(), new WindowsMacroOutput(), true); macroError = null; }
            catch (Exception ex)
            {
                macroError = ex.Message;
                trayIcon.ShowBalloonTip(4000, Theme.Title, "宏功能未启用：" + macroError, ToolTipIcon.Warning);
            }
        }
        private void ShowMacroEditor()
        {
            if (macroController == null) InitializeMacros();
            if (macroController == null)
            {
                MessageBox.Show(this, "宏功能无法启动，原有设备功能仍可使用。\n" + macroError +
                    "\n若配置损坏，请先备份 %LOCALAPPDATA%\\LeiyunLite\\macros.xml，再修复或移走该文件。", Theme.Title);
                return;
            }
            if (macroEditor == null || macroEditor.IsDisposed) macroEditor = new MacroEditorForm(macroController);
            macroEditor.Show(); macroEditor.WindowState = FormWindowState.Normal; macroEditor.Activate();
        }
        private void StopAllMacros() { if (macroController != null) macroController.Stop(); }
    }
}
