// Hand-written argument parsing (no System.CommandLine), in the style of sfmodcheck: options are "--name value" or
// "--name" flags, a value may not start with "--" and may not be empty, and every error names the option, gives an
// example and ends with the usage text.
namespace SfModBuild;

/// <summary>What a command accepts: options that take a value, and flags that don't.</summary>
internal sealed record ArgSpec(IReadOnlyCollection<string> ValueOptions, IReadOnlyCollection<string> Flags, int MaxPositionals = 0);

/// <summary>The parsed command line of one command.</summary>
internal sealed class ParsedArgs
{
    public Dictionary<string, string> Values { get; } = new(StringComparer.Ordinal);
    public HashSet<string> Flags { get; } = new(StringComparer.Ordinal);
    public List<string> Positionals { get; } = new();

    public string? Get(string option) => Values.TryGetValue(option, out var v) ? v : null;
    public bool Has(string flag) => Flags.Contains(flag);
}

internal static class Args
{
    /// <summary>Options every command takes.</summary>
    public static readonly string[] CommonValues = { "--log" };
    public static readonly string[] CommonFlags = { "--verbose", "--help" };

    /// <summary>Parses the arguments after the command name. Returns null and sets error (a message naming the option
    /// and the fix) when they don't fit the spec.</summary>
    public static ParsedArgs? Parse(string[] a, ArgSpec spec, out string? error)
    {
        error = null;
        var p = new ParsedArgs();
        var values = new HashSet<string>(spec.ValueOptions.Concat(CommonValues), StringComparer.Ordinal);
        var flags = new HashSet<string>(spec.Flags.Concat(CommonFlags), StringComparer.Ordinal);
        for (var i = 0; i < a.Length; i++)
        {
            var arg = a[i];
            if (arg.StartsWith("--", StringComparison.Ordinal))
            {
                // --name=value is accepted too
                var name = arg;
                string? inline = null;
                var eq = arg.IndexOf('=');
                if (eq > 2) { name = arg[..eq]; inline = arg[(eq + 1)..]; }
                if (values.Contains(name))
                {
                    string? v;
                    if (inline != null) v = inline.Trim().Length > 0 ? inline : null;
                    else if (i + 1 < a.Length && !a[i + 1].StartsWith("--", StringComparison.Ordinal) && a[i + 1].Trim().Length > 0) v = a[++i];
                    else v = null;
                    if (v == null)
                    {
                        var empty = inline != null || (i + 1 < a.Length && a[i + 1].Trim().Length == 0);
                        error = $"{name} needs {What(name)} after it{(empty ? " (it was given an empty value)" : "")}, for example: {name} {Example(name)}";
                        return null;
                    }
                    if (p.Values.ContainsKey(name))
                    {
                        error = $"{name} was given twice. Give it once.";
                        return null;
                    }
                    p.Values[name] = v;
                }
                else if (flags.Contains(name) && inline == null) p.Flags.Add(name);
                else if (flags.Contains(name))
                {
                    error = $"{name} doesn't take a value. Write it alone: {name}";
                    return null;
                }
                else
                {
                    error = $"unknown option {name}." + Suggest(name, values.Concat(flags));
                    return null;
                }
            }
            else
            {
                if (p.Positionals.Count >= spec.MaxPositionals)
                {
                    error = spec.MaxPositionals == 0
                        ? $"unexpected argument \"{arg}\": this command takes only options. If it is a path with spaces, put it in quotes after its option."
                        : $"unexpected extra argument \"{arg}\". If it is a path with spaces, put it in quotes.";
                    return null;
                }
                p.Positionals.Add(arg);
            }
        }
        return p;
    }

    static string Suggest(string name, IEnumerable<string> known)
    {
        var best = known.Select(k => (k, d: Distance(name, k))).OrderBy(x => x.d).FirstOrDefault();
        return best.k != null && best.d <= 2 ? $" Did you mean {best.k}?" : " Run with --help to see the options.";
    }

    internal static int Distance(string s, string t)
    {
        var d = new int[s.Length + 1, t.Length + 1];
        for (var i = 0; i <= s.Length; i++) d[i, 0] = i;
        for (var j = 0; j <= t.Length; j++) d[0, j] = j;
        for (var i = 1; i <= s.Length; i++)
            for (var j = 1; j <= t.Length; j++)
                d[i, j] = Math.Min(Math.Min(d[i - 1, j] + 1, d[i, j - 1] + 1), d[i - 1, j - 1] + (s[i - 1] == t[j - 1] ? 0 : 1));
        return d[s.Length, t.Length];
    }

    static string What(string option) => option switch
    {
        "--game" => "the game folder",
        "--checker" => "the checker program",
        "--my-games" => "the folder that holds StarfieldCustom.ini",
        "--log" => "a log file name",
        _ => "a value",
    };

    /// <summary>An example value for an option, for the error messages.</summary>
    public static string Example(string option) => option switch
    {
        "--game" => "\"C:\\Program Files (x86)\\Steam\\steamapps\\common\\Starfield\"",
        "--checker" => "\"C:\\Tools\\sf-mod-check.exe\"",
        "--my-games" => "\"C:\\Users\\you\\Documents\\My Games\\Starfield\"",
        "--log" => "sf-mod-build-log.txt",
        _ => "<value>",
    };
}
