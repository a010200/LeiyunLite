using System;
using System.Collections.Generic;
using System.Text.RegularExpressions;

namespace RazerBatteryTray.Desktop
{
    // Plain-text presentation only; never modifies Offer.Notes or the GitHub release body.
    internal static class ReleaseNotesFormatter
    {
        internal static string Format(string notes)
        {
            var bullets = new List<string>(); bool fenced = false;
            foreach (string raw in (notes ?? "").Split('\n')) {
                string line = raw.Trim(); if (line.StartsWith("```")) { fenced = !fenced; continue; } if (fenced) continue;
                var match = Regex.Match(line, @"^[-*+]\s+(.+)$"); if (!match.Success) continue;
                string item = match.Groups[1].Value;
                if (Regex.IsMatch(item, @"(?i)(\b(?:TID|CRC|RSA|SHA256|byte|Tests?)\b|\d+\s*/\s*\d+|\.(?:cs|ps1|xaml)\b|验签|校验和|协议字段|测试数量)")) continue;
                item = Regex.Replace(item, @"\[([^\]]+)\]\([^)]+\)", "$1").Replace("`", "").Replace("**", "");
                if (item.Length > 100) continue;
                if (!bullets.Contains(item)) bullets.Add(item); if (bullets.Count == 4) break;
            }
            if (bullets.Count == 0) bullets.Add(Ui.T("修复了一些已知问题，提升软件稳定性。", "Fixed known issues and improved stability."));
            return "• " + string.Join("\n• ", bullets);
        }
    }
}
