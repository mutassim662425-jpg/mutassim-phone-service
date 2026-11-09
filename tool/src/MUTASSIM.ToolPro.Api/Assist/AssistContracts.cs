namespace MUTASSIM.ToolPro.Api.Assist;

// JSON contract with MUTASSIM GSM CLOUD server (/api/assist/*). Field names must stay in sync with
// MUTASSIM_SERVER.Core.Assist on the server side.

/// <summary>Device state sent to the server. Serial / IMEI are never sent.</summary>
public sealed record AssistDeviceDto(
    string? Mode,
    string? Brand,
    string? Manufacturer,
    string? Model,
    string? Chipset,
    string? AndroidVersion,
    string? BootloaderState);

public sealed record AssistRequestDto(string ClientId, string Problem, AssistDeviceDto? Device, string? Language);

public sealed record AssistStepDto(
    int Order,
    string ActionId,
    string TitleEn,
    string TitleAr,
    string ReasonEn,
    string ReasonAr,
    bool Dangerous);

public sealed record AssistNoteDto(string En, string Ar);

public sealed record AssistResponseDto(
    Guid SessionId,
    string Problem,
    string ModeGroup,
    string SummaryEn,
    string SummaryAr,
    IReadOnlyList<AssistStepDto> Steps,
    IReadOnlyList<AssistNoteDto> Guidance);

public sealed record AssistFeedbackDto(Guid SessionId, string ActionId, bool Success, string? Message);

public sealed record AssistProblemDto(string Code, string TitleEn, string TitleAr);

public sealed record AssistHealthDto(bool Success, string? Server, string? Version, string? Status, bool? AuthRequired);

/// <summary>A server answer after local validation: steps the tool does not know were removed.</summary>
public sealed record AssistAnswer(AssistResponseDto Response, IReadOnlyList<AssistStepDto> Steps, IReadOnlyList<string> RejectedActionIds);

/// <summary>Readable error for the UI (never contains a stack trace).</summary>
public sealed class AssistServerException(string message, Exception? inner = null) : Exception(message, inner);

/// <summary>
/// Action IDs this tool is willing to run when the server recommends them. Each maps to a built-in
/// function of the tool. Must match the server's allow-list (MUTASSIM_SERVER.Core.Assist.AssistActions).
/// </summary>
public static class AssistKnownActions
{
    public static readonly IReadOnlySet<string> All = new HashSet<string>(StringComparer.Ordinal)
    {
        "device.info", "adb.info", "fb.info", "usb.scan",
        "adb.reboot.system", "adb.reboot.recovery", "adb.reboot.bootloader", "adb.reboot.fastbootd",
        "adb.reboot.download", "adb.reboot.edl",
        "fb.reboot.system", "fb.reboot.recovery", "fb.reboot.bootloader", "fb.reboot.fastbootd",
        "adb.apps.user", "adb.uninstall",
        "sam.fw.check",
        "frp.info", "frp.google", "mdm.info"
    };
}
