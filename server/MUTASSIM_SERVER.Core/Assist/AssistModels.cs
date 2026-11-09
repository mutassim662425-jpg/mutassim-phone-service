namespace MUTASSIM_SERVER.Core.Assist;

/// <summary>
/// What MUTASSIM TOOL PRO knows about the connected phone. Every field is optional and only filled
/// with what the device really reported. No serial / IMEI is required.
/// </summary>
public sealed record AssistDevice(
    string? Mode,
    string? Brand = null,
    string? Manufacturer = null,
    string? Model = null,
    string? Chipset = null,
    string? AndroidVersion = null,
    string? BootloaderState = null);

public sealed record AssistRequest(
    string ClientId,
    string Problem,
    AssistDevice? Device,
    string? Language = null);

/// <summary>
/// One step for the tool to run. ActionId always comes from <see cref="AssistActions.All"/>; the tool maps it
/// to its own built-in implementation. The server never sends shell commands.
/// </summary>
public sealed record AssistStep(
    int Order,
    string ActionId,
    string TitleEn,
    string TitleAr,
    string ReasonEn,
    string ReasonAr,
    bool Dangerous);

public sealed record AssistNote(string En, string Ar);

public sealed record AssistResponse(
    Guid SessionId,
    string Problem,
    string ModeGroup,
    string SummaryEn,
    string SummaryAr,
    IReadOnlyList<AssistStep> Steps,
    IReadOnlyList<AssistNote> Guidance);

public sealed record AssistFeedback(
    Guid SessionId,
    string ActionId,
    bool Success,
    string? Message);

public sealed record AssistProblem(string Code, string TitleEn, string TitleAr);

public sealed record AssistSessionRecord(
    AssistRequest Request,
    AssistResponse Response,
    DateTime CreatedAt,
    IReadOnlyList<AssistFeedbackEntry> Feedback);

public sealed record AssistFeedbackEntry(string ActionId, bool Success, string? Message, DateTime ReceivedAt);
