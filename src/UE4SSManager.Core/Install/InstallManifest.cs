using System.Text.Json;
using System.Text.Json.Serialization;

namespace UE4SSManager.Core.Install;

/// <summary>Written next to the game exe after an install so updates and uninstalls know exactly which files belong to UE4SS.</summary>
public sealed class InstallManifest
{
    public const string FileName = "ue4ss-manager.json";

    public string? Tag { get; set; }
    public string? Version { get; set; }
    public string? AssetName { get; set; }
    public bool IsDevBuild { get; set; }
    public bool IsExperimental { get; set; }
    public string? ProxyDll { get; set; }
    /// <summary>Folder (relative to Win64) holding UE4SS.dll: "ue4ss" for 3.x, "" for the flat 2.x layout.</summary>
    public string Ue4ssFolder { get; set; } = "";
    public DateTimeOffset InstalledAt { get; set; }
    /// <summary>Files written by the installer, relative to Win64, with forward slashes.</summary>
    public List<string> Files { get; set; } = [];

    private static readonly JsonSerializerOptions Options = new()
    {
        WriteIndented = true,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
    };

    public static string PathIn(string win64) => Path.Combine(win64, FileName);

    public static InstallManifest? TryLoad(string win64)
    {
        try
        {
            var path = PathIn(win64);
            return File.Exists(path) ? JsonSerializer.Deserialize<InstallManifest>(File.ReadAllText(path), Options) : null;
        }
        catch { return null; }
    }

    public void Save(string win64) => File.WriteAllText(PathIn(win64), JsonSerializer.Serialize(this, Options));
}
