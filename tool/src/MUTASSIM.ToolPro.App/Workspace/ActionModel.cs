using Microsoft.Extensions.DependencyInjection;
using MUTASSIM.ToolPro.Adb;
using MUTASSIM.ToolPro.App.Services;
using MUTASSIM.ToolPro.Core.Devices;
using MUTASSIM.ToolPro.Fastboot;
using MUTASSIM.ToolPro.Usb;

namespace MUTASSIM.ToolPro.App.Workspace;

public enum DeviceRequirement
{
    None,
    Adb,
    Fastboot,
    AnyDevice
}

public sealed record ActionResult(bool Success, string Message)
{
    public static ActionResult Ok(string message) => new(true, message);
    public static ActionResult Fail(string message) => new(false, message);
}

public sealed class ActionContext(
    DeviceInfo? device,
    OperationLog log,
    IServiceProvider services,
    IDialogService dialogs,
    CancellationToken cancellationToken)
{
    public DeviceInfo? Device { get; } = device;
    public OperationLog Log { get; } = log;
    public IDialogService Dialogs { get; } = dialogs;
    public CancellationToken Ct { get; } = cancellationToken;

    /// <summary>Serial of the selected device. Requirement checks guarantee it exists for ADB/Fastboot actions.</summary>
    public string Serial => Device?.SerialNumber ?? throw new InvalidOperationException("The selected device has no serial number.");

    public T Get<T>() where T : notnull => services.GetRequiredService<T>();
    public AdbClient Adb => services.GetRequiredService<AdbClient>();
    public FastbootClient Fastboot => services.GetRequiredService<FastbootClient>();
    public UsbDeviceDetector Usb => services.GetRequiredService<UsbDeviceDetector>();
    public AdbDeviceDetector AdbDetector => services.GetRequiredService<AdbDeviceDetector>();
    public FastbootDeviceDetector FastbootDetector => services.GetRequiredService<FastbootDeviceDetector>();
}

/// <summary>One button. Every visible button is backed by a real implementation in <see cref="Run"/>.</summary>
public sealed record ActionDefinition(
    string Id,
    string TitleEn,
    string TitleAr,
    DeviceRequirement Requires,
    Func<ActionContext, Task<ActionResult>> Run)
{
    public string? ConfirmEn { get; init; }
    public string? ConfirmAr { get; init; }
    public bool IsDangerous { get; init; }

    /// <summary>When set, the operation consumes credits with this billing code (charged before, refunded on failure).</summary>
    public string? BillingCode { get; init; }

    public string Title => LocalizationService.Get("Lang.IsRtl") == "true" ? TitleAr : TitleEn;
    public string Glyph => ActionGlyphs.For(Id);
    public string? Confirm => LocalizationService.Get("Lang.IsRtl") == "true" ? ConfirmAr ?? ConfirmEn : ConfirmEn;

    public bool Accepts(ConnectionMode mode) => Requires switch
    {
        DeviceRequirement.None => true,
        DeviceRequirement.AnyDevice => true,
        DeviceRequirement.Adb => mode is ConnectionMode.Adb or ConnectionMode.Recovery,
        DeviceRequirement.Fastboot => mode is ConnectionMode.Fastboot or ConnectionMode.FastbootD,
        _ => false
    };
}

public sealed record ActionGroup(string TitleEn, string TitleAr, IReadOnlyList<ActionDefinition> Actions)
{
    public string Title => LocalizationService.Get("Lang.IsRtl") == "true" ? TitleAr : TitleEn;
}

public enum TerminalKind
{
    None,
    Adb,
    Fastboot
}

public sealed record WorkspaceDefinition(
    string Route,
    string TabEn,
    string TabAr,
    IReadOnlyList<string> ModuleIds,
    IReadOnlyList<ActionGroup> Groups,
    TerminalKind Terminal = TerminalKind.None)
{
    public string Tab => LocalizationService.Get("Lang.IsRtl") == "true" ? TabAr : TabEn;
}

/// <summary>Segoe Fluent / MDL2 icon for an action, chosen from its id.</summary>
public static class ActionGlyphs
{
    public static string For(string id)
    {
        var i = id.ToLowerInvariant();
        if (i.Contains("terminal")) return "\uE756";
        if (i.Contains("edl")) return "\uE9D9";
        if (i.Contains("download")) return "\uE896";
        if (i.Contains("reboot")) return "\uE72C";
        if (i.Contains("flash")) return "\uE945";
        if (i.Contains("fw.") || i.Contains("firmware")) return "\uE8B7";
        if (i.Contains("uninstall")) return "\uE74D"; // before "install" (substring)
        if (i.Contains("install")) return "\uE896";
        if (i.Contains("apps")) return "\uE71D";
        if (i.Contains("usb") || i.Contains("ports")) return "\uE88E";
        if (i.Contains("google")) return "\uE774";
        if (i.StartsWith("frp", StringComparison.Ordinal)) return "\uE72E";
        if (i.StartsWith("mdm", StringComparison.Ordinal)) return "\uE821";
        if (i.Contains("info")) return "\uE946";
        return "\uE76C";
    }
}
