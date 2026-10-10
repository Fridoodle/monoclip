using System.Text;
namespace MonoClip.Core;

public static class CommandLine
{
    // Windows command-line quoting (CommandLineToArgvW rules): backslashes before a quote are doubled.
    public static string Quote(string arg)
    {
        if (arg.Length > 0 && arg.IndexOfAny([' ', '\t', '\n', '\v', '"']) < 0) return arg;
        var quoted = new StringBuilder("\"");
        for (int i = 0; i < arg.Length; i++)
        {
            int slashes = 0; while (i < arg.Length && arg[i] == '\\') { slashes++; i++; }
            if (i == arg.Length) { quoted.Append('\\', slashes * 2); break; }
            if (arg[i] == '"') quoted.Append('\\', slashes * 2 + 1).Append('"'); else quoted.Append('\\', slashes).Append(arg[i]);
        }
        return quoted.Append('"').ToString();
    }
    public static string Build(string program, IEnumerable<string> args) => string.Join(' ', args.Prepend(program).Select(Quote));
}
