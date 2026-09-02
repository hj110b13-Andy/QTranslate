using System.ComponentModel;
using System.Diagnostics;
using System.IO.Compression;
using System.Linq;
using System.Reflection;
using Microsoft.Win32;

namespace QTranslateFix;

/// <summary>
/// Unpacks QTranslate, wires up shortcuts, and takes it all away again.
///
/// This process never elevates itself as a whole (see app.manifest), and by
/// default it doesn't need to at all: the default install location is
/// %LocalAppData%, and this installer's own Apps-and-Features entry lives in
/// HKEY_CURRENT_USER, not HKEY_LOCAL_MACHINE - both writable by a completely
/// ordinary, unprivileged process. Only placing files under a location that
/// genuinely requires it (Program Files, if the user picks it deliberately)
/// relaunches this same exe elevated for just that one step, via
/// <see cref="ElevatedMove"/> / <see cref="ElevatedRemove"/>. Options.json,
/// the HKCU Run key, shortcuts, and this installer's own registration always
/// run directly in this unelevated process, so they always resolve the
/// profile of whoever is actually sitting at the keyboard.
///
/// That split exists because of a confirmed, documented Windows behavior:
/// when UAC elevation is satisfied with a *different* administrator account's
/// credentials (common when the daily account is a standard user), the whole
/// elevated process runs as that other account, and "current user" paths -
/// %APPDATA%, HKCU - resolve to *their* profile, not the one actually using
/// QTranslate. A version of this installer that required elevation for its
/// entire run silently wrote settings nobody could find afterwards. Defaulting
/// to a location and a registry hive that need no elevation at all removes
/// that failure mode by construction rather than working around it - the same
/// principle an unrelated, portable reimplementation (ahatem/QTranslate) uses
/// by keeping all of its own state next to its own executable.
/// </summary>
sealed class Deployer
{
    public const string DisplayName = "QTranslate 6.10.0 (修正版)";
    public const string Version = "6.10.8";

    const string ProcessName = "QTranslate";
    const string RunValueName = "QTranslate";
    const string SetupFileName = "QTranslate-Setup.exe";

    // Ours - HKCU, no elevation ever needed to read or write it.
    const string UninstallKey = @"SOFTWARE\Microsoft\Windows\CurrentVersion\Uninstall\QTranslate-Fixed";
    const string RunKey = @"SOFTWARE\Microsoft\Windows\CurrentVersion\Run";
    const int ErrorCancelled = 1223; // the user clicked "No" on the UAC prompt

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

        var staging = Path.Combine(Path.GetTempPath(), "qtsetup-" + Guid.NewGuid().ToString("N")[..8]);
        Directory.CreateDirectory(staging);
        try
        {
            Extract(staging);
            PlaceFiles(staging, targetFolder);
        }
        finally
        {
            TryDeleteDirectory(staging);
        }

        var exePath = QTranslateLocator.ExePath(targetFolder);
        if (!File.Exists(exePath))
        {
            throw new InvalidOperationException("安裝似乎沒有成功：找不到 " + exePath);
        }
        _log("已安裝程式檔案到");
        _log("  " + targetFolder);

        // Everything from here on is scoped to the current user and always
        // runs directly in this (unelevated) process - see the class remarks.
        // This includes our own Apps-and-Features registration: it used to
        // happen inside ElevatedMove, but that meant it ran as whichever
        // account UAC elevated to, which is the exact hazard this class
        // exists to avoid - it just showed up as a cosmetic Control Panel
        // entry instead of a missing setting, so it went unnoticed longer.
        RegisterUninstaller(targetFolder, exePath);

        if (options.SettingsMode is { } mode)
        {
            _log("");
            new OptionsPatcher(_log).Apply(QTranslateLocator.OptionsPath(targetFolder), mode);
        }

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

        _log("");
        _log("安裝完成。");
        Launch(exePath, wasRunning);
    }

    public void Uninstall(string targetFolder)
    {
        StopQTranslate();
        SetStartup(false, null);
        RemoveShortcuts();

        // Our own registration is HKCU - always unelevated, always in this
        // process, regardless of whether the folder itself needs elevation.
        try
        {
            Registry.CurrentUser.DeleteSubKeyTree(UninstallKey, throwOnMissingSubKey: false);
            _log("已移除「應用程式與功能」登錄項目");
        }
        catch (Exception ex)
        {
            _log("移除登錄項目時發生問題：" + ex.Message);
        }

        // The safety check - only ever remove a folder that really holds
        // QTranslate - just needs read access, so it happens here rather than
        // inside the possibly-elevated step, where a refusal would otherwise
        // go unlogged.
        if (Directory.Exists(targetFolder) && File.Exists(QTranslateLocator.ExePath(targetFolder)))
        {
            RemoveInstalledFiles(targetFolder);
        }
        else
        {
            _log("這個資料夾看起來不是 QTranslate，沒有刪除：" + targetFolder);
        }

        _log("");
        _log("解除安裝完成。個人設定與翻譯紀錄保留在：");
        _log("  " + Path.GetDirectoryName(QTranslateLocator.OptionsPath(targetFolder)));
    }

    /// <summary>
    /// Copies the staged payload into place and removes the stock installer's
    /// HKLM entry if one exists there - the only two things that can
    /// genuinely require administrator rights (Program Files, another
    /// program's HKLM key). Called either directly in this process (when the
    /// target turns out to be writable without elevation) or from the
    /// elevated child process spawned for "/elevated-move" - the logic is
    /// identical either way, only the process it runs in differs.
    /// </summary>
    public void ElevatedMove(string stagingFolder, string targetFolder)
    {
        CopyStagedFiles(stagingFolder, targetFolder);
        RemoveStockUninstaller(targetFolder);
        CopySelfInto(targetFolder);
    }

    /// <summary>
    /// Just the file placement, with no registry side effects - split out so
    /// tests can verify it without touching the real machine's HKLM.
    /// </summary>
    public static void CopyStagedFiles(string stagingFolder, string targetFolder)
    {
        Directory.CreateDirectory(targetFolder);
        foreach (var file in Directory.GetFiles(stagingFolder, "*", SearchOption.AllDirectories))
        {
            var relative = Path.GetRelativePath(stagingFolder, file);
            var destination = Path.Combine(targetFolder, relative);
            Directory.CreateDirectory(Path.GetDirectoryName(destination)!);
            File.Copy(file, destination, overwrite: true);
        }
    }

    /// <summary>
    /// The admin-only half of uninstalling: deleting the folder itself, needed
    /// only when it lives somewhere like Program Files. Our own HKCU
    /// registration is removed separately in the always-unelevated parent -
    /// see <see cref="Uninstall"/>.
    /// </summary>
    public void ElevatedRemove(string targetFolder) => DeleteFolder(targetFolder);

    void PlaceFiles(string stagingFolder, string targetFolder)
    {
        if (!NeedsElevation(targetFolder))
        {
            ElevatedMove(stagingFolder, targetFolder);
            return;
        }

        _log("寫入安裝位置需要系統管理員權限，請在接下來的視窗中確認…");
        RunElevatedHelper("/elevated-move", stagingFolder, targetFolder);
    }

    void RemoveInstalledFiles(string targetFolder)
    {
        if (!NeedsElevation(targetFolder))
        {
            ElevatedRemove(targetFolder);
            return;
        }

        _log("移除安裝位置需要系統管理員權限，請在接下來的視窗中確認…");
        RunElevatedHelper("/elevated-remove", targetFolder);
    }

    /// <summary>
    /// Whether writing into this folder needs elevation, tested by actually
    /// trying rather than guessing from the path - a portable install under
    /// the user's own profile needs no elevation at all, while the default
    /// Program Files location does.
    /// </summary>
    public static bool NeedsElevation(string targetFolder)
    {
        try
        {
            Directory.CreateDirectory(targetFolder);
            var probe = Path.Combine(targetFolder, ".qtsetup-write-probe");
            File.WriteAllText(probe, "probe");
            File.Delete(probe);
            return false;
        }
        catch (UnauthorizedAccessException)
        {
            return true;
        }
        catch (IOException)
        {
            return true;
        }
    }

    /// <summary>Relaunches this same exe elevated to run one narrow, privileged step.</summary>
    void RunElevatedHelper(string mode, params string[] args)
    {
        var self = Environment.ProcessPath;
        if (string.IsNullOrEmpty(self))
        {
            throw new InvalidOperationException("找不到目前執行檔的路徑，無法要求系統管理員權限。");
        }

        var startInfo = new ProcessStartInfo(self) { UseShellExecute = true, Verb = "runas" };
        startInfo.ArgumentList.Add(mode);
        foreach (var arg in args)
        {
            startInfo.ArgumentList.Add(arg);
        }

        Process process;
        try
        {
            process = Process.Start(startInfo) ?? throw new InvalidOperationException("無法啟動提權程序。");
        }
        catch (Win32Exception ex) when (ex.NativeErrorCode == ErrorCancelled)
        {
            throw new InvalidOperationException("已取消系統管理員授權，操作未完成。");
        }

        using (process)
        {
            process.WaitForExit();
            if (process.ExitCode != 0)
            {
                var detail = ReadAndClearCrashLog(mode);
                throw new InvalidOperationException(detail is null
                    ? "提權操作失敗（結束碼 " + process.ExitCode + "）。"
                    : "提權操作失敗：\n" + detail);
            }
        }

        _log("已取得系統管理員權限並完成該步驟。");
    }

    static string ReadAndClearCrashLog(string mode)
    {
        var phase = mode.TrimStart('/');
        var path = Path.Combine(Path.GetTempPath(), $"qtsetup-{phase}-error.txt");
        try
        {
            if (!File.Exists(path))
            {
                return null;
            }
            var text = File.ReadAllText(path);
            File.Delete(path);
            return text;
        }
        catch
        {
            return null;
        }
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

            Directory.CreateDirectory(Path.GetDirectoryName(destination)!);
            entry.ExtractToFile(destination, overwrite: true);
            written++;
        }

        _log($"已解壓縮 {written} 個檔案到暫存位置");
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

    /// <summary>
    /// Keeps a copy of this installer beside the program - that's what lets
    /// the Apps-and-Features entry launch it again to uninstall later. This
    /// is a file placed inside targetFolder, so - unlike the registry entry
    /// itself - it does need to run wherever ElevatedMove runs.
    /// </summary>
    void CopySelfInto(string targetFolder)
    {
        try
        {
            var self = Environment.ProcessPath;
            var setupPath = Path.Combine(targetFolder, SetupFileName);
            if (!string.IsNullOrEmpty(self) &&
                !string.Equals(Path.GetFullPath(self), Path.GetFullPath(setupPath), StringComparison.OrdinalIgnoreCase))
            {
                File.Copy(self, setupPath, overwrite: true);
            }
        }
        catch (Exception ex)
        {
            _log("複製安裝程式到安裝目錄時發生問題：" + ex.Message);
        }
    }

    /// <summary>
    /// Registers the Apps-and-Features entry under HKCU - always unelevated,
    /// regardless of where targetFolder itself needed elevation to write to.
    /// </summary>
    void RegisterUninstaller(string targetFolder, string exePath)
    {
        try
        {
            var setupPath = Path.Combine(targetFolder, SetupFileName);
            using var key = Registry.CurrentUser.CreateSubKey(UninstallKey, writable: true);
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
        // During an uninstall the copy of this installer inside the folder can
        // be the file currently running (when launched from its own install
        // location), so it cannot delete itself right away.
        //
        // Data\ holds the user's settings, translation history, and
        // dictionary lookups (see QTranslateLocator.OptionsPath) - it lives
        // inside the install folder, not the roaming profile, so deleting
        // the folder outright would destroy them. Keep it, both so an
        // uninstall doesn't throw away things the user never asked to lose,
        // and so reinstalling to the same folder picks the settings back up.
        var dataFolder = Path.Combine(folder, "Data") + Path.DirectorySeparatorChar;
        var deferred = new List<string>();

        foreach (var file in Directory.GetFiles(folder, "*", SearchOption.AllDirectories))
        {
            if (file.StartsWith(dataFolder, StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            try
            {
                File.Delete(file);
            }
            catch
            {
                deferred.Add(file);
            }
        }

        // Remove now-empty subfolders, deepest first, but never Data\ itself.
        foreach (var dir in Directory.GetDirectories(folder, "*", SearchOption.AllDirectories)
                     .OrderByDescending(d => d.Length))
        {
            var withSeparator = dir + Path.DirectorySeparatorChar;
            if (withSeparator.Equals(dataFolder, StringComparison.OrdinalIgnoreCase) ||
                dataFolder.StartsWith(withSeparator, StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            try
            {
                Directory.Delete(dir);
            }
            catch
            {
                // Not empty (deferred files still inside) - leave it.
            }
        }

        if (deferred.Count == 0)
        {
            _log("已刪除安裝資料夾（保留 Data\\ 底下的個人設定與翻譯紀錄）");
            return;
        }

        _log("已刪除程式檔案。下列檔案正在使用中，重新開機後會自動清除：");
        foreach (var file in deferred)
        {
            _log("  " + Path.GetFileName(file));
            ScheduleDeleteOnReboot(file);
        }
    }

    static void ScheduleDeleteOnReboot(string path)
    {
        const int MoveFileDelayUntilReboot = 0x4;
        _ = NativeMethods.MoveFileEx(path, null, MoveFileDelayUntilReboot);
    }

    static void TryDeleteDirectory(string path)
    {
        try
        {
            if (Directory.Exists(path))
            {
                Directory.Delete(path, recursive: true);
            }
        }
        catch
        {
            // Leftover staging files in %TEMP% are harmless.
        }
    }

    void Launch(string exePath, bool wasRunning)
    {
        try
        {
            // This process is not elevated (see the class remarks), so
            // starting QTranslate directly launches it as the same user -
            // no explorer.exe relaunch trick needed to shed elevation.
            Process.Start(new ProcessStartInfo(exePath) { UseShellExecute = true });
            _log(wasRunning ? "QTranslate 已重新啟動。" : "QTranslate 已啟動。");
        }
        catch (Exception ex)
        {
            _log($"無法自動啟動 QTranslate（{ex.Message}），請手動開啟。");
        }
    }
}
