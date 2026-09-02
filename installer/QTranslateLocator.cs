using Microsoft.Win32;

namespace QTranslateFix;

/// <summary>Finds where QTranslate is installed on this machine.</summary>
static class QTranslateLocator
{
    const string ExeName = "QTranslate.exe";
    const string OwnUninstallKey = @"SOFTWARE\Microsoft\Windows\CurrentVersion\Uninstall\QTranslate-Fixed";

    // The stock installer only ever registers per-machine (HKLM). Our own
    // installer registers per-user (HKCU) - see the remarks on Deployer.
    static readonly string[] StockRegistryKeys =
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

        var own = FromRegistry(Registry.CurrentUser, OwnUninstallKey);
        if (IsInstallFolder(own))
        {
            return own;
        }

        foreach (var key in StockRegistryKeys)
        {
            var folder = FromRegistry(Registry.LocalMachine, key);
            if (IsInstallFolder(folder))
            {
                return folder;
            }
        }

        foreach (var root in new[]
                 {
                     Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
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

    /// <summary>
    /// Where a new install should default to. %LocalAppData% needs no
    /// administrator rights at all, unlike Program Files - see the remarks
    /// on Deployer for why that matters. Program Files is still available by
    /// browsing to it manually.
    /// </summary>
    public static string DefaultInstallFolder() =>
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "QTranslate");

    static string FromRegistry(RegistryKey hive, string keyPath)
    {
        try
        {
            using var key = hive.OpenSubKey(keyPath);
            if (key is null)
            {
                return null;
            }

            if (key.GetValue("InstallLocation") is string location && !string.IsNullOrWhiteSpace(location))
            {
                return location.Trim().Trim('"');
            }

            // A fallback in case InstallLocation is ever missing: the
            // uninstaller sits in the install folder, so its directory is
            // the answer too.
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
