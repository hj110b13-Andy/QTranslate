namespace QTranslateFix;

static class Program
{
    [STAThread]
    static void Main(string[] args)
    {
        ApplicationConfiguration.Initialize();
        var uninstallMode = args.Any(a => a.Equals("/uninstall", StringComparison.OrdinalIgnoreCase));
        Application.Run(new MainForm(uninstallMode));
    }
}
