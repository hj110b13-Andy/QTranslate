using Microsoft.Win32;

namespace QTranslateFix;

/// <summary>Finds where QTranslate is installed on this machine.</summary>
static class QTranslateLocator
{
    const string ExeName = "QTranslate.exe";

    static readonly string[] RegistryKeys =
    {
        @"SOFTWARE\WOW6432Node\Microsoft\Windows\CurrentVersion\Uninstall\QTranslate",
        @"SOFTWARE\Microsoft\Windows\CurrentVersion\Uninstall\QTranslate",
    };

    public static string Find()
    {
        // An explicit override wins - portable copies live anywhere.
        var overridden = Environment.GetEnvironmentVariable("QTRANSLATE_DIR");
        if (IsInstallFolder(overridden))
        {
            return overridden;
        }

        foreach (var key in RegistryKeys)
        {
            var folder = FromRegistry(key);
            if (IsInstallFolder(folder))
            {
                return folder;
            }
        }

        foreach (var root in new[]
                 {
                     Environment.GetFolderPath(Environment.SpecialFolder.ProgramFilesX86),
                     Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles),
                 })
        {
            if (string.IsNullOrEmpty(root))
            {
                continue;
            }
            var folder = Path.Combine(root, "QTranslate");
            if (IsInstallFolder(folder))
            {
                return folder;
            }
        }

        return null;
    }

    static string FromRegistry(string keyPath)
    {
        try
        {
            using var key = Registry.LocalMachine.OpenSubKey(keyPath);
            if (key is null)
            {
                return null;
            }

            if (key.GetValue("InstallLocation") is string location && !string.IsNullOrWhiteSpace(location))
            {
                return location.Trim().Trim('"');
            }

            // QTranslate leaves InstallLocation empty, but the uninstaller sits
            // in the install folder, so its directory is the answer.
            if (key.GetValue("UninstallString") is string uninstall && !string.IsNullOrWhiteSpace(uninstall))
            {
                return Path.GetDirectoryName(uninstall.Trim().Trim('"'));
            }
        }
        catch
        {
            // An unreadable registry key just means we fall through to the
            // well-known paths.
        }

        return null;
    }

    static bool IsInstallFolder(string folder) =>
        !string.IsNullOrWhiteSpace(folder) && File.Exists(Path.Combine(folder, ExeName));

    public static string ExePath(string installFolder) => Path.Combine(installFolder, ExeName);

    public static string ServiceFolder(string installFolder) =>
        Path.Combine(installFolder, "Services", "Google Translate");

    public static string OptionsPath() => Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "QTranslate", "Options.json");
}
