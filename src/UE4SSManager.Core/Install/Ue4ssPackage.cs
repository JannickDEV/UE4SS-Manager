using System.IO.Compression;

namespace UE4SSManager.Core.Install;

/// <summary>A UE4SS release zip, understood well enough to install it: where UE4SS.dll sits and which file is the proxy DLL.</summary>
public sealed class Ue4ssPackage : IDisposable
{
    private readonly ZipArchive _zip;
    private readonly string _prefix;

    /// <summary>Files in the package, relative to the folder that maps onto Win64 (forward slashes).</summary>
    public IReadOnlyList<string> Files { get; }
    /// <summary>"ue4ss" for 3.x packages, "" for flat 2.x packages.</summary>
    public string Ue4ssFolder { get; }
    public Ue4ssLayout Layout => Ue4ssFolder.Length == 0 ? Ue4ssLayout.Flat : Ue4ssLayout.Folder;
    /// <summary>Root-level proxy DLL in the package (e.g. dwmapi.dll), or null for injector-only builds.</summary>
    public string? ProxyFile { get; }

    private Ue4ssPackage(ZipArchive zip)
    {
        _zip = zip;
        var names = zip.Entries.Where(e => !e.FullName.EndsWith('/') && e.Length >= 0 && e.Name.Length > 0)
                               .Select(e => e.FullName.Replace('\\', '/')).ToList();

        // Strip a single wrapper folder (e.g. a hand-made "UE4SS_v3.0.1/..." zip).
        _prefix = "";
        for (int pass = 0; pass < 2; pass++)
        {
            var rel = names.Select(n => n[_prefix.Length..]).ToList();
            if (rel.Any(n => !n.Contains('/'))) break;
            var tops = rel.Select(n => n[..n.IndexOf('/')]).Distinct(StringComparer.OrdinalIgnoreCase).ToList();
            if (tops.Count != 1 || tops[0].Equals("ue4ss", StringComparison.OrdinalIgnoreCase)) break;
            _prefix += tops[0] + "/";
        }
        Files = names.Where(n => n.StartsWith(_prefix, StringComparison.Ordinal)).Select(n => n[_prefix.Length..]).ToList();

        var dll = Files.FirstOrDefault(f => string.Equals(Path.GetFileName(f), "UE4SS.dll", StringComparison.OrdinalIgnoreCase)
                                            && f.Count(c => c == '/') <= 1)
                  ?? throw new InvalidDataException("This zip doesn't contain UE4SS.dll, so it isn't a UE4SS release package.");
        Ue4ssFolder = dll.Contains('/') ? dll[..dll.IndexOf('/')] : "";

        ProxyFile = Files.Where(f => !f.Contains('/') && f.EndsWith(".dll", StringComparison.OrdinalIgnoreCase)
                                     && !f.Equals("UE4SS.dll", StringComparison.OrdinalIgnoreCase))
                         .OrderByDescending(f => Ue4ssDetector.ProxyNames.Contains(f, StringComparer.OrdinalIgnoreCase))
                         .FirstOrDefault();
    }

    public static Ue4ssPackage Open(string zipPath) => new(ZipFile.OpenRead(zipPath));
    public static Ue4ssPackage Open(Stream stream) => new(new ZipArchive(stream, ZipArchiveMode.Read));

    public Stream OpenFile(string relative) =>
        (_zip.GetEntry(_prefix + relative) ?? _zip.Entries.First(e => e.FullName.Replace('\\', '/') == _prefix + relative)).Open();

    public string ReadText(string relative)
    {
        using var r = new StreamReader(OpenFile(relative));
        return r.ReadToEnd();
    }

    public void Dispose() => _zip.Dispose();
}
