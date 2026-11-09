using MUTASSIM.ToolPro.Core.Devices;
using MUTASSIM.ToolPro.Core.Modules;
using static MUTASSIM.ToolPro.Core.Modules.Svc;

namespace MUTASSIM.ToolPro.Api;

/// <summary>Contract every GSM service-provider plugin implements (Phase 7).</summary>
public interface IApiProvider
{
    string ProviderId { get; }
    string DisplayName { get; }
    Task<IReadOnlyList<RemoteService>> GetServicesAsync(CancellationToken cancellationToken);
    Task<RemoteOrder> PlaceOrderAsync(string serviceId, IReadOnlyDictionary<string, string> fields, CancellationToken cancellationToken);
    Task<RemoteOrder> GetOrderAsync(string orderId, CancellationToken cancellationToken);
    Task<decimal?> GetBalanceAsync(CancellationToken cancellationToken);
}

public sealed record RemoteService(string Id, string Name, decimal? Price, string? Currency, string? DeliveryTime);

public enum RemoteOrderState { Pending, InProcess, Completed, Rejected, Failed }

public sealed record RemoteOrder(string OrderId, string ServiceId, RemoteOrderState State, string? Result,
    DateTimeOffset CreatedAt, DateTimeOffset? CompletedAt, string? ProviderMessage);

/// <summary>Provider connection settings. The API key itself is never stored here (DPAPI-encrypted store, Phase 7).</summary>
public sealed record ApiProviderConfig(string ProviderId, string DisplayName, Uri BaseUrl, string SecretRef,
    TimeSpan Timeout, int MaxRetries);

public sealed class ApiServicesModule : ServiceModuleBase
{
    public override string Id => ModuleIds.ApiServices;
    public override string DisplayName => "API Services";
    public override ModuleCategory Category => ModuleCategory.System;
    public override string Description =>
        "Integration engine for GSM service providers. Each provider is an independent plugin; API keys are encrypted with Windows DPAPI and never embedded in code.";

    public override IReadOnlyList<ServiceDescriptor> Services { get; } =
    [
        Planned("api.providers", "Add / remove providers (plugins)", 7),
        Planned("api.keys", "Secure API key storage (DPAPI)", 7),
        Planned("api.catalog", "Service catalogue & prices", 7),
        Planned("api.order", "Place order / track status / receive result", 7),
        Planned("api.balance", "Balance (when provider supports it)", 7),
        Planned("api.resilience", "Timeouts, retries, provider error display", 7)
    ];

    public override bool CanHandle(DeviceInfo device) => false;
}
