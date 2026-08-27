using System.Diagnostics;
using System.IO.Compression;
using System.Reflection;
using Microsoft.Win32;

namespace QTranslateFix;

/// <summary>Unpacks QTranslate, wires up shortcuts, and takes it all away again.</summary>
sealed class Deployer
{
    public const string DisplayName = "QTranslate 6.10.0 (修正版)";
    public const string Version = "6.10.3";

    const string ProcessName = "QTranslate";
    const string RunValueName = "QTranslate";
    const string SetupFileName = "QTranslate-Setup.exe";
    const string UninstallKey = @"SOFTWARE\Microsoft\Windows\CurrentVersion\Uninstall\QTranslate-Fixed";
    const string RunKey = @"SOFTWARE\Microsoft\Windows\CurrentVersion\Run";

    // The stock QTranslate installer registers itself here. Left in place it
    // would show up as a second entry in Apps and Features alongside ours, and
    // running it would delete the folder out from under our own uninstaller.
    const string StockUninstallKey = @"SOFTWARE\Microsoft\Windows\CurrentVersion\Uninstall\QTranslate";
    const string StockUninstaller = "Uninstall.exe";

    readonly Action<string> _log;

    public Deployer(Action<string> log) => _log = log;

    /// <param name="SettingsMode">null leaves Options.json untouched.</param>
    public sealed record Options(
        bool DesktopShortcut,
        bool StartMenuShortcut,
        bool RunAtStartup,
        OptionsPatcher.Mode? SettingsMode);

    public void Install(string targetFolder, Options options)
    {
        var wasRunning = StopQTranslate();

        Directory.CreateDirectory(targetFolder);
        Extract(targetFolder);

        if (options.SettingsMode is { } mode)
        {
            _log("");
            new OptionsPatcher(_log).Apply(QTranslateLocator.OptionsPath(), mode);
        }

        var exePath = QTranslateLocator.ExePath(targetFolder);

        if (options.StartMenuShortcut)
        {
            var folder = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.Programs), "QTranslate");
            Directory.CreateDirectory(folder);
            Shortcut.Create(Path.Combine(folder, "QTranslate.lnk"), exePath, "", targetFolder, DisplayName);
            _log("已建立開始功能表捷徑");
        }

        if (options.DesktopShortcut)
        {
            var desktop = Environment.GetFolderPath(Environment.SpecialFolder.DesktopDirectory);
            Shortcut.Create(Path.Combine(desktop, "QTranslate.lnk"), exePath, "", targetFolder, DisplayName);
            _log("已建立桌面捷徑");
        }

        SetStartup(options.RunAtStartup, exePath);
        RemoveStockUninstaller(targetFolder);
        CopySelfAndRegister(targetFolder, exePath);

        _log("");
        _log("安裝完成。");
        Launch(exePath, wasRunning);
    }

    public void Uninstall(string targetFolder)
    {
        StopQTranslate();
        SetStartup(false, null);
        RemoveShortcuts();

        try
        {
            Registry.LocalMachine.DeleteSubKeyTree(UninstallKey, throwOnMissingSubKey: false);
        }
        catch (Exception ex)
        {
            _log("移除註冊表項目時發生問題：" + ex.Message);
        }

        // Only ever delete a folder that really holds QTranslate, never a parent.
        if (Directory.Exists(targetFolder) && File.Exists(QTranslateLocator.ExePath(targetFolder)))
        {
            DeleteFolder(targetFolder);
        }
        else
        {
            _log("這個資料夾看起來不是 QTranslate，沒有刪除：" + targetFolder);
        }

        _log("");
        _log("解除安裝完成。個人設定與翻譯紀錄保留在：");
        _log("  " + Path.GetDirectoryName(QTranslateLocator.OptionsPath()));
    }

    public void Extract(string targetFolder)
    {
        using var stream = Assembly.GetExecutingAssembly().GetManifestResourceStream("QTranslate.zip")
                           ?? throw new InvalidOperationException("安裝內容沒有正確嵌入這個執行檔。");
        using var archive = new ZipArchive(stream, ZipArchiveMode.Read);

        var root = Path.GetFullPath(targetFolder);
        if (!root.EndsWith(Path.DirectorySeparatorChar))
        {
            root += Path.DirectorySeparatorChar;
        }

        var written = 0;

        foreach (var entry in archive.Entries)
        {
            var destination = Path.GetFullPath(Path.Combine(root, entry.FullName));

            // Refuse anything that would escape the install folder.
            if (!destination.StartsWith(root, StringComparison.OrdinalIgnoreCase))
            {
                throw new InvalidOperationException("封裝內容含有非法路徑：" + entry.FullName);
            }

            if (string.IsNullOrEmpty(entry.Name))
            {
                Directory.CreateDirectory(destination);
                continue;
            }

            Directory.CreateDirectory(Path.GetDirectoryName(destination));
            entry.ExtractToFile(destination, overwrite: true);
            written++;
        }

        _log($"已解壓縮 {written} 個檔案到");
        _log("  " + targetFolder);
    }

    bool StopQTranslate()
    {
        var running = Process.GetProcessesByName(ProcessName);
        if (running.Length == 0)
        {
            return false;
        }

        foreach (var process in running)
        {
            try
            {
                process.Kill();
                process.WaitForExit(5000);
            }
            catch (Exception ex)
            {
                _log("關閉 QTranslate 時發生問題：" + ex.Message);
            }
            finally
            {
                process.Dispose();
            }
        }

        _log("已關閉執行中的 QTranslate");
        return true;
    }

    void RemoveShortcuts()
    {
        var startMenuFolder = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.Programs), "QTranslate");

        foreach (var link in new[]
                 {
                     Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.DesktopDirectory), "QTranslate.lnk"),
                     Path.Combine(startMenuFolder, "QTranslate.lnk"),
                 })
        {
            try
            {
                if (File.Exists(link))
                {
                    File.Delete(link);
                    _log("已移除捷徑：" + Path.GetFileName(link));
                }
            }
            catch (Exception ex)
            {
                _log("移除捷徑時發生問題：" + ex.Message);
            }
        }

        try
        {
            if (Directory.Exists(startMenuFolder) && Directory.GetFileSystemEntries(startMenuFolder).Length == 0)
            {
                Directory.Delete(startMenuFolder);
            }
        }
        catch
        {
            // A leftover empty folder is harmless.
        }
    }

    void SetStartup(bool enable, string exePath)
    {
        try
        {
            using var key = Registry.CurrentUser.CreateSubKey(RunKey, writable: true);
            if (key is null)
            {
                return;
            }

            if (enable)
            {
                key.SetValue(RunValueName, $"\"{exePath}\" /startup-minimized");
                _log("已設定開機自動啟動");
            }
            else if (key.GetValue(RunValueName) is not null)
            {
                key.DeleteValue(RunValueName, throwOnMissingValue: false);
                _log("已取消開機自動啟動");
            }
        }
        catch (Exception ex)
        {
            _log("設定開機啟動時發生問題：" + ex.Message);
        }
    }

    /// <summary>
    /// Takes over from the stock installer, so that "應用程式與功能" ends up with
    /// exactly one QTranslate entry - ours - and there is only one way to remove
    /// the program.
    /// </summary>
    void RemoveStockUninstaller(string targetFolder)
    {
        // The stock installer is 32-bit, so its key lives in the WOW6432Node
        // view; check both views rather than assuming.
        foreach (var view in new[] { RegistryView.Registry64, RegistryView.Registry32 })
        {
            try
            {
                using var root = RegistryKey.OpenBaseKey(RegistryHive.LocalMachine, view);
                using (var probe = root.OpenSubKey(StockUninstallKey))
                {
                    if (probe is null)
                    {
                        continue;
                    }
                    _log($"已移除原版的解除安裝項目：{probe.GetValue("DisplayName")}");
                }

                root.DeleteSubKeyTree(StockUninstallKey, throwOnMissingSubKey: false);
            }
            catch (Exception ex)
            {
                _log("移除原版解除安裝項目時發生問題：" + ex.Message);
            }
        }

        // Its uninstaller would still delete the folder if run directly, which
        // would leave our own entry pointing at a file that no longer exists.
        var stock = Path.Combine(targetFolder, StockUninstaller);
        try
        {
            if (File.Exists(stock))
            {
                File.Delete(stock);
                _log("已移除原版的 Uninstall.exe");
            }
        }
        catch (Exception ex)
        {
            _log("移除原版 Uninstall.exe 時發生問題：" + ex.Message);
        }
    }

    void CopySelfAndRegister(string targetFolder, string exePath)
    {
        var setupPath = Path.Combine(targetFolder, SetupFileName);

        try
        {
            // Keeping a copy of this installer beside the program is what makes
            // the entry in Apps and Features able to uninstall later.
            var self = Environment.ProcessPath;
            if (!string.IsNullOrEmpty(self) &&
                !string.Equals(Path.GetFullPath(self), Path.GetFullPath(setupPath), StringComparison.OrdinalIgnoreCase))
            {
                File.Copy(self, setupPath, overwrite: true);
            }

            using var key = Registry.LocalMachine.CreateSubKey(UninstallKey, writable: true);
            if (key is null)
            {
                return;
            }

            key.SetValue("DisplayName", DisplayName);
            key.SetValue("DisplayVersion", Version);
            key.SetValue("InstallLocation", targetFolder);
            key.SetValue("DisplayIcon", exePath);
            key.SetValue("Publisher", "QuestSoft");
            key.SetValue("UninstallString", $"\"{setupPath}\" /uninstall");
            key.SetValue("NoModify", 1, RegistryValueKind.DWord);
            key.SetValue("NoRepair", 1, RegistryValueKind.DWord);
            _log("已註冊到「應用程式與功能」");
        }
        catch (Exception ex)
        {
            _log("註冊解除安裝項目時發生問題：" + ex.Message);
        }
    }

    void DeleteFolder(string folder)
    {
        // During an uninstall the copy of this installer inside the folder is
        // the file currently running, so it cannot delete itself right away.
        var deferred = new List<string>();

        foreach (var file in Directory.GetFiles(folder, "*", SearchOption.AllDirectories))
        {
            try
            {
                File.Delete(file);
            }
            catch
            {
                deferred.Add(file);
            }
        }

        if (deferred.Count == 0)
        {
            Directory.Delete(folder, recursive: true);
            _log("已刪除安裝資料夾");
            return;
        }

        _log("已刪除程式檔案。下列檔案正在使用中，重新開機後會自動清除：");
        foreach (var file in deferred)
        {
            _log("  " + Path.GetFileName(file));
            ScheduleDeleteOnReboot(file);
        }

        ScheduleDeleteOnReboot(folder);
    }

    static void ScheduleDeleteOnReboot(string path)
    {
        const int MoveFileDelayUntilReboot = 0x4;
        _ = NativeMethods.MoveFileEx(path, null, MoveFileDelayUntilReboot);
    }

    void Launch(string exePath, bool wasRunning)
    {
        try
        {
            // Explorer starts it as the normal user, so QTranslate does not
            // inherit this installer's administrator rights.
            Process.Start(new ProcessStartInfo("explorer.exe", $"\"{exePath}\"") { UseShellExecute = true });
            _log(wasRunning ? "QTranslate 已重新啟動。" : "QTranslate 已啟動。");
        }
        catch (Exception ex)
        {
            _log($"無法自動啟動 QTranslate（{ex.Message}），請手動開啟。");
        }
    }
}
