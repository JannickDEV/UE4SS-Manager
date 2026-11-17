using System.Text.RegularExpressions;

namespace UE4SSManager.Core.Mods;

/// <summary>Line-preserving editor for UE4SS's Mods\mods.txt ("ModName : 1" per line, ';' comments).</summary>
public sealed partial class ModsTxt
{
    private readonly List<string> _lines;

    public ModsTxt(IEnumerable<string> lines) => _lines = lines.ToList();

    public static ModsTxt Load(string path) => new(File.Exists(path) ? File.ReadAllLines(path) : []);
    public static ModsTxt Parse(string text) => new(text.Replace("\r\n", "\n").Split('\n'));

    public void Save(string path)
    {
        // Drop a trailing empty line produced by Split so the file doesn't grow on each save.
        var lines = _lines.ToList();
        while (lines.Count > 0 && lines[^1].Length == 0) lines.RemoveAt(lines.Count - 1);
        File.WriteAllLines(path, lines);
    }

    public override string ToString() => string.Join(Environment.NewLine, _lines);

    public IReadOnlyList<(string Name, bool Enabled)> Entries =>
        _lines.Select(l => EntryRegex().Match(l)).Where(m => m.Success)
              .Select(m => (m.Groups[1].Value.Trim(), m.Groups[2].Value == "1")).ToList();

    public bool? Get(string name)
    {
        var i = IndexOf(name);
        return i < 0 ? null : EntryRegex().Match(_lines[i]).Groups[2].Value == "1";
    }

    public void Set(string name, bool enabled)
    {
        var line = $"{name} : {(enabled ? 1 : 0)}";
        var i = IndexOf(name);
        if (i >= 0) { _lines[i] = line; return; }
        _lines.Insert(InsertIndex(), line);
    }

    public bool Remove(string name)
    {
        var i = IndexOf(name);
        if (i < 0) return false;
        _lines.RemoveAt(i);
        return true;
    }

    /// <summary>Adds entries from a newer mods.txt that this one doesn't have yet, keeping every existing choice.</summary>
    public void MergeMissingFrom(ModsTxt newer)
    {
        foreach (var (name, enabled) in newer.Entries)
            if (Get(name) is null) Set(name, enabled);
    }

    // New mods go above the built-in Keybinds entry and its "do not move up" comment, which UE4SS wants last.
    private int InsertIndex()
    {
        var kb = IndexOf("Keybinds");
        if (kb < 0)
        {
            int last = _lines.Count;
            while (last > 0 && _lines[last - 1].Trim().Length == 0) last--;
            return last;
        }
        int idx = kb;
        while (idx > 0 && _lines[idx - 1].TrimStart().StartsWith(';')) idx--;
        return idx;
    }

    private int IndexOf(string name)
    {
        for (int i = 0; i < _lines.Count; i++)
        {
            var m = EntryRegex().Match(_lines[i]);
            if (m.Success && string.Equals(m.Groups[1].Value.Trim(), name, StringComparison.OrdinalIgnoreCase)) return i;
        }
        return -1;
    }

    [GeneratedRegex(@"^\s*([^;:#][^:]*?)\s*:\s*([01])\s*(?:;.*)?$")]
    private static partial Regex EntryRegex();
}
