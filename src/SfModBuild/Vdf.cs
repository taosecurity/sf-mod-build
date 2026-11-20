// A small reader for Valve's KeyValues text format (libraryfolders.vdf), enough to list Steam's library folders.
//   "libraryfolders" { "0" { "path" "C:\\Program Files (x86)\\Steam" "apps" { "1716740" "123" } } "1" { ... } }
// Older Steam versions wrote "1" "D:\\SteamLibrary" directly under the root; both forms are read.
using System.Text;

namespace SfModBuild;

internal sealed class VdfNode
{
    public string? Value { get; init; }
    public List<KeyValuePair<string, VdfNode>> Children { get; } = new();

    public VdfNode? this[string key] =>
        Children.FirstOrDefault(c => string.Equals(c.Key, key, StringComparison.OrdinalIgnoreCase)).Value;
}

internal static class Vdf
{
    /// <summary>Parses KeyValues text into a root node whose children are the top-level keys. Throws FormatException
    /// with the line number on text it can't read.</summary>
    public static VdfNode Parse(string text)
    {
        var pos = 0;
        var root = new VdfNode();
        ReadPairs(text, ref pos, root, topLevel: true);
        return root;
    }

    static void ReadPairs(string s, ref int pos, VdfNode into, bool topLevel)
    {
        while (true)
        {
            SkipSpace(s, ref pos);
            if (pos >= s.Length)
            {
                if (topLevel) return;
                throw new FormatException($"line {Line(s, pos)}: the file ends inside a {{ }} block (a closing }} is missing).");
            }
            if (s[pos] == '}')
            {
                if (topLevel) throw new FormatException($"line {Line(s, pos)}: a }} with no {{ before it.");
                pos++;
                return;
            }
            var key = ReadToken(s, ref pos);
            SkipSpace(s, ref pos);
            if (pos >= s.Length) throw new FormatException($"line {Line(s, pos)}: the key \"{key}\" has no value.");
            if (s[pos] == '{')
            {
                pos++;
                var child = new VdfNode();
                ReadPairs(s, ref pos, child, topLevel: false);
                into.Children.Add(new(key, child));
            }
            else
            {
                var value = ReadToken(s, ref pos);
                into.Children.Add(new(key, new VdfNode { Value = value }));
                // a conditional like [$WIN32] after a value is skipped
                SkipSpace(s, ref pos);
                if (pos < s.Length && s[pos] == '[') { while (pos < s.Length && s[pos] != ']') pos++; pos++; }
            }
        }
    }

    static void SkipSpace(string s, ref int pos)
    {
        while (pos < s.Length)
        {
            if (char.IsWhiteSpace(s[pos])) pos++;
            else if (s[pos] == '/' && pos + 1 < s.Length && s[pos + 1] == '/') { while (pos < s.Length && s[pos] != '\n') pos++; }
            else return;
        }
    }

    static string ReadToken(string s, ref int pos)
    {
        var sb = new StringBuilder();
        if (s[pos] == '"')
        {
            var start = pos++;
            while (true)
            {
                if (pos >= s.Length) throw new FormatException($"line {Line(s, start)}: a quoted string is not closed.");
                var c = s[pos++];
                if (c == '"') return sb.ToString();
                if (c == '\\' && pos < s.Length)
                {
                    var n = s[pos++];
                    sb.Append(n switch { 'n' => '\n', 't' => '\t', _ => n });   // \\ and \" give the character itself
                }
                else sb.Append(c);
            }
        }
        if (s[pos] is '{' or '}') throw new FormatException($"line {Line(s, pos)}: expected a key or value, found '{s[pos]}'.");
        while (pos < s.Length && !char.IsWhiteSpace(s[pos]) && s[pos] is not ('{' or '}' or '"')) sb.Append(s[pos++]);
        return sb.ToString();
    }

    static int Line(string s, int pos) => s.AsSpan(0, Math.Min(pos, s.Length)).Count('\n') + 1;

    /// <summary>The library folders listed in a libraryfolders.vdf (in file order, no duplicates).</summary>
    public static List<string> LibraryFolders(string vdfText)
    {
        var root = Parse(vdfText);
        var lib = root["libraryfolders"] ?? root["LibraryFolders"];
        var list = new List<string>();
        if (lib == null) return list;
        foreach (var (key, node) in lib.Children)
        {
            if (!key.All(char.IsDigit)) continue;          // "contentstatsid" and other fields
            var path = node.Value ?? node["path"]?.Value;  // old form: "1" "D:\\Lib"; new form: "1" { "path" ... }
            if (!string.IsNullOrWhiteSpace(path) && !list.Contains(path, StringComparer.OrdinalIgnoreCase)) list.Add(path);
        }
        return list;
    }
}
