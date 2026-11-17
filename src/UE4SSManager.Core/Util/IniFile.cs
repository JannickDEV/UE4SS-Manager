namespace UE4SSManager.Core.Util;

/// <summary>Line-preserving INI editor: changing one key keeps every comment and the layout of UE4SS-settings.ini intact.</summary>
public sealed class IniFile
{
    private readonly List<string> _lines;

    public IniFile(IEnumerable<string> lines) => _lines = lines.ToList();

    public static IniFile Load(string path) => new(File.Exists(path) ? File.ReadAllLines(path) : []);

    public void Save(string path) => File.WriteAllLines(path, _lines);

    public override string ToString() => string.Join(Environment.NewLine, _lines);

    public string? Get(string section, string key)
    {
        var idx = FindKey(section, key);
        if (idx < 0) return null;
        var line = _lines[idx];
        var value = line[(line.IndexOf('=') + 1)..];
        var comment = value.IndexOf(';');
        if (comment >= 0) value = value[..comment];
        return value.Trim();
    }

    public bool? GetBool(string section, string key) => Get(section, key) switch
    {
        null => null,
        var v when v == "1" || v.Equals("true", StringComparison.OrdinalIgnoreCase) => true,
        _ => false,
    };

    public void Set(string section, string key, string value)
    {
        var idx = FindKey(section, key);
        if (idx >= 0)
        {
            var line = _lines[idx];
            var eq = line.IndexOf('=');
            var left = line[..eq].TrimEnd();
            _lines[idx] = $"{left} = {value}";
            return;
        }
        var sec = FindSection(section);
        if (sec < 0)
        {
            if (_lines.Count > 0 && _lines[^1].Trim().Length > 0) _lines.Add("");
            _lines.Add($"[{section}]");
            _lines.Add($"{key} = {value}");
            return;
        }
        // insert after the last non-empty line of the section
        int insert = sec + 1;
        for (int i = sec + 1; i < _lines.Count && !IsSection(_lines[i]); i++)
            if (_lines[i].Trim().Length > 0) insert = i + 1;
        _lines.Insert(insert, $"{key} = {value}");
    }

    public void SetBool(string section, string key, bool value) => Set(section, key, value ? "1" : "0");

    private static bool IsSection(string line)
    {
        var t = line.Trim();
        return t.StartsWith('[') && t.Contains(']');
    }

    private int FindSection(string section)
    {
        for (int i = 0; i < _lines.Count; i++)
        {
            var t = _lines[i].Trim();
            if (IsSection(t) && string.Equals(t[1..t.IndexOf(']')].Trim(), section, StringComparison.OrdinalIgnoreCase)) return i;
        }
        return -1;
    }

    private int FindKey(string section, string key)
    {
        var sec = FindSection(section);
        if (sec < 0) return -1;
        for (int i = sec + 1; i < _lines.Count && !IsSection(_lines[i]); i++)
        {
            var t = _lines[i].TrimStart();
            if (t.StartsWith(';') || t.StartsWith('#')) continue;
            var eq = t.IndexOf('=');
            if (eq > 0 && string.Equals(t[..eq].Trim(), key, StringComparison.OrdinalIgnoreCase)) return i;
        }
        return -1;
    }
}
