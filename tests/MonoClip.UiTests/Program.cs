internal static class Program
{
    [STAThread]
    private static int Main(string[] args)
    {
        // Opt-in modes that render or probe instead of running the suite.
        if (args.Contains("--dpi")) return DpiProbe.Run(ArgAfter(args, "--dpi"));
        if (args.Contains("--popup")) return OptInModes.PopupRender(ArgAfter(args, "--popup"));
        if (args.Contains("--editor")) return OptInModes.EditorProbe(ArgAfter(args, "--editor"), args.Contains("--interactive"));

        HotkeyTests.Run();
        AutostartTests.Run();
        SettingsFormTests.Run();
        SettingsPopupTests.Run();
        TrayTests.Run();
        TraySharingTests.Run();
        SoundAndIconTests.Run();
        ExportEditorTests.Run();
        // Opt-in tests (--reveal, --render <dir>, --play-sounds) count towards the failures below.
        OptInModes.Run(args);

        Console.WriteLine($"UI tests: {UiTest.Failures} failure(s)");
        return UiTest.Failures == 0 ? 0 : 1;
    }

    internal static string ArgAfter(string[] args, string flag) => args[Array.IndexOf(args, flag) + 1];
}
