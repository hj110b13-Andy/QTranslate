using System.Reflection;
using System.Text;
using System.Text.RegularExpressions;

namespace QTranslateFix;

/// <summary>
/// Brings Options.json to the wanted configuration.
///
/// Uninstalling QTranslate leaves Options.json behind, so a machine that once
/// had a different build still carries that build's hotkeys and mouse settings.
/// Merging only the keys this installer knows about is not enough to dislodge
/// them, which is why <see cref="Mode.Replace"/> exists: it drops the whole
/// file and writes the prepared one, so every machine ends up behaving the same.
/// </summary>
sealed class OptionsPatcher
{
    public enum Mode
    {
        /// <summary>Overwrite the whole file. The OCR key is still carried over.</summary>
        Replace,

        /// <summary>Change only the keys listed below, leave everything else alone.</summary>
        Merge,
    }

    // QTranslate encodes a hotkey as (modifiers << 8) | virtual-key, where the
    // modifier bits are Alt=1, Ctrl=2, Shift=4, and 0x80 marks a double tap.
    const int DoubleTapCtrl = 0x8200;   // 33280
    const int CtrlAltQ = 0x0351;        // 849
    const int CtrlQ = 0x0251;           // 593
    const int CtrlE = 0x0245;           // 581

    const string OcrKeyName = "OcrApiKey";

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
        new(OcrKeyName, "\"\"", "OCR API 金鑰留空，請自行申請後填入", KeepExisting: true),
        new("RemoveLineBreaks", "false", "關閉「移除換行字元」（開啟會讓標題與段落黏在一起）"),
        new("MouseMode", "1", "滑鼠模式：選取文字後直接顯示翻譯"),
        new("MouseModeOn", "true", "啟用滑鼠模式"),
        new("EnableMouseModeOnCtrl", "true", "按住 Ctrl 選取文字才翻譯"),
        new("InstantTranslation", "true", "主視窗即時翻譯"),
        new("EnableHotKeys", "true", "啟用全域快速鍵"),
        new("HotKeyTextRecognition", DoubleTapCtrl.ToString(), "連點兩下 Ctrl：畫面框選翻譯"),
        new("HotKeyMainWindow", CtrlAltQ.ToString(), "Ctrl+Alt+Q：主視窗"),
        new("HotKeyPopupWindow", CtrlQ.ToString(), "Ctrl+Q：彈出視窗翻譯"),
        new("HotKeyListenText", CtrlE.ToString(), "Ctrl+E：朗讀選取文字"),
        new("MidSplitterPos", "200", "主視窗原文/譯文各佔約一半"),
        new("ShowMiddlePane", "false", "隱藏語言選擇工具列"),
        new("ShowServicesPane", "false", "隱藏底部服務圖示列", Section: "General"),
    };

    readonly Action<string> _log;

    public OptionsPatcher(Action<string> log) => _log = log;

    public void Apply(string optionsPath, Mode mode)
    {
        _log("套用設定…");

        var existing = File.Exists(optionsPath) ? File.ReadAllText(optionsPath) : null;

        if (existing is not null)
        {
            BackUp(optionsPath);
        }

        if (existing is null || mode == Mode.Replace)
        {
            WriteTemplate(optionsPath, existing);
            return;
        }

        Merge(optionsPath, existing);
    }

    void WriteTemplate(string optionsPath, string existing)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(optionsPath));

        using var stream = Assembly.GetExecutingAssembly().GetManifestResourceStream("Options.default.json")
                           ?? throw new InvalidOperationException("設定範本沒有正確嵌入這個執行檔。");
        using var reader = new StreamReader(stream, Encoding.UTF8);
        var template = reader.ReadToEnd();

        // The OCR key is the one thing worth rescuing from whatever was here
        // before: it is personal, and it is the only setting the user has to
        // type in by hand after installing.
        var key = existing is null ? null : ValueOf(existing, OcrKeyName);
        if (!string.IsNullOrEmpty(key))
        {
            var slice = template;
            if (TrySetValue(ref slice, OcrKeyName, "\"" + key + "\""))
            {
                template = slice;
                _log("  保留這台電腦原有的 OCR API 金鑰");
            }
        }

        File.WriteAllText(optionsPath, template, new UTF8Encoding(true));

        _log(existing is null
            ? "  這台電腦還沒有設定檔，已寫入完整設定"
            : "  已用完整設定覆蓋這台電腦原本的設定");

        foreach (var setting in Settings)
        {
            if (setting.Key == OcrKeyName && !string.IsNullOrEmpty(key))
            {
                continue;
            }
            _log("  " + setting.Description);
        }
    }

    void Merge(string optionsPath, string json)
    {
        var missing = new List<string>();

        foreach (var setting in Settings)
        {
            if (setting.KeepExisting && !string.IsNullOrEmpty(ValueOf(json, setting.Key)))
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

    void BackUp(string optionsPath)
    {
        var backup = optionsPath + ".before-fix";
        if (File.Exists(backup))
        {
            return;
        }

        File.Copy(optionsPath, backup, overwrite: false);
        _log("  已備份原設定為 Options.json.before-fix");
    }

    /// <summary>The string value of a key, or null when absent or not a string.</summary>
    static string ValueOf(string json, string key)
    {
        var match = new Regex("\"" + Regex.Escape(key) + "\"\\s*:\\s*\"((?:[^\"\\\\]|\\\\.)*)\"").Match(json);
        return match.Success ? match.Groups[1].Value : null;
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
