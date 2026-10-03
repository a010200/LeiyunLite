using System;
using System.Collections.Generic;
using System.Windows;
using System.Windows.Media;

namespace RazerBatteryTray.Desktop
{
    // Semantic roles are shared by both themes. Hex aliases are a migration bridge,
    // not another theme: existing drawings continue to receive the same live brushes.
    internal static class ThemeTokens
    {
        internal static readonly Dictionary<string, string> Classic = new Dictionary<string, string> {
            {"WindowBackground", "#040404"}, {"NavigationBackground", "#080808"}, {"PageBackground", "#141414"},
            {"CardBackground", "#191919"}, {"CardHoverBackground", "#202020"}, {"CardPressedBackground", "#222222"}, {"CardBorder", "#303030"},
            {"ControlBackground", "#222222"}, {"ControlHoverBackground", "#292929"}, {"ControlPressedBackground", "#333333"}, {"ControlDisabledBackground", "#202020"},
            {"Accent", "#44D62C"}, {"AccentHover", "#58E341"}, {"AccentPressed", "#36BA21"}, {"AccentMuted", "#253A21"}, {"AccentText", "#141414"},
            {"TextPrimary", "#F3F3F3"}, {"TextSecondary", "#AAAAAA"}, {"TextDisabled", "#777777"},
            {"SelectionBackground", "#253A21"}, {"SelectionIndicator", "#44D62C"},
            {"Success", "#44D62C"}, {"Warning", "#F5C969"}, {"Danger", "#C42B1C"}, {"Divider", "#353535"}, {"Overlay", "#96000000"},
            {"ControlBorder", "#484848"}, {"InputBackground", "#0F0F0F"}, {"ToggleOff", "#444444"}, {"ScrollThumb", "#505050"}, {"ToggleThumb", "#F3F3F3"}
        };
        internal static readonly Dictionary<string, string> Fluent = new Dictionary<string, string> {
            {"WindowBackground", "#FAFAFA"}, {"NavigationBackground", "#FAFAFA"}, {"PageBackground", "#F2F2F2"},
            {"CardBackground", "#FFFFFF"}, {"CardHoverBackground", "#FFFFFF"}, {"CardPressedBackground", "#ECECEC"}, {"CardBorder", "#DADADA"},
            {"ControlBackground", "#FFFFFF"}, {"ControlHoverBackground", "#ECECEC"}, {"ControlPressedBackground", "#ECECEC"}, {"ControlDisabledBackground", "#F2F2F2"},
            {"Accent", "#44D62C"}, {"AccentHover", "#58E341"}, {"AccentPressed", "#36BA21"}, {"AccentMuted", "#BCEFB3"}, {"AccentText", "#141414"},
            {"TextPrimary", "#1B1B1B"}, {"TextSecondary", "#5F5F5F"}, {"TextDisabled", "#878787"},
            {"SelectionBackground", "#E6E6E6"}, {"SelectionIndicator", "#44D62C"},
            {"Success", "#44D62C"}, {"Warning", "#F5C969"}, {"Danger", "#C42B1C"}, {"Divider", "#DADADA"}, {"Overlay", "#96000000"},
            {"ControlBorder", "#B8B8B8"}, {"InputBackground", "#FFFFFF"}, {"ToggleOff", "#878787"}, {"ScrollThumb", "#A0A0A0"}, {"ToggleThumb", "#FFFFFF"}
        };
        internal static readonly Dictionary<string, string> Aliases = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase) {
            {"#040404", "WindowBackground"}, {"#080808", "NavigationBackground"}, {"#141414", "PageBackground"},
            {"#191919", "CardBackground"}, {"#202020", "CardHoverBackground"}, {"#222222", "ControlBackground"}, {"#0F0F0F", "InputBackground"},
            {"#303030", "CardBorder"}, {"#353535", "Divider"}, {"#343434", "Divider"}, {"#444444", "ToggleOff"}, {"#484848", "ControlBorder"},
            {"#505050", "ScrollThumb"}, {"#333333", "ControlPressedBackground"}, {"#292929", "ControlHoverBackground"},
            {"#253A21", "SelectionBackground"}, {"#34682B", "AccentMuted"}, {"#44D62C", "Accent"},
            {"#F3F3F3", "TextPrimary"}, {"#AAAAAA", "TextSecondary"}, {"#BBBBBB", "TextSecondary"}, {"#D2D2D2", "TextPrimary"}, {"#CCCCCC", "TextSecondary"},
            {"#C42B1C", "Danger"}
        };
        internal static string Normalize(string theme) { return theme == "fluent" || theme == "light" ? "fluent" : "classic"; }
        internal static string Role(string key) { string role; return Aliases.TryGetValue(key, out role) ? role : key; }
        internal static Color ColorFor(string key, string theme)
        {
            string color; var palette = Normalize(theme) == "fluent" ? Fluent : Classic;
            return (Color)ColorConverter.ConvertFromString(palette.TryGetValue(Role(key), out color) ? color : key);
        }
        // Theme chooser swatches represent BOTH themes, not the currently active palette.
        internal static Brush Swatch(string role, string theme) { var brush = new SolidColorBrush(ColorFor(role, theme)); brush.Freeze(); return brush; }
    }
    internal static class ThemeManager
    {
        internal static string Current = "classic";
        internal static void Apply(string theme) { Current = ThemeTokens.Normalize(theme); Ui.RefreshPalette(Current); ElasticSwitch.RefreshAll(); }
        internal static void Install(Application app)
        {
            foreach (string key in ThemeTokens.Classic.Keys) app.Resources[key] = Ui.Brush(key);
            foreach (string key in ThemeTokens.Aliases.Keys) app.Resources["C" + key.Substring(1)] = Ui.Brush(key);
            app.Resources["Ink"] = Ui.Ink;
        }
    }
}
