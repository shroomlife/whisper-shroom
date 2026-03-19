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
