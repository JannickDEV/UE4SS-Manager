using System.Diagnostics;

namespace UE4SSManager.Core.Util;

public sealed record FileVersionData(string? ProductName, string? ProductVersion, string? FileVersion, string? FileDescription);

/// <summary>Reads PE version resources (exe/dll). Abstracted so tests can fake it.</summary>
public interface IFileVersionReader
{
    FileVersionData? Read(string path);
}

public sealed class SystemFileVersionReader : IFileVersionReader
{
    public static readonly SystemFileVersionReader Instance = new();

    public FileVersionData? Read(string path)
    {
        try
        {
            if (!File.Exists(path)) return null;
            var info = FileVersionInfo.GetVersionInfo(path);
            if (info.ProductName is null && info.FileVersion is null && info.ProductVersion is null) return null;
            return new FileVersionData(info.ProductName, info.ProductVersion, info.FileVersion, info.FileDescription);
        }
        catch { return null; }
    }
}
