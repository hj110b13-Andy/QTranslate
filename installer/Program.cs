namespace QTranslateFix;

static class Program
{
    [STAThread]
    static int Main(string[] args)
    {
        // This mode is how the unelevated main process hands off just the
        // steps that genuinely need administrator rights - copying into
        // Program Files and writing HKLM - to a short-lived elevated child,
        // instead of running the whole installer elevated. See app.manifest
        // for why that split matters.
        if (args.Length >= 3 && args[0].Equals("/elevated-move", StringComparison.OrdinalIgnoreCase))
        {
            return RunElevatedMove(stagingFolder: args[1], targetFolder: args[2]);
        }

        if (args.Length >= 2 && args[0].Equals("/elevated-remove", StringComparison.OrdinalIgnoreCase))
        {
            return RunElevatedRemove(targetFolder: args[1]);
        }

        ApplicationConfiguration.Initialize();
        var uninstallMode = args.Any(a => a.Equals("/uninstall", StringComparison.OrdinalIgnoreCase));
        Application.Run(new MainForm(uninstallMode));
        return 0;
    }

    static int RunElevatedMove(string stagingFolder, string targetFolder)
    {
        try
        {
            new Deployer(_ => { }).ElevatedMove(stagingFolder, targetFolder);
            return 0;
        }
        catch (Exception ex)
        {
            WriteCrashLog("elevated-move", ex);
            return 1;
        }
    }

    static int RunElevatedRemove(string targetFolder)
    {
        try
        {
            new Deployer(_ => { }).ElevatedRemove(targetFolder);
            return 0;
        }
        catch (Exception ex)
        {
            WriteCrashLog("elevated-remove", ex);
            return 1;
        }
    }

    static void WriteCrashLog(string phase, Exception ex)
    {
        try
        {
            var path = Path.Combine(Path.GetTempPath(), $"qtsetup-{phase}-error.txt");
            File.WriteAllText(path, ex.ToString());
        }
        catch
        {
            // If we can't even write the crash log, the exit code alone will
            // have to tell the parent process something went wrong.
        }
    }
}
