using System.Security.Cryptography;
using System.Text;
using MUTASSIM_SERVER;
using MUTASSIM_SERVER.Core.Assist;
using MUTASSIM_SERVER.Core.Billing;
using MUTASSIM_SERVER.Core.Commands;
using MUTASSIM_SERVER.Core.Devices;

var builder = WebApplication.CreateBuilder(args);

// ======================================
// MUTASSIM GSM CLOUD - SERVER V1.6
// ======================================

const string ServerVersion = "1.7";
const string ApiKeyHeader = "X-MUTASSIM-KEY";
const int MaxResultMessageLength = 64 * 1024;

builder.Services.AddSingleton<CommandQueue>();
builder.Services.AddSingleton<DeviceRegistry>();
builder.Services.AddSingleton<AssistSessionStore>();

builder.Services.AddEndpointsApiExplorer();
builder.Services.AddSwaggerGen();

var app = builder.Build();

// ======================================
// API KEY (never stored in code)
// Set environment variable MUTASSIM_API_KEY, or Security:ApiKey via
// `dotnet user-secrets`. Required outside Development.
// ======================================

var apiKey =
    app.Configuration["MUTASSIM_API_KEY"] ??
    app.Configuration["Security:ApiKey"];

if (string.IsNullOrWhiteSpace(apiKey))
{
    if (!app.Environment.IsDevelopment())
    {
        throw new InvalidOperationException(
            "MUTASSIM_API_KEY is not configured. Refusing to start outside Development without authentication.");
    }

    app.Logger.LogWarning(
        "MUTASSIM_API_KEY is not set: API authentication is DISABLED (Development only).");
    apiKey = null;
}
else
{
    app.Logger.LogInformation("API key authentication is enabled.");
}

var apiKeyBytes = apiKey is null ? null : Encoding.UTF8.GetBytes(apiKey);

// ======================================
// ERROR HANDLING: always JSON, never a stack trace to the client
// ======================================

app.Use(async (context, next) =>
{
    try
    {
        await next();
    }
    catch (BadHttpRequestException ex)
    {
        app.Logger.LogWarning("Bad request {Method} {Path}: {Message}",
            context.Request.Method, context.Request.Path, ex.Message);

        if (!context.Response.HasStarted)
        {
            context.Response.Clear();
            context.Response.StatusCode = ex.StatusCode;
            await context.Response.WriteAsJsonAsync(new
            {
                success = false,
                message = "Invalid or missing JSON request body"
            });
        }
    }
    catch (Exception ex) when (!context.RequestAborted.IsCancellationRequested)
    {
        app.Logger.LogError(ex, "Unhandled error on {Method} {Path}",
            context.Request.Method, context.Request.Path);

        if (!context.Response.HasStarted)
        {
            context.Response.Clear();
            context.Response.StatusCode = StatusCodes.Status500InternalServerError;
            await context.Response.WriteAsJsonAsync(new
            {
                success = false,
                message = "Internal server error"
            });
        }
    }
});

// ======================================
// HTTPS (production only; localhost dev stays on http)
// ======================================

if (!app.Environment.IsDevelopment())
{
    app.UseHsts();
    app.UseHttpsRedirection();
}

// ======================================
// SWAGGER
// ======================================

if (app.Environment.IsDevelopment())
{
    app.UseSwagger();
    app.UseSwaggerUI();
}

// ======================================
// AUTHENTICATION for /api/* (except health)
// ======================================

app.Use(async (context, next) =>
{
    var path = context.Request.Path;

    // The agent shared key (X-MUTASSIM-KEY) guards the device/agent API. The human-facing billing API
    // (login, accounts, credits, operations) uses its own Bearer session tokens instead, so it is exempt here.
    var isBilling =
        path.StartsWithSegments("/api/auth") || path.StartsWithSegments("/api/accounts") ||
        path.StartsWithSegments("/api/credits") || path.StartsWithSegments("/api/operations") ||
        path.StartsWithSegments("/api/pricing");

    if (apiKeyBytes is not null &&
        path.StartsWithSegments("/api") &&
        !path.StartsWithSegments("/api/health") &&
        !isBilling)
    {
        var supplied = context.Request.Headers[ApiKeyHeader].ToString();
        var suppliedBytes = Encoding.UTF8.GetBytes(supplied);

        if (suppliedBytes.Length != apiKeyBytes.Length ||
            !CryptographicOperations.FixedTimeEquals(suppliedBytes, apiKeyBytes))
        {
            app.Logger.LogWarning("Rejected unauthenticated request {Method} {Path} from {Ip}",
                context.Request.Method, path, context.Connection.RemoteIpAddress);

            context.Response.StatusCode = StatusCodes.Status401Unauthorized;
            await context.Response.WriteAsJsonAsync(new
            {
                success = false,
                message = $"Missing or invalid {ApiKeyHeader} header"
            });
            return;
        }
    }

    await next();
});

// ======================================
// SERVER HEALTH
// ======================================

app.MapGet("/api/health", () =>
{
    return Results.Ok(new
    {
        success = true,
        server = "MUTASSIM GSM CLOUD",
        version = ServerVersion,
        status = "ONLINE",
        authRequired = apiKeyBytes is not null,
        time = DateTime.UtcNow
    });
});

// ======================================
// CREATE COMMAND
// ======================================

app.MapPost("/api/commands",
    (CreateCommandRequest? request, CommandQueue queue) =>
{
    if (request is null ||
        string.IsNullOrWhiteSpace(request.DeviceId) ||
        string.IsNullOrWhiteSpace(request.Action))
    {
        return Results.BadRequest(new
        {
            success = false,
            message = "DeviceId and Action are required"
        });
    }

    var deviceId = request.DeviceId.Trim();

    if (!AllowedCommands.IsValidDeviceId(deviceId))
    {
        return Results.BadRequest(new
        {
            success = false,
            message = "Invalid DeviceId (1-64 chars: letters, digits, . _ : -)"
        });
    }

    var action = AllowedCommands.Normalize(request.Action);

    if (action is null)
    {
        app.Logger.LogWarning("Rejected unknown command '{Action}' for {DeviceId}",
            request.Action, deviceId);

        return Results.BadRequest(new
        {
            success = false,
            message = "Unknown command. Allowed: " + string.Join(", ", AllowedCommands.All)
        });
    }

    var command = queue.Add(deviceId, action);

    app.Logger.LogInformation("Command queued {CommandId} {Action} -> {DeviceId}",
        command.Id, command.Action, command.DeviceId);

    return Results.Ok(new
    {
        success = true,
        message = "Command queued successfully",
        command
    });
});

// ======================================
// GET NEXT COMMAND
// ======================================

app.MapGet("/api/commands/next/{deviceId}",
    (string deviceId, CommandQueue queue, DeviceRegistry registry) =>
{
    if (!AllowedCommands.IsValidDeviceId(deviceId))
    {
        return Results.BadRequest(new
        {
            success = false,
            message = "Invalid Device ID"
        });
    }

    registry.TouchAgent(deviceId);

    var command = queue.GetNext(deviceId);

    // No command: HTTP 204 with an empty body (the agent checks the status code).
    if (command is null)
        return Results.NoContent();

    app.Logger.LogInformation("Command dispatched {CommandId} {Action} -> {DeviceId}",
        command.Id, command.Action, command.DeviceId);

    return Results.Ok(command);
});

// ======================================
// SUBMIT COMMAND RESULT
// ======================================

app.MapPost("/api/commands/result",
    (CommandResult? result, CommandQueue queue) =>
{
    if (result is null || result.CommandId == Guid.Empty)
    {
        return Results.BadRequest(new
        {
            success = false,
            message = "CommandId is required"
        });
    }

    var message = result.Message ?? "";
    if (message.Length > MaxResultMessageLength)
        message = message[..MaxResultMessageLength] + "\n[truncated]";

    var outcome = queue.Complete(result with { Message = message });

    switch (outcome)
    {
        case CompleteOutcome.Completed:
            app.Logger.LogInformation("Command result {CommandId} success={Success}",
                result.CommandId, result.Success);
            return Results.Ok(new
            {
                success = true,
                message = "Command result received",
                commandId = result.CommandId
            });

        case CompleteOutcome.AlreadyCompleted:
            return Results.Conflict(new
            {
                success = false,
                message = "Result already received for this command",
                commandId = result.CommandId
            });

        case CompleteOutcome.NotRunning:
            return Results.Conflict(new
            {
                success = false,
                message = "Command was not dispatched to an agent yet",
                commandId = result.CommandId
            });

        case CompleteOutcome.WrongDevice:
            app.Logger.LogWarning("Result for {CommandId} came from wrong device {DeviceId}",
                result.CommandId, result.DeviceId);
            return Results.BadRequest(new
            {
                success = false,
                message = "Command does not belong to this device"
            });

        default:
            return Results.NotFound(new
            {
                success = false,
                message = "Command not found"
            });
    }
});

// ======================================
// COMMAND STATUS / RESULTS (read-only)
// ======================================

app.MapGet("/api/commands/{commandId:guid}",
    (Guid commandId, CommandQueue queue) =>
{
    var record = queue.Get(commandId);
    return record is null
        ? Results.NotFound(new { success = false, message = "Command not found" })
        : Results.Ok(record);
});

app.MapGet("/api/commands",
    (string? deviceId, CommandQueue queue) => Results.Ok(queue.List(deviceId)));

// ======================================
// DEVICE REPORT
// ======================================

app.MapPost("/api/device/report",
    (DeviceReport? report, DeviceRegistry registry) =>
{
    if (report is null || !AllowedCommands.IsValidDeviceId(report.DeviceId))
    {
        return Results.BadRequest(new
        {
            success = false,
            message = "Valid Device ID is required"
        });
    }

    if (report.AgentId is not null && !AllowedCommands.IsValidDeviceId(report.AgentId))
    {
        return Results.BadRequest(new
        {
            success = false,
            message = "Invalid Agent ID"
        });
    }

    var entry = registry.Upsert(report);

    app.Logger.LogInformation(
        "Device report: {DeviceId} | {Brand} | {Model} | Android {Android} | {Connection} | agent {AgentId}",
        report.DeviceId, report.Brand, report.Model, report.Android, report.Connection, report.AgentId);

    return Results.Ok(new
    {
        success = true,
        message = "Device report received",
        deviceId = report.DeviceId,
        receivedAt = entry.ReceivedAt
    });
});

app.MapGet("/api/devices", (DeviceRegistry registry) => Results.Ok(registry.Devices()));

app.MapGet("/api/agents", (DeviceRegistry registry) => Results.Ok(registry.Agents()));

// ======================================
// ASSIST (MUTASSIM TOOL PRO ⇄ SERVER)
// The tool sends the problem + device state, the server answers with
// allow-listed steps that the tool runs with its own built-in functions.
// ======================================

app.MapGet("/api/assist/problems", () => Results.Ok(SolutionEngine.Problems));

app.MapGet("/api/assist/actions", () => Results.Ok(AssistActions.All.Values));

app.MapPost("/api/assist/diagnose",
    (AssistRequest? request, AssistSessionStore sessions) =>
{
    if (request is null || !AllowedCommands.IsValidDeviceId(request.ClientId))
    {
        return Results.BadRequest(new
        {
            success = false,
            message = "Valid ClientId is required (1-64 chars: letters, digits, . _ : -)"
        });
    }

    if (!SolutionEngine.IsKnownProblem(request.Problem))
    {
        return Results.BadRequest(new
        {
            success = false,
            message = "Unknown problem. Allowed: " + string.Join(", ", SolutionEngine.Problems.Select(p => p.Code))
        });
    }

    var device = request.Device;
    if (device is not null && new[] { device.Mode, device.Brand, device.Manufacturer, device.Model,
            device.Chipset, device.AndroidVersion, device.BootloaderState }.Any(v => v is { Length: > 128 }))
    {
        return Results.BadRequest(new { success = false, message = "Device field too long" });
    }

    var response = SolutionEngine.Diagnose(Guid.NewGuid(), request.Problem, device);
    sessions.Add(request, response);

    app.Logger.LogInformation(
        "Assist {SessionId}: {Problem} | {Brand} {Model} | mode {Mode} → {Steps} step(s) for client {ClientId}",
        response.SessionId, request.Problem, device?.Brand, device?.Model, device?.Mode,
        response.Steps.Count, request.ClientId);

    return Results.Ok(response);
});

app.MapPost("/api/assist/feedback",
    (AssistFeedback? feedback, AssistSessionStore sessions) =>
{
    if (feedback is null || feedback.SessionId == Guid.Empty || string.IsNullOrWhiteSpace(feedback.ActionId))
        return Results.BadRequest(new { success = false, message = "SessionId and ActionId are required" });

    var outcome = sessions.AddFeedback(feedback);

    switch (outcome)
    {
        case AssistSessionStore.FeedbackOutcome.Recorded:
            app.Logger.LogInformation("Assist {SessionId}: {ActionId} success={Success}",
                feedback.SessionId, feedback.ActionId, feedback.Success);
            return Results.Ok(new { success = true, message = "Feedback recorded" });

        case AssistSessionStore.FeedbackOutcome.ActionNotInSession:
            return Results.BadRequest(new { success = false, message = "Action was not part of this session" });

        default:
            return Results.NotFound(new { success = false, message = "Session not found" });
    }
});

app.MapGet("/api/assist/sessions/{sessionId:guid}",
    (Guid sessionId, AssistSessionStore sessions) =>
        sessions.Get(sessionId) is { } r
            ? Results.Ok(r)
            : Results.NotFound(new { success = false, message = "Session not found" }));

app.MapGet("/api/assist/sessions", (AssistSessionStore sessions) => Results.Ok(sessions.Recent()));

// ======================================
// BILLING (accounts, credits, operations) — SQLite-backed
// ======================================
// DB path: MUTASSIM_DB_PATH, else "mutassim.db" next to the server.
// First admin: set MUTASSIM_ADMIN_USER + MUTASSIM_ADMIN_PASS once to create it.

var dbPath = app.Configuration["MUTASSIM_DB_PATH"]
    ?? Path.Combine(AppContext.BaseDirectory, "mutassim.db");
var billing = await BillingStore.OpenFileAsync(dbPath);
app.Logger.LogInformation("Billing database: {Path}", dbPath);

var adminUser = app.Configuration["MUTASSIM_ADMIN_USER"];
var adminPass = app.Configuration["MUTASSIM_ADMIN_PASS"];
if (!string.IsNullOrWhiteSpace(adminUser) && !string.IsNullOrWhiteSpace(adminPass))
{
    if (await billing.EnsureAdminAsync(adminUser, adminPass))
        app.Logger.LogInformation("Admin account '{User}' created.", adminUser);
    else
        app.Logger.LogInformation("An admin already exists; MUTASSIM_ADMIN_USER was ignored.");
}

app.MapBilling(billing);

app.Lifetime.ApplicationStopping.Register(() => billing.DisposeAsync().AsTask().GetAwaiter().GetResult());

// ======================================
// ROOT
// ======================================

app.MapGet("/", () =>
{
    return Results.Ok(new
    {
        application = "MUTASSIM GSM CLOUD",
        version = ServerVersion,
        status = "RUNNING",
        health = "/api/health",
        swagger = "/swagger"
    });
});

// ======================================
// START SERVER
// ======================================

app.Logger.LogInformation("Starting MUTASSIM GSM CLOUD SERVER {Version}...", ServerVersion);

app.Run();
