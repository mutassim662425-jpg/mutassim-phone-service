using MUTASSIM.ToolPro.App.Services;
using MUTASSIM.ToolPro.Core;
using MUTASSIM.ToolPro.Core.Modules;

namespace MUTASSIM.ToolPro.App.ViewModels;

public sealed record StatusCount(ImplementationStatus Status, int Count);

public sealed record FeatureRow(string Module, string Service, ImplementationStatus Status, string Phase, string? Notes);

/// <summary>About page + the feature status matrix required by the acceptance criteria (spec §29).</summary>
public sealed class AboutViewModel
{
    public AboutViewModel(IModuleRegistry registry)
    {
        var modules = registry.Modules;
        Rows = modules
            .SelectMany(m => m.Services.Select(s => new FeatureRow(
                m.DisplayName,
                s.Name,
                s.Status,
                s.PlannedPhase > 0 ? LocalizationService.Format("Module.PhaseN", s.PlannedPhase) : "—",
                s.Notes)))
            .ToArray();

        Counts = Enum.GetValues<ImplementationStatus>()
            .Select(st => new StatusCount(st, Rows.Count(r => r.Status == st)))
            .ToArray();
    }

    public string ProductName => AppInfo.ProductName;
    public string DisplayVersion => AppInfo.DisplayVersion;
    public string InformationalVersion => AppInfo.InformationalVersion;
    public string Developer => AppInfo.Developer;
    public string Slogan => AppInfo.Slogan;
    public string Runtime => $".NET {Environment.Version} · {Environment.OSVersion.VersionString} · {(Environment.Is64BitProcess ? "x64" : "x86")}";

    public IReadOnlyList<FeatureRow> Rows { get; }
    public IReadOnlyList<StatusCount> Counts { get; }
}
