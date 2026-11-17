using System.Text;

namespace UE4SSManager.Core.Games;

/// <summary>A node in a Valve KeyValues (VDF/ACF) text file: either a string value or a nested object.</summary>
public sealed class VdfNode
{
    public string? Value { get; }
    public Dictionary<string, VdfNode> Children { get; } = new(StringComparer.OrdinalIgnoreCase);

    public VdfNode(string? value = null) => Value = value;

    public VdfNode? this[string key] => Children.TryGetValue(key, out var n) ? n : null;

    public string? GetString(string key) => this[key]?.Value;
}

/// <summary>Minimal parser for Steam's text KeyValues format (libraryfolders.vdf, appmanifest_*.acf).</summary>
public static class VdfParser
{
    public static VdfNode Parse(string text)
    {
        var tokens = Tokenize(text).GetEnumerator();
        var root = new VdfNode();
        ParseObject(tokens, root, topLevel: true);
        return root;
    }

    public static VdfNode ParseFile(string path) => Parse(File.ReadAllText(path));

    private static void ParseObject(IEnumerator<string?> tokens, VdfNode target, bool topLevel)
    {
        while (tokens.MoveNext())
        {
            var key = tokens.Current;
            if (key is null) // '}' marker
            {
                if (topLevel) continue;
                return;
            }
            if (key == "{") continue; // stray brace, tolerate
            if (!tokens.MoveNext()) return;
            var next = tokens.Current;
            if (next == "{")
            {
                var child = new VdfNode();
                ParseObject(tokens, child, topLevel: false);
                target.Children[key] = child;
            }
            else if (next is null)
            {
                // "key" } -> malformed, treat as end of object
                target.Children[key] = new VdfNode(string.Empty);
                if (!topLevel) return;
            }
            else
            {
                target.Children[key] = new VdfNode(next);
            }
        }
    }

    // Yields strings; "{" for an open brace and null for a close brace. Quoted strings that are literally "{"
    // are extremely unlikely in Steam files, so this simple encoding is fine.
    private static IEnumerable<string?> Tokenize(string text)
    {
        int i = 0;
        while (i < text.Length)
        {
            char c = text[i];
            if (char.IsWhiteSpace(c)) { i++; continue; }
            if (c == '/' && i + 1 < text.Length && text[i + 1] == '/')
            {
                while (i < text.Length && text[i] != '\n') i++;
                continue;
            }
            if (c == '{') { i++; yield return "{"; continue; }
            if (c == '}') { i++; yield return null; continue; }
            if (c == '"')
            {
                i++;
                var sb = new StringBuilder();
                while (i < text.Length && text[i] != '"')
                {
                    if (text[i] == '\\' && i + 1 < text.Length)
                    {
                        char e = text[i + 1];
                        sb.Append(e switch { 'n' => '\n', 't' => '\t', '\\' => '\\', '"' => '"', _ => e });
                        i += 2;
                        continue;
                    }
                    sb.Append(text[i]);
                    i++;
                }
                i++; // closing quote
                yield return sb.ToString();
                continue;
            }
            // unquoted token
            int start = i;
            while (i < text.Length && !char.IsWhiteSpace(text[i]) && text[i] != '{' && text[i] != '}' && text[i] != '"') i++;
            yield return text[start..i];
        }
    }
}
