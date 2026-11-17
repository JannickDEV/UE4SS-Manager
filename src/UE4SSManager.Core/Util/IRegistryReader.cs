using System.Runtime.Versioning;
using Microsoft.Win32;

namespace UE4SSManager.Core.Util;

public enum RegHive { CurrentUser, LocalMachine }

/// <summary>Small seam over the Windows registry so game detection can be tested anywhere.</summary>
public interface IRegistryReader
{
    string? GetString(RegHive hive, string keyPath, string valueName);
    IReadOnlyList<string> GetSubKeyNames(RegHive hive, string keyPath);
}

public sealed class NullRegistryReader : IRegistryReader
{
    public static readonly NullRegistryReader Instance = new();
    public string? GetString(RegHive hive, string keyPath, string valueName) => null;
    public IReadOnlyList<string> GetSubKeyNames(RegHive hive, string keyPath) => [];
}

[SupportedOSPlatform("windows")]
public sealed class WindowsRegistryReader : IRegistryReader
{
    private static RegistryKey Root(RegHive hive) => hive == RegHive.CurrentUser ? Registry.CurrentUser : Registry.LocalMachine;

    public string? GetString(RegHive hive, string keyPath, string valueName)
    {
        try
        {
            using var key = Root(hive).OpenSubKey(keyPath);
            return key?.GetValue(valueName) as string;
        }
        catch { return null; }
    }

    public IReadOnlyList<string> GetSubKeyNames(RegHive hive, string keyPath)
    {
        try
        {
            using var key = Root(hive).OpenSubKey(keyPath);
            return key?.GetSubKeyNames() ?? [];
        }
        catch { return []; }
    }
}

public static class RegistryReader
{
    public static IRegistryReader CreateDefault() =>
        OperatingSystem.IsWindows() ? new WindowsRegistryReader() : NullRegistryReader.Instance;
}
