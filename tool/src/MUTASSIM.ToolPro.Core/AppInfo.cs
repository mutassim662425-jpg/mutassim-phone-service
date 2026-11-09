using System.Reflection;

namespace MUTASSIM.ToolPro.Core;

/// <summary>Product identity. Single source of truth for name/version shown in the UI.</summary>
public static class AppInfo
{
    public const string ProductName = "MUTASSIM TOOL PRO";
    public const string ShortName = "MUTASSIM";
    public const string Developer = "MUTASSIM SOFTWARE";
    public const string Slogan = "POWERED BY MUTASSIM";

    public static Version Version { get; } =
        typeof(AppInfo).Assembly.GetName().Version ?? new Version(1, 0, 0, 0);

    /// <summary>Marketing version, e.g. "V1.0".</summary>
    public static string DisplayVersion => $"V{Version.Major}.{Version.Minor}";

    public static string InformationalVersion =>
        typeof(AppInfo).Assembly.GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion
        ?? Version.ToString();
}
