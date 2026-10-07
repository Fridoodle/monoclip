namespace MonoClip.Windows.UI;

public readonly record struct Hotkey(uint Modifiers, Keys Key)
{
    public const uint NoRepeat = 0x4000;

    public static Hotkey Parse(string text)
    {
        if (string.IsNullOrWhiteSpace(text)) throw new ArgumentException("Bitte eine Tastenkombination festlegen.");
        uint modifiers = NoRepeat;
        var tokens = text.Split('+', StringSplitOptions.TrimEntries);
        foreach (var token in tokens[..^1])
        {
            var modifier = token.ToUpperInvariant() switch
            {
                "CTRL" or "CONTROL" => 2u,
                "SHIFT" => 4u,
                "ALT" => 1u,
                "WIN" or "WINDOWS" => 8u,
                _ => throw new ArgumentException("Ungültige Tastenkombination.")
            };
            if ((modifiers & modifier) != 0) throw new ArgumentException("Modifikator doppelt angegeben.");
            modifiers |= modifier;
        }
        var name = tokens[^1];
        if (name.Length == 1 && char.IsDigit(name[0])) name = "D" + name;
        if (name.Length == 0 || char.IsDigit(name[0]) || !Enum.TryParse<Keys>(name, true, out var key) ||
            !Enum.IsDefined(key) || (int)key <= 0 || (int)key > 254 ||
            key is Keys.ShiftKey or Keys.ControlKey or Keys.Menu or Keys.LWin or Keys.RWin or
                Keys.LShiftKey or Keys.RShiftKey or Keys.LControlKey or Keys.RControlKey or Keys.LMenu or Keys.RMenu)
            throw new ArgumentException("Bitte eine Taste zusammen mit optionalen Modifikatoren drücken.");
        return new Hotkey(modifiers, key);
    }

    public override string ToString() =>
        ((Modifiers & 2) != 0 ? "Ctrl+" : "") +
        ((Modifiers & 1) != 0 ? "Alt+" : "") +
        ((Modifiers & 4) != 0 ? "Shift+" : "") +
        ((Modifiers & 8) != 0 ? "Win+" : "") + Key;
}
