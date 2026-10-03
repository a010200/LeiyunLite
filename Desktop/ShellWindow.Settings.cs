using System;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Shapes;

namespace RazerBatteryTray.Desktop
{
    internal sealed partial class ShellWindow
    {
        private static UIElement CaptionGlyph(string kind)
        {
            string data = kind == "min" ? "M 1,6 L 11,6" : kind == "close" ? "M 1,1 L 11,11 M 11,1 L 1,11" : kind == "restore" ? "M 3,1 L 11,1 L 11,9 M 1,3 L 9,3 L 9,11 L 1,11 Z" : "M 1,1 L 11,1 L 11,11 L 1,11 Z";
            return new Path { Data = Geometry.Parse(data), Stroke = Ui.Foreground, StrokeThickness = 1, Width = 12, Height = 12, SnapsToDevicePixels = true, VerticalAlignment = VerticalAlignment.Center, HorizontalAlignment = HorizontalAlignment.Center };
        }
        private static Button CaptionButton(string kind, Action action)
        {
            var b = Ui.Button("", action); b.Content = CaptionGlyph(kind); b.Width = 46; b.Height = 44;
            if (kind == "close") b.SetResourceReference(FrameworkElement.StyleProperty, "CloseCaptionButton");
            b.ToolTip = kind == "min" ? Ui.T("最小化", "Minimize") : kind == "max" ? Ui.T("最大化 / 还原", "Maximize / restore") : Ui.T("关闭", "Close");
            b.MouseEnter += (s, e) => b.Background = kind == "close" ? Ui.Brush("#C42B1C") : Ui.Brush("#292929");
            b.MouseLeave += (s, e) => b.Background = Ui.Frame;
            return b;
        }
        // Persist one store per change. On failure, restore the model before rolling back the control.
        private void Change<T>(Func<T> read, Action<T> set, T value, bool legacy = false)
        {
            T previous = read(); set(value);
            try { if (!Demo) { if (legacy) legacyStore.Save(LegacySettings); else store.Save(Preferences); } }
            catch (Exception ex) { set(previous); Notice(Ui.T("设置未保存：", "Setting was not saved: ") + ex.Message); throw; }
            bool motionChanged = Ui.ReducedMotion != Preferences.ReducedMotion;
            Ui.ReducedMotion = Preferences.ReducedMotion;
            if (ThemeManager.Current != ThemeTokens.Normalize(Preferences.Theme)) ThemeManager.Apply(Preferences.Theme);
            if (motionChanged && Ui.ReducedMotion) ElasticSwitch.RefreshAll();
            refreshTimer.Interval = TimeSpan.FromMilliseconds(LegacySettings.RefreshInterval);
            if (Preferences.ReducedMotion) { foreach (var view in views) if (view != null) Ui.Stop(view); foreach (FrameworkElement drawer in overlay.Children) Ui.Stop(drawer); }
            if (tray != null) tray.PreferencesChanged();
        }
        private void SelectSetting(ComboBox combo, Action<int> action)
        {
            int saved = combo.SelectedIndex; bool reverting = false;
            combo.SelectionChanged += (s, e) => { if (reverting || combo.SelectedIndex < 0) return; try { action(combo.SelectedIndex); saved = combo.SelectedIndex; } catch { reverting = true; combo.SelectedIndex = saved; reverting = false; } };
        }
        private FrameworkElement BuildSettings()
        {
            var language = Ui.Combo(new[] { "跟随系统 / System", "简体中文", "English" }, Preferences.Language == "zh" ? 1 : Preferences.Language == "en" ? 2 : 0);
            SelectSetting(language, index => { if (macroPage != null && !macroPage.CommitPending()) throw new InvalidOperationException(); Change(() => Preferences.Language, v => Preferences.Language = v, new[] { "system", "zh", "en" }[index]); Dispatcher.BeginInvoke(new Action(() => RebuildPages(2))); });
            var theme = Ui.Combo(new[] { Ui.T("经典", "Classic"), "Fluent" }, ThemeTokens.Normalize(Preferences.Theme) == "fluent" ? 1 : 0);
            theme.Name = "ThemeSelector";
            SelectSetting(theme, index => { Change(() => Preferences.Theme, v => Preferences.Theme = v, index == 1 ? "fluent" : "classic"); Notice(Ui.T("主题已切换。", "Theme changed.")); });
            var interval = Ui.Combo(new[] { Ui.T("30 秒", "30 seconds"), Ui.T("1 分钟", "1 minute"), Ui.T("5 分钟", "5 minutes") }, LegacySettings.RefreshInterval == 30000 ? 0 : LegacySettings.RefreshInterval == 300000 ? 2 : 1);
            SelectSetting(interval, index => Change(() => LegacySettings.RefreshInterval, v => LegacySettings.RefreshInterval = v, new[] { 30000, 60000, 300000 }[index], true));
            var threshold = Ui.Combo(new[] { "10%", "15%", "20%" }, Preferences.LowBatteryThreshold == 10 ? 0 : Preferences.LowBatteryThreshold == 15 ? 1 : 2);
            SelectSetting(threshold, index => Change(() => Preferences.LowBatteryThreshold, v => Preferences.LowBatteryThreshold = v, new[] { 10, 15, 20 }[index]));
            return Ui.Scroll(Ui.Stack(Ui.Text(Ui.T("设置", "Settings"), 32),
                Ui.Card(Ui.Stack(Ui.Text(Ui.T("通用", "General"), 19), Ui.Text(Ui.T("显示语言", "Display language")), language,
                    Ui.Toggle(Ui.T("开机自启动", "Launch at sign-in"), !Demo && autoStart.IsCurrentExecutableEnabled(), b => { try { if (!Demo) { autoStart.SetEnabled(b); if (autoStart.IsCurrentExecutableEnabled() != b) throw new System.IO.IOException(Ui.T("开机启动设置未生效。", "Startup registration was not confirmed.")); } else Notice(Ui.T("安全预览不修改开机启动。", "Safe preview does not change startup registration.")); } catch (Exception ex) { Notice(ex.Message); throw; } }),
                    Ui.Toggle(Ui.T("开机时显示主窗口", "Show window at sign-in"), LegacySettings.AutoStartShowUI, b => Change(() => LegacySettings.AutoStartShowUI, v => LegacySettings.AutoStartShowUI = v, b, true)),
                    Ui.Toggle(Ui.T("关闭窗口时最小化到托盘", "Close window to tray"), Preferences.CloseToTray, b => Change(() => Preferences.CloseToTray, v => Preferences.CloseToTray = v, b)))),
                Ui.Card(Ui.Stack(Ui.Text(Ui.T("外观", "Appearance"), 19), Ui.Row(Ui.Text(Ui.T("主题", "Theme")), Ui.InfoTip(Ui.T("外观主题", "Appearance theme"), Ui.T("经典为深色主题；Fluent使用明亮白天配色。切换主题不会改变宏草稿或绑定。", "Classic is dark; Fluent uses a bright daytime palette. Switching preserves drafts and bindings."))), theme,
                    Ui.Toggle(Ui.T("减少动画", "Reduce motion"), Preferences.ReducedMotion, b => Change(() => Preferences.ReducedMotion, v => Preferences.ReducedMotion = v, b)),
                    Ui.Toggle(Ui.T("托盘弹出动画", "Animate tray flyouts"), Preferences.TrayAnimation, b => Change(() => Preferences.TrayAnimation, v => Preferences.TrayAnimation = v, b)))),
                Ui.Card(Ui.Stack(Ui.Text(Ui.T("设备与通知", "Device and notifications"), 19), Ui.Text(Ui.T("电量刷新间隔", "Battery refresh interval")), interval,
                    Ui.Toggle(Ui.T("低电量提醒", "Low battery alert"), LegacySettings.LowBatteryAlert, b => Change(() => LegacySettings.LowBatteryAlert, v => LegacySettings.LowBatteryAlert = v, b, true)),
                    Ui.Text(Ui.T("低电量阈值", "Low battery threshold")), threshold,
                    Ui.Toggle(Ui.T("设备连接 / 断开通知", "Device connection notifications"), Preferences.ConnectionNotifications, b => Change(() => Preferences.ConnectionNotifications, v => Preferences.ConnectionNotifications = v, b)))),
                Ui.Card(Ui.Stack(Ui.Text(Ui.T("关于", "About"), 19), Ui.Text(AppVersion.DisplayName), Ui.Text(Ui.T("独立开源修改版，非雷蛇官方产品。", "Independent open-source modification, not an official Razer product."), 13, Ui.Muted)))));
        }
    }
}
