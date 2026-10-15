using MonoClip.Windows.UI;
using static UiTest;

internal static class HotkeyTests
{
    public static void Run()
    {
        Test("Hotkey parses modifiers and F8 without repeat", ParsesModifiersAndKey);
        Test("Hotkey rejects modifier-only keys and ambiguous numeric keys", RejectsInvalidText);
        Test("Global hotkey conflict preserves original registration", ConflictKeepsOriginal);
    }

    static void ParsesModifiersAndKey()
    {
        var value = Hotkey.Parse("Ctrl+Shift+F8");
        Equal(0x4006u, value.Modifiers);
        Equal(Keys.F8, value.Key);
        Equal("Ctrl+Shift+F8", value.ToString());
    }

    static void RejectsInvalidText()
    {
        foreach (var text in new[] { "", "Ctrl", "Ctrl+Shift", "Ctrl+17", "Ctrl+NoSuchKey", "Ctrl+F8+", "Ctrl+Ctrl+F8" })
        {
            try { Hotkey.Parse(text); }
            catch (ArgumentException) { continue; }
            throw new Exception("Accepted invalid hotkey: " + text);
        }
    }

    static void ConflictKeepsOriginal()
    {
        using var first = new HotkeyRegistration(() => { });
        using var second = new HotkeyRegistration(() => { });
        var a = Hotkey.Parse("Ctrl+Alt+Shift+F11");
        var b = Hotkey.Parse("Ctrl+Alt+Shift+F12");
        first.Set(a);
        second.Set(b);
        try
        {
            first.Set(b);
            throw new Exception("Conflicting registration accepted");
        }
        catch (InvalidOperationException) { }
        Equal<Hotkey?>(a, first.Current);
        try
        {
            second.Set(a);
            throw new Exception("Original registration was lost");
        }
        catch (InvalidOperationException) { }
    }
}
