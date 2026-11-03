using System.Runtime.InteropServices;

namespace WhisperShroom.Helpers;

internal static partial class ClipboardHelper
{
    private const uint CF_UNICODETEXT = 13;
    private const uint GMEM_MOVEABLE = 0x0002;

    [LibraryImport("user32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool OpenClipboard(nint hWndNewOwner);

    [LibraryImport("user32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool CloseClipboard();

    [LibraryImport("user32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool EmptyClipboard();

    [LibraryImport("user32.dll", SetLastError = true)]
    private static partial nint SetClipboardData(uint uFormat, nint hMem);

    [LibraryImport("kernel32.dll", SetLastError = true)]
    private static partial nint GlobalAlloc(uint uFlags, nuint dwBytes);

    [LibraryImport("kernel32.dll", SetLastError = true)]
    private static partial nint GlobalLock(nint hMem);

    [LibraryImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool GlobalUnlock(nint hMem);

    [LibraryImport("kernel32.dll", SetLastError = true)]
    private static partial nint GlobalFree(nint hMem);

    /// <summary>
    /// Places <paramref name="text"/> on the clipboard as CF_UNICODETEXT.
    /// Returns false if any Win32 step fails. Ownership of the global memory passes to the
    /// system only when SetClipboardData succeeds; on every failure path it is freed here.
    /// </summary>
    internal static bool CopyToClipboard(string text, nint hWnd)
    {
        if (!OpenClipboard(hWnd))
            return false;

        try
        {
            if (!EmptyClipboard())
                return false;

            var bytes = System.Text.Encoding.Unicode.GetBytes(text + "\0");
            var hGlobal = GlobalAlloc(GMEM_MOVEABLE, (nuint)bytes.Length);
            if (hGlobal == 0)
                return false;

            var ptr = GlobalLock(hGlobal);
            if (ptr == 0)
            {
                GlobalFree(hGlobal);
                return false;
            }

            Marshal.Copy(bytes, 0, ptr, bytes.Length);
            GlobalUnlock(hGlobal);

            if (SetClipboardData(CF_UNICODETEXT, hGlobal) == 0)
            {
                GlobalFree(hGlobal);
                return false;
            }

            return true;
        }
        finally
        {
            CloseClipboard();
        }
    }
}
