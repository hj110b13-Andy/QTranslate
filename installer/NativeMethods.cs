using System.Runtime.InteropServices;

namespace QTranslateFix;

static class NativeMethods
{
    [DllImport("kernel32.dll", EntryPoint = "MoveFileExW", CharSet = CharSet.Unicode, SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    internal static extern bool MoveFileEx(string existingFileName, string newFileName, uint flags);
}
