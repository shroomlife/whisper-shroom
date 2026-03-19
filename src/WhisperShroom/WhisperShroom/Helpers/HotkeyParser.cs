using Windows.System;

namespace WhisperShroom.Helpers;

public static class HotkeyParser
{
    // Win32 hotkey modifier constants
    public const uint MOD_ALT = 0x0001;
    public const uint MOD_CONTROL = 0x0002;
    public const uint MOD_SHIFT = 0x0004;
    public const uint MOD_WIN = 0x0008;
    public const uint MOD_NOREPEAT = 0x4000;

    private static readonly Dictionary<string, uint> ModMap = new(StringComparer.OrdinalIgnoreCase)
    {
        ["ctrl"] = MOD_CONTROL,
        ["control"] = MOD_CONTROL,
        ["alt"] = MOD_ALT,
        ["menu"] = MOD_ALT,
        ["shift"] = MOD_SHIFT,
        ["win"] = MOD_WIN,
        ["windows"] = MOD_WIN,
    };

    private static readonly Dictionary<string, uint> VkMap = new(StringComparer.OrdinalIgnoreCase)
    {
        ["space"] = 0x20, ["enter"] = 0x0D, ["return"] = 0x0D, ["tab"] = 0x09,
        ["escape"] = 0x1B, ["esc"] = 0x1B, ["backspace"] = 0x08,
        ["delete"] = 0x2E, ["insert"] = 0x2D, ["home"] = 0x24, ["end"] = 0x23,
        ["pageup"] = 0x21, ["pagedown"] = 0x22,
        ["up"] = 0x26, ["down"] = 0x28, ["left"] = 0x25, ["right"] = 0x27,
        ["f1"] = 0x70, ["f2"] = 0x71, ["f3"] = 0x72, ["f4"] = 0x73,
        ["f5"] = 0x74, ["f6"] = 0x75, ["f7"] = 0x76, ["f8"] = 0x77,
        ["f9"] = 0x78, ["f10"] = 0x79, ["f11"] = 0x7A, ["f12"] = 0x7B,
        ["numpad0"] = 0x60, ["numpad1"] = 0x61, ["numpad2"] = 0x62, ["numpad3"] = 0x63,
        ["numpad4"] = 0x64, ["numpad5"] = 0x65, ["numpad6"] = 0x66, ["numpad7"] = 0x67,
        ["numpad8"] = 0x68, ["numpad9"] = 0x69,
    };

    private static readonly Dictionary<VirtualKey, string> VkToName = new()
    {
        [VirtualKey.Space] = "space", [VirtualKey.Enter] = "enter", [VirtualKey.Tab] = "tab",
        [VirtualKey.Escape] = "escape", [VirtualKey.Back] = "backspace",
        [VirtualKey.Delete] = "delete", [VirtualKey.Insert] = "insert",
        [VirtualKey.Home] = "home", [VirtualKey.End] = "end",
        [VirtualKey.PageUp] = "pageup", [VirtualKey.PageDown] = "pagedown",
        [VirtualKey.Up] = "up", [VirtualKey.Down] = "down",
        [VirtualKey.Left] = "left", [VirtualKey.Right] = "right",
        [VirtualKey.F1] = "f1", [VirtualKey.F2] = "f2", [VirtualKey.F3] = "f3",
        [VirtualKey.F4] = "f4", [VirtualKey.F5] = "f5", [VirtualKey.F6] = "f6",
        [VirtualKey.F7] = "f7", [VirtualKey.F8] = "f8", [VirtualKey.F9] = "f9",
        [VirtualKey.F10] = "f10", [VirtualKey.F11] = "f11", [VirtualKey.F12] = "f12",
        [VirtualKey.NumberPad0] = "numpad0", [VirtualKey.NumberPad1] = "numpad1",
        [VirtualKey.NumberPad2] = "numpad2", [VirtualKey.NumberPad3] = "numpad3",
        [VirtualKey.NumberPad4] = "numpad4", [VirtualKey.NumberPad5] = "numpad5",
        [VirtualKey.NumberPad6] = "numpad6", [VirtualKey.NumberPad7] = "numpad7",
        [VirtualKey.NumberPad8] = "numpad8", [VirtualKey.NumberPad9] = "numpad9",
    };

    /// <summary>
    /// Formats a hotkey from VirtualKey + modifier flags into the config string format (e.g. "ctrl+shift+e").
    /// Returns null if the key is not a valid hotkey target.
    /// </summary>
    public static string? Format(VirtualKey key, bool ctrl, bool shift, bool alt, bool win)
    {
        // Must have at least one modifier
        if (!ctrl && !shift && !alt && !win)
            return null;

        // Determine key name
        string? keyName = null;
        if (VkToName.TryGetValue(key, out var name))
            keyName = name;
        else if (key >= VirtualKey.A && key <= VirtualKey.Z)
            keyName = ((char)('a' + (key - VirtualKey.A))).ToString();
        else if (key >= VirtualKey.Number0 && key <= VirtualKey.Number9)
            keyName = ((char)('0' + (key - VirtualKey.Number0))).ToString();

        if (keyName is null)
            return null;

        var parts = new List<string>(4);
        if (ctrl) parts.Add("ctrl");
        if (alt) parts.Add("alt");
        if (shift) parts.Add("shift");
        if (win) parts.Add("win");
        parts.Add(keyName);

        return string.Join('+', parts);
    }

    public static bool IsModifier(VirtualKey key) =>
        key is VirtualKey.Control or VirtualKey.LeftControl or VirtualKey.RightControl
            or VirtualKey.Shift or VirtualKey.LeftShift or VirtualKey.RightShift
            or VirtualKey.Menu or VirtualKey.LeftMenu or VirtualKey.RightMenu
            or VirtualKey.LeftWindows or VirtualKey.RightWindows;

    public static (uint Modifiers, uint VkCode) Parse(string hotkeyStr)
    {
        var parts = hotkeyStr.Split('+', StringSplitOptions.TrimEntries);
        uint modifiers = MOD_NOREPEAT;
        uint vkCode = 0;

        foreach (var part in parts)
        {
            var key = part.ToLowerInvariant();

            if (ModMap.TryGetValue(key, out var mod))
            {
                modifiers |= mod;
            }
            else if (VkMap.TryGetValue(key, out var vk))
            {
                vkCode = vk;
            }
            else if (key.Length == 1 && char.IsLetterOrDigit(key[0]))
            {
                vkCode = (uint)char.ToUpper(key[0]);
            }
            else
            {
                throw new ArgumentException($"Unbekannte Taste: '{part}'");
            }
        }

        if (vkCode == 0)
            throw new ArgumentException("Keine Taste angegeben (nur Modifier)");

        return (modifiers, vkCode);
    }
}
