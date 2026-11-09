using MUTASSIM.ToolPro.Core.Devices;

namespace MUTASSIM.ToolPro.Core.Modules;

/// <summary>The six honest implementation states required by the acceptance criteria.</summary>
public enum ImplementationStatus
{
    /// <summary>Implemented and verified (unit tests + real hardware where applicable).</summary>
    CompletedTested,
    /// <summary>Implemented; still needs verification on a real device / real Windows machine.</summary>
    CompletedNeedsDeviceTest,
    InDevelopment,
    /// <summary>Needs an authorized account, signed files, or an OEM-approved service.</summary>
    RequiresExternalAuthorization,
    /// <summary>Needs a protocol/tool/hardware that is not part of this program.</summary>
    RequiresAdditionalProtocolOrHardware,
    NotSupported
}

public enum ModuleCategory
{
    Brand,
    Chipset,
    Protocol,
    Service,
    System
}

/// <summary>Describes one service a module offers and its real implementation state.</summary>
public sealed record ServiceDescriptor(
    string Id,
    string Name,
    ImplementationStatus Status,
    int PlannedPhase,
    string? Notes = null)
{
    public IReadOnlyList<ConnectionMode> RequiredModes { get; init; } = [];

    /// <summary>May erase data or change the device. Requires explicit user consent.</summary>
    public bool IsDestructive { get; init; }

    public bool IsAvailableNow =>
        Status is ImplementationStatus.CompletedTested or ImplementationStatus.CompletedNeedsDeviceTest;
}
