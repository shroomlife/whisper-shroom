using System.Runtime.InteropServices;

namespace WhisperShroom.Helpers;

/// <summary>
/// Thin P/Invoke wrapper for Win32 popup menus.
/// Used instead of CsWin32 because its SafeHandle wrappers don't work well with HMENU.
/// </summary>
internal static partial class NativeMenu
{
    internal const uint MF_STRING = 0x0000;
    internal const uint MF_SEPARATOR = 0x0800;
    internal const uint MF_POPUP = 0x0010;
    internal const uint MF_CHECKED = 0x0008;
    internal const uint MF_GRAYED = 0x0001;

    internal const uint TPM_RETURNCMD = 0x0100;
    internal const uint TPM_BOTTOMALIGN = 0x0020;

    [LibraryImport("user32.dll")]
    internal static partial nint CreatePopupMenu();

    [LibraryImport("user32.dll", StringMarshalling = StringMarshalling.Utf16)]
    [return: MarshalAs(UnmanagedType.Bool)]
    internal static partial bool AppendMenuW(nint hMenu, uint uFlags, nuint uIDNewItem, string? lpNewItem);

    [LibraryImport("user32.dll")]
    internal static partial int TrackPopupMenuEx(nint hMenu, uint uFlags, int x, int y, nint hWnd, nint lptpm);

    [LibraryImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    internal static partial bool DestroyMenu(nint hMenu);

    [LibraryImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    internal static partial bool SetForegroundWindow(nint hWnd);

    [LibraryImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    internal static partial bool GetCursorPos(out POINT lpPoint);

    [StructLayout(LayoutKind.Sequential)]
    internal struct POINT
    {
        public int X;
        public int Y;
    }
}
