using System.Text.Json;
using System.Text.Json.Nodes;

namespace UE4SSManager.Core.Mods;

/// <summary>Editor for Mods\mods.json used by newer UE4SS builds: [{ "mod_name": "X", "mod_enabled": true }].</summary>
public sealed class ModsJson
{
    private readonly JsonArray _array;

    private ModsJson(JsonArray array) => _array = array;

    public static ModsJson Parse(string json) =>
        new(JsonNode.Parse(json, documentOptions: new JsonDocumentOptions { AllowTrailingCommas = true, CommentHandling = JsonCommentHandling.Skip }) as JsonArray ?? []);

    public static ModsJson Load(string path) => File.Exists(path) ? Parse(File.ReadAllText(path)) : new ModsJson([]);

    public void Save(string path) => File.WriteAllText(path, _array.ToJsonString(new JsonSerializerOptions { WriteIndented = true }));

    public IReadOnlyList<(string Name, bool Enabled)> Entries =>
        _array.OfType<JsonObject>()
              .Where(o => o["mod_name"] is not null)
              .Select(o => (o["mod_name"]!.GetValue<string>(), o["mod_enabled"]?.GetValue<bool>() ?? false))
              .ToList();

    public bool? Get(string name) => Find(name)?["mod_enabled"]?.GetValue<bool>();

    public void Set(string name, bool enabled)
    {
        var o = Find(name);
        if (o is not null) { o["mod_enabled"] = enabled; return; }
        var entry = new JsonObject { ["mod_name"] = name, ["mod_enabled"] = enabled };
        var kb = _array.Select((n, i) => (n, i)).FirstOrDefault(t => IsNamed(t.n, "Keybinds"));
        if (kb.n is not null) _array.Insert(kb.i, entry); else _array.Add(entry);
    }

    public bool Remove(string name)
    {
        var o = Find(name);
        return o is not null && _array.Remove(o);
    }

    public void MergeMissingFrom(ModsJson newer)
    {
        foreach (var (name, enabled) in newer.Entries)
            if (Get(name) is null) Set(name, enabled);
    }

    private JsonObject? Find(string name) => _array.OfType<JsonObject>().FirstOrDefault(o => IsNamed(o, name));

    private static bool IsNamed(JsonNode? n, string name) =>
        n is JsonObject o && o["mod_name"] is JsonValue v && v.TryGetValue<string>(out var s) &&
        string.Equals(s, name, StringComparison.OrdinalIgnoreCase);
}
