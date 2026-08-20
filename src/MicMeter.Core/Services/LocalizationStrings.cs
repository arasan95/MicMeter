namespace MicMeter.Services;

public static class LocalizationStrings
{
    public static IReadOnlyDictionary<string, string> EnglishLabels { get; } =
        new Dictionary<string, string>
        {
            ["MicMeter 設定"] = "MicMeter Settings",
            ["入力デバイス"] = "Input devices",
            ["↑ 上へ"] = "↑ Up",
            ["↓ 下へ"] = "↓ Down",
            ["一覧の上から順に表示されます。項目を選び、上下ボタンで表示順を変更できます。"] =
                "Devices are shown in this order. Select an item and use Up or Down to reorder it.",
            ["大きさ"] = "Scale",
            ["透明度"] = "Opacity",
            ["表示位置"] = "Placement",
            ["セグメント数"] = "Segments",
            ["メーター方向"] = "Meter orientation",
            ["横型"] = "Horizontal",
            ["縦型"] = "Vertical",
            ["テーマ"] = "Theme",
            ["Flat Black（角丸なし）"] = "Flat Black (square corners)",
            ["リスニング出力"] = "Listening output",
            ["システムのデフォルト"] = "System default",
            ["言語"] = "Language",
            ["常に手前に表示"] = "Always on top",
            ["Windows起動時に自動起動"] = "Start with Windows",
            ["起動時に自動起動"] = "Start at login",
            ["全ミュート・ホットキー"] = "Mute-all hotkey",
            ["表示項目"] = "Visible elements",
            ["デバイス名"] = "Device name",
            ["dB数値"] = "dB value",
            ["状態文字"] = "Status text",
            ["ミュート操作"] = "Mute control",
            ["リスニング操作"] = "Listening control",
            ["ピークホールド"] = "Peak hold",
            ["CLIP警告"] = "Clipping warning",
            ["トレイメーター"] = "Tray meter",
            ["メニューバーメーター"] = "Menu bar meter",
            ["ミュート通知音"] = "Mute/unmute sounds",
            ["ミュート表示"] = "Mute overlay",
            ["ミュート表示の位置を調整"] = "Position mute overlay",
            ["メーターバーの色と切替位置"] = "Meter colors and thresholds",
            ["低レベル"] = "Low level",
            ["中レベル"] = "Mid level",
            ["高レベル"] = "High level",
            ["中色へ切替 (dB)"] = "Mid threshold (dB)",
            ["高色へ切替 (dB)"] = "High threshold (dB)",
            ["細くリサイズすると文字は自動で隠れ、ミュートとリスニングは点表示になります。耳ボタンはヘッドホン使用を推奨します。"] =
                "Text is hidden automatically at compact sizes; mute and listening become status dots. Headphones are recommended when listening.",
            ["閉じる"] = "Close",
            ["終了"] = "Quit"
        };

    public static string Translate(string value, bool isEnglish)
    {
        if (isEnglish)
        {
            return EnglishLabels.TryGetValue(value, out var english) ? english : value;
        }

        foreach (var pair in EnglishLabels)
        {
            if (pair.Value == value)
            {
                return pair.Key;
            }
        }

        return value;
    }
}
