using UE4SSManager.Core.Util;

namespace UE4SSManager.Core.Install;

public enum Ue4ssLayout
{
    None,
    /// <summary>UE4SS 2.x: UE4SS.dll, settings and Mods\ directly in Win64.</summary>
    Flat,
    /// <summary>UE4SS 3.x: dwmapi.dll in Win64, everything else in Win64\ue4ss\.</summary>
    Folder,
}

public sealed record Ue4ssStatus
{
    public required string Win64Path { get; init; }
    public Ue4ssLayout Layout { get; init; }
    public string? Ue4ssDir { get; init; }
    public string? ProxyDll { get; init; }
    public string? Version { get; init; }
    public InstallManifest? Manifest { get; init; }

    public bool IsInstalled => Layout != Ue4ssLayout.None;
    public bool ProxyMissing => IsInstalled && ProxyDll is null;
    public bool InstalledByManager => Manifest is not null;

    public string? SettingsPath => Ue4ssDir is null ? null : Path.Combine(Ue4ssDir, "UE4SS-settings.ini");
    public string? LogPath => Ue4ssDir is null ? null : Path.Combine(Ue4ssDir, "UE4SS.log");

    /// <summary>Where mods live. When UE4SS isn't installed this is where they would go (3.x layout).</summary>
    public string ModsDir => Path.Combine(Ue4ssDir ?? Path.Combine(Win64Path, "ue4ss"), "Mods");
}

public static class Ue4ssDetector
{
    /// <summary>Proxy DLL names UE4SS can be loaded through.</summary>
    public static readonly string[] ProxyNames =
        ["dwmapi.dll", "xinput1_3.dll", "dxgi.dll", "d3d11.dll", "version.dll", "winmm.dll", "dinput8.dll"];

    /// <summary>Names that games essentially never ship themselves, so seeing them next to UE4SS.dll is enough.</summary>
    private static readonly string[] SafeProxyNames = ["dwmapi.dll", "xinput1_3.dll"];

    public static Ue4ssStatus Detect(string win64, IFileVersionReader? versions = null)
    {
        versions ??= SystemFileVersionReader.Instance;
        var manifest = InstallManifest.TryLoad(win64);

        string? ue4ssDir = null;
        var layout = Ue4ssLayout.None;
        var folder = PathUtil.FindChildDirectory(win64, "ue4ss");
        if (folder is not null && PathUtil.FindChildFile(folder, "UE4SS.dll") is not null)
        {
            ue4ssDir = folder;
            layout = Ue4ssLayout.Folder;
        }
        else if (PathUtil.FindChildFile(win64, "UE4SS.dll") is not null)
        {
            ue4ssDir = win64;
            layout = Ue4ssLayout.Flat;
        }

        string? proxy = null;
        if (layout != Ue4ssLayout.None)
        {
            if (manifest?.ProxyDll is { } p && PathUtil.FindChildFile(win64, p) is not null) proxy = p;
            proxy ??= SafeProxyNames.FirstOrDefault(n => PathUtil.FindChildFile(win64, n) is not null);
            proxy ??= ProxyNames.Except(SafeProxyNames).FirstOrDefault(n =>
                PathUtil.FindChildFile(win64, n) is { } f &&
                (versions.Read(f)?.ProductName?.Contains("UE4SS", StringComparison.OrdinalIgnoreCase) ?? false));
        }

        string? version = null;
        if (layout != Ue4ssLayout.None)
        {
            version = manifest?.Version;
            if (version is null && PathUtil.FindChildFile(ue4ssDir!, "UE4SS.dll") is { } dll)
            {
                var v = versions.Read(dll);
                version = v?.ProductVersion ?? v?.FileVersion;
                if (version is not null) version = version.Trim().TrimStart('v');
            }
        }

        return new Ue4ssStatus
        {
            Win64Path = win64,
            Layout = layout,
            Ue4ssDir = ue4ssDir,
            ProxyDll = proxy,
            Version = version,
            Manifest = layout == Ue4ssLayout.None ? null : manifest,
        };
    }
}
