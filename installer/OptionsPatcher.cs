using System.Reflection;
using System.Text;
using System.Text.RegularExpressions;

namespace QTranslateFix;

/// <summary>
/// Brings Options.json to the recommended configuration. On a machine that has
/// never run QTranslate the file does not exist yet, so a prepared template is
/// written instead. Where the file does exist, only the known keys are
/// rewritten, which preserves formatting and every setting not listed here.
/// </summary>
sealed class OptionsPatcher
{
    // QTranslate encodes a hotkey as (modifiers << 8) | virtual-key, where the
    // modifier bits are Alt=1, Ctrl=2, Shift=4, and 0x80 marks a double tap.
    const int DoubleTapCtrl = 0x8200;   // 33280
    const int CtrlAltQ = 0x0351;        // 849
    const int CtrlQ = 0x0251;           // 593
    const int CtrlE = 0x0245;           // 581

    /// <param name="KeepExisting">
    /// Leave the value alone when the file already has one. The OCR key is
    /// personal and is typed in per machine, so a later run of the installer
    /// must not wipe it - the blank default lives in the template instead.
    /// </param>
    /// <param name="Section">
    /// Restrict the edit to one top-level object. Needed for keys that appear
    /// more than once: "ShowServicesPane" exists under both "Dictionary" and
    /// "General", and only the latter controls the main window.
    /// </param>
    public sealed record Setting(
        string Key,
        string Value,
        string Description,
        bool KeepExisting = false,
        string Section = null);

    public static readonly Setting[] Settings =
    {
        new("OcrApiKey", "\"\"", "OCR API 金鑰留空，請自行申請後填入", KeepExisting: true),
        new("RemoveLineBreaks", "false", "關閉「移除換行字元」（開啟會讓標題與段落黏在一起）"),
        new("MouseMode", "1", "滑鼠模式：選取文字後直接顯示翻譯"),
        new("MouseModeOn", "true", "啟用滑鼠模式"),
        new("EnableMouseModeOnCtrl", "true", "按住 Ctrl 選取文字才翻譯"),
        new("InstantTranslation", "true", "主視窗即時翻譯"),
        new("HotKeyTextRecognition", "33280", "連點兩下 Ctrl：畫面框選翻譯"),
        new("HotKeyMainWindow", "849", "Ctrl+Alt+Q：主視窗"),
        new("HotKeyPopupWindow", "593", "Ctrl+Q：彈出視窗翻譯"),
        new("HotKeyListenText", "581", "Ctrl+E：朗讀選取文字"),
        new("MidSplitterPos", "1", "收合原文框，主視窗只顯示譯文"),
        new("ShowMiddlePane", "false", "隱藏語言選擇工具列"),
        new("ShowServicesPane", "false", "隱藏底部服務圖示列", Section: "General"),
    };

    readonly Action<string> _log;

    public OptionsPatcher(Action<string> log) => _log = log;

    public void Apply(string optionsPath)
    {
        _log("套用建議設定…");

        if (!File.Exists(optionsPath))
        {
            WriteTemplate(optionsPath);
            return;
        }

        var backup = optionsPath + ".before-fix";
        if (!File.Exists(backup))
        {
            File.Copy(optionsPath, backup, overwrite: false);
            _log("  已備份原設定為 Options.json.before-fix");
        }

        var json = File.ReadAllText(optionsPath);
        var missing = new List<string>();

        foreach (var setting in Settings)
        {
            if (setting.KeepExisting && HasValue(json, setting.Key))
            {
                _log($"  保留這台電腦原有的設定：{setting.Key}");
                continue;
            }

            if (TryApply(ref json, setting))
            {
                _log("  " + setting.Description);
            }
            else
            {
                missing.Add(setting.Key);
            }
        }

        // Options.json is written with a byte order mark; keep it that way.
        File.WriteAllText(optionsPath, json, new UTF8Encoding(true));

        if (missing.Count > 0)
        {
            _log("  這些設定在檔案中找不到，已略過：" + string.Join(", ", missing));
        }
    }

    void WriteTemplate(string optionsPath)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(optionsPath));

        using var stream = Assembly.GetExecutingAssembly().GetManifestResourceStream("Options.default.json")
                           ?? throw new InvalidOperationException("設定範本沒有正確嵌入這個執行檔。");
        using var reader = new StreamReader(stream, Encoding.UTF8);

        File.WriteAllText(optionsPath, reader.ReadToEnd(), new UTF8Encoding(true));
        _log("  這台電腦還沒有設定檔，已寫入預設設定");

        foreach (var setting in Settings)
        {
            _log("  " + setting.Description);
        }
    }

    /// <summary>True when the key is present and is not an empty string.</summary>
    static bool HasValue(string json, string key)
    {
        var match = new Regex("\"" + Regex.Escape(key) + "\"\\s*:\\s*\"((?:[^\"\\\\]|\\\\.)*)\"").Match(json);
        return match.Success && match.Groups[1].Value.Length > 0;
    }

    static bool TryApply(ref string json, Setting setting)
    {
        var start = 0;
        var length = json.Length;

        if (setting.Section is not null && !TryFindSection(json, setting.Section, out start, out length))
        {
            return false;
        }

        var slice = json.Substring(start, length);
        if (!TrySetValue(ref slice, setting.Key, setting.Value))
        {
            return false;
        }

        json = json[..start] + slice + json[(start + length)..];
        return true;
    }

    /// <summary>Locates the body of a top-level object by walking its braces.</summary>
    static bool TryFindSection(string json, string section, out int start, out int length)
    {
        start = 0;
        length = 0;

        var header = new Regex("\"" + Regex.Escape(section) + "\"\\s*:\\s*\\{").Match(json);
        if (!header.Success)
        {
            return false;
        }

        var depth = 0;
        var inString = false;
        var escaped = false;

        for (var i = header.Index + header.Length - 1; i < json.Length; i++)
        {
            var c = json[i];

            if (escaped)
            {
                escaped = false;
                continue;
            }

            if (inString)
            {
                if (c == '\\')
                {
                    escaped = true;
                }
                else if (c == '"')
                {
                    inString = false;
                }
                continue;
            }

            switch (c)
            {
                case '"':
                    inString = true;
                    break;
                case '{':
                    depth++;
                    break;
                case '}':
                    depth--;
                    if (depth == 0)
                    {
                        start = header.Index;
                        length = i - header.Index + 1;
                        return true;
                    }
                    break;
            }
        }

        return false;
    }

    static bool TrySetValue(ref string json, string key, string value)
    {
        // Match "Key": <json scalar>. Quoting the key inside the pattern stops
        // "MouseMode" from also matching inside "MouseModeOn".
        var pattern = "(\"" + Regex.Escape(key) + "\"\\s*:\\s*)(\"(?:[^\"\\\\]|\\\\.)*\"|-?\\d+(?:\\.\\d+)?|true|false|null)";
        var regex = new Regex(pattern);

        if (!regex.IsMatch(json))
        {
            return false;
        }

        json = regex.Replace(json, m => m.Groups[1].Value + value, 1);
        return true;
    }
}
