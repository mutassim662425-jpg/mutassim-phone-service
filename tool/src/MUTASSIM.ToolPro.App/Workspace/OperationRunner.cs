using System.Diagnostics;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Microsoft.Extensions.Logging;
using MUTASSIM.ToolPro.App.Services;
using MUTASSIM.ToolPro.Core.Devices;
using MUTASSIM.ToolPro.Core.Operations;

namespace MUTASSIM.ToolPro.App.Workspace;

/// <summary>
/// Executes one action at a time against the selected device: checks the connection mode, asks for consent
/// when required, records the operation, shows progress and reports the REAL result in the log.
/// </summary>
public sealed partial class OperationRunner(
    DeviceContext context,
    OperationLog log,
    IOperationTracker tracker,
    IDialogService dialogs,
    AccountSession session,
    IServiceProvider services,
    ILogger<OperationRunner> logger) : ObservableObject
{
    private CancellationTokenSource? _cts;

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(StopCommand))]
    private bool _isBusy;

    [ObservableProperty] private string _statusText = "";

    /// <summary>Runs the action. Returns its real result, or null when it was not started (busy, wrong mode, cancelled).</summary>
    public async Task<ActionResult?> RunAsync(ActionDefinition action)
    {
        if (IsBusy)
        {
            log.Warn(LocalizationService.Get("Run.Busy"));
            return null;
        }

        var device = context.Selected;
        if (action.Requires != DeviceRequirement.None)
        {
            if (device is null)
            {
                log.Error(LocalizationService.Get("Run.NoDevice"));
                return null;
            }
            if (device.Mode == ConnectionMode.AdbUnauthorized && action.Requires == DeviceRequirement.Adb)
            {
                log.Error(LocalizationService.Get("Run.Unauthorized"));
                return null;
            }
            if (!action.Accepts(device.Mode))
            {
                log.Error(LocalizationService.Format("Run.WrongMode", RequirementText(action.Requires), device.Mode.ToDisplayText()));
                if (action.Requires == DeviceRequirement.Adb && device.Mode is ConnectionMode.Mtp or ConnectionMode.Unknown)
                    log.Info(LocalizationService.Get("Run.EnableAdbHint"));
                return null;
            }
        }

        if (action.Confirm is { } confirm && !dialogs.Confirm(confirm, action.IsDangerous))
        {
            log.Warn(LocalizationService.Get("Run.CancelledByUser"));
            return null;
        }

        // Billing: charge credits before a paid operation; it is refunded below if the operation fails.
        Guid? chargeId = null;
        if (action.BillingCode is { } billingCode)
        {
            if (!session.IsLoggedIn)
            {
                log.Error(LocalizationService.Get("Account.SignInRequired"));
                return null;
            }
            try
            {
                chargeId = await session.ChargeAsync(billingCode, device?.SerialNumber ?? action.Id);
                log.Muted(LocalizationService.Format("Account.Charged", billingCode));
            }
            catch (MUTASSIM.ToolPro.Api.Account.AccountException ex)
            {
                log.Error(ex.Message);
                return null;
            }
        }

        IsBusy = true;
        StatusText = action.Title;
        _cts = new CancellationTokenSource();
        var sw = Stopwatch.StartNew();
        log.Header(action.Title);
        if (device is not null) log.Muted(DeviceContext.Describe(device));

        IOperationScope? op = null;
        ActionResult? outcome = null;
        try
        {
            op = await tracker.BeginAsync(action.Id, action.TitleEn, device);
            var ctx = new ActionContext(device, log, services, dialogs, _cts.Token);
            // Runs on the UI context (actions may open dialogs); all tool I/O inside is asynchronous.
            var result = await action.Run(ctx);
            outcome = result;

            if (result.Success)
            {
                log.Ok($"OK — {result.Message}");
                await op.SucceedAsync(result.Message);
            }
            else
            {
                log.Error($"FAILED — {result.Message}");
                await op.FailAsync(result.Message);
            }
        }
        catch (OperationCanceledException)
        {
            log.Warn(LocalizationService.Get("Run.Stopped"));
            outcome = ActionResult.Fail("Stopped by user");
            if (op is not null) await op.CancelAsync("Stopped by user");
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Action {Id} failed", action.Id);
            log.Error($"FAILED — {ex.Message}");
            outcome = ActionResult.Fail(ex.Message);
            if (op is not null) await op.FailAsync(ex.Message, ex);
        }
        finally
        {
            // Refund the credits if a paid operation did not succeed.
            if (chargeId is { } cid && outcome is not { Success: true })
            {
                await session.RefundAsync(cid);
                log.Muted(LocalizationService.Get("Account.Refunded"));
            }

            if (op is not null) await op.DisposeAsync();
            log.Muted(LocalizationService.Format("Run.Elapsed", sw.Elapsed.TotalSeconds.ToString("0.0")));
            _cts.Dispose();
            _cts = null;
            IsBusy = false;
            StatusText = "";
        }

        return outcome;
    }

    [RelayCommand(CanExecute = nameof(IsBusy))]
    private void Stop() => _cts?.Cancel();

    private static string RequirementText(DeviceRequirement r) => r switch
    {
        DeviceRequirement.Adb => "ADB",
        DeviceRequirement.Fastboot => "Fastboot",
        _ => "—"
    };
}
