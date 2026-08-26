using System.Reflection;
using System.Runtime.InteropServices;

namespace QTranslateFix;

/// <summary>
/// Creates .lnk files through the Windows Script Host shell object. Late
/// binding avoids pulling in a COM interop assembly for a handful of calls.
/// </summary>
static class Shortcut
{
    public static void Create(string linkPath, string target, string arguments, string workingDirectory,
        string description)
    {
        var shellType = Type.GetTypeFromProgID("WScript.Shell")
                        ?? throw new InvalidOperationException("這台電腦無法建立捷徑（找不到 WScript.Shell）。");
        var shell = Activator.CreateInstance(shellType);

        try
        {
            var link = shellType.InvokeMember("CreateShortcut", BindingFlags.InvokeMethod, null, shell,
                new object[] { linkPath });
            var linkType = link.GetType();

            Set(linkType, link, "TargetPath", target);
            Set(linkType, link, "Arguments", arguments ?? string.Empty);
            Set(linkType, link, "WorkingDirectory", workingDirectory);
            Set(linkType, link, "Description", description);
            Set(linkType, link, "IconLocation", target + ",0");

            linkType.InvokeMember("Save", BindingFlags.InvokeMethod, null, link, null);
        }
        finally
        {
            if (shell is not null && Marshal.IsComObject(shell))
            {
                Marshal.ReleaseComObject(shell);
            }
        }
    }

    static void Set(Type type, object instance, string property, string value) =>
        type.InvokeMember(property, BindingFlags.SetProperty, null, instance, new object[] { value });
}
