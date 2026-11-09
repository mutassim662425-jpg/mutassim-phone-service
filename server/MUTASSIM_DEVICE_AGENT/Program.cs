using System.Net;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using MUTASSIM_DEVICE_AGENT;
using MUTASSIM_SERVER.Core.Commands;
using MUTASSIM_SERVER.Core.Devices;

// ==========================================
// MUTASSIM DEVICE AGENT V1.5
// Config (environment variables):
//   MUTASSIM_SERVER_URL  default http://localhost:5179
//   MUTASSIM_DEVICE_ID   default TEST-DEVICE-001 (this agent's ID)
//   MUTASSIM_API_KEY     must match the server's key when auth is enabled
//   MUTASSIM_ADB_PATH    optional full path to adb.exe (or its folder)
// ==========================================

const string AgentVersion = "1.5";
const string DefaultServerUrl = "http://localhost:5179";
const string DefaultDeviceId = "TEST-DEVICE-001";
const string ApiKeyHeader = "X-MUTASSIM-KEY";

var pollInterval = TimeSpan.FromSeconds(2);
var maxBackoff = TimeSpan.FromSeconds(30);

Console.Title = $"MUTASSIM DEVICE AGENT V{AgentVersion}";

var serverUrl = Environment.GetEnvironmentVariable("MUTASSIM_SERVER_URL") ?? DefaultServerUrl;
var deviceId = (Environment.GetEnvironmentVariable("MUTASSIM_DEVICE_ID") ?? DefaultDeviceId).Trim();
var apiKey = Environment.GetEnvironmentVariable("MUTASSIM_API_KEY");

if (!AllowedCommands.IsValidDeviceId(deviceId))
{
    Log("CONFIG ERROR", $"Invalid MUTASSIM_DEVICE_ID '{deviceId}' (1-64 chars: letters, digits, . _ : -)");
    return 2;
}

if (!Uri.TryCreate(serverUrl, UriKind.Absolute, out var serverUri))
{
    Log("CONFIG ERROR", $"Invalid MUTASSIM_SERVER_URL '{serverUrl}'");
    return 2;
}

if (serverUri.Scheme == Uri.UriSchemeHttp && !serverUri.IsLoopback)
    Log("WARNING", "Server URL is plain HTTP on a non-local address. Use HTTPS in production.");

using var http = new HttpClient
{
    BaseAddress = serverUri,
    Timeout = TimeSpan.FromSeconds(30)
};

if (!string.IsNullOrWhiteSpace(apiKey))
    http.DefaultRequestHeaders.Add(ApiKeyHeader, apiKey);

var jsonOptions = new JsonSerializerOptions(JsonSerializerDefaults.Web)
{
    Converters = { new JsonStringEnumConverter() } // accepts both "Running" and 1
};

var (initialAdb, _) = Adb.Resolve();

Console.WriteLine("======================================");
Console.WriteLine($"       MUTASSIM DEVICE AGENT V{AgentVersion}");
Console.WriteLine("======================================");
Console.WriteLine($"Device ID : {deviceId}");
Console.WriteLine($"Server    : {serverUri}");
Console.WriteLine($"API key   : {(string.IsNullOrWhiteSpace(apiKey) ? "not set" : "set")}");
Console.WriteLine($"ADB       : {initialAdb ?? "Not found (set MUTASSIM_ADB_PATH)"}");
Console.WriteLine("======================================");

using var cts = new CancellationTokenSource();
Console.CancelKeyPress += (_, e) =>
{
    e.Cancel = true;
    cts.Cancel();
};

// Commands this agent already executed (never execute twice) and results
// that still have to reach the server (never lose a result).
var executed = new HashSet<Guid>();
var executedOrder = new Queue<Guid>();
var undelivered = new Dictionary<Guid, CommandResult>();

try
{
    while (!cts.IsCancellationRequested)
    {
        await WaitForServerAsync(cts.Token);
        await FlushUndeliveredAsync(cts.Token);
        await PollLoopAsync(cts.Token); // returns when the connection is lost
    }
}
catch (OperationCanceledException) when (cts.IsCancellationRequested)
{
}

Console.WriteLine();
Log("STOPPED", "MUTASSIM DEVICE AGENT");
return 0;

// ==========================================
// CONNECTION
// ==========================================

async Task WaitForServerAsync(CancellationToken ct)
{
    var delay = pollInterval;
    var attempt = 0;

    while (true)
    {
        attempt++;
        string? problem;

        try
        {
            if (attempt == 1)
                Log("CONNECTING", "Checking server...");

            using var response = await http.GetAsync("/api/health", ct);
            var body = await response.Content.ReadAsStringAsync(ct);

            if (!response.IsSuccessStatusCode)
            {
                problem = $"health returned HTTP {(int)response.StatusCode}";
            }
            else
            {
                var health = string.IsNullOrWhiteSpace(body)
                    ? null
                    : JsonSerializer.Deserialize<HealthResponse>(body, jsonOptions);

                if (health?.Success == true)
                {
                    Log("CONNECTED", $"MUTASSIM SERVER ONLINE (v{health.Version ?? "?"})");
                    if (health.AuthRequired == true && string.IsNullOrWhiteSpace(apiKey))
                        Log("WARNING", "Server requires an API key but MUTASSIM_API_KEY is not set.");
                    Log("READY", "Agent is listening for commands.");
                    return;
                }

                problem = "unexpected health response";
            }
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException or JsonException)
        {
            problem = ex is TaskCanceledException ? "request timed out" : ex.Message;
        }

        // Log the first failure and then only every ~10 attempts (no error spam)
        if (attempt == 1 || attempt % 10 == 0)
            Log("OFFLINE", $"Server not reachable ({problem}). Retrying (next try in {delay.TotalSeconds:0}s)...");

        await Task.Delay(delay, ct);
        delay = TimeSpan.FromSeconds(Math.Min(delay.TotalSeconds * 2, maxBackoff.TotalSeconds));
    }
}

async Task PollLoopAsync(CancellationToken ct)
{
    while (!ct.IsCancellationRequested)
    {
        DeviceCommand? command;

        try
        {
            command = await PollOnceAsync(ct);
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException)
        {
            Log("DISCONNECTED", ex is TaskCanceledException ? "Poll timed out." : ex.Message);
            return; // back to WaitForServerAsync
        }

        if (command is null)
        {
            await Task.Delay(pollInterval, ct);
            continue;
        }

        var result = await ExecuteOnceAsync(command, ct);

        if (!await DeliverResultAsync(result, ct))
            return; // connection lost; result stays in 'undelivered'
    }
}

/// <summary>Returns the next command, or null when there is none (or the response was unusable).</summary>
async Task<DeviceCommand?> PollOnceAsync(CancellationToken ct)
{
    using var response = await http.GetAsync(
        $"/api/commands/next/{Uri.EscapeDataString(deviceId)}", ct);

    // 204 No Content = no command. This is the normal idle case; do NOT parse the empty body.
    if (response.StatusCode == HttpStatusCode.NoContent)
        return null;

    var body = await response.Content.ReadAsStringAsync(ct);

    if (response.StatusCode == HttpStatusCode.Unauthorized)
    {
        Log("AUTH ERROR", "Server rejected the API key. Set MUTASSIM_API_KEY to the server's key.");
        await Task.Delay(TimeSpan.FromSeconds(10), ct);
        return null;
    }

    if (!response.IsSuccessStatusCode)
    {
        Log("POLL ERROR", $"HTTP {(int)response.StatusCode}: {Truncate(body, 200)}");
        await Task.Delay(TimeSpan.FromSeconds(5), ct);
        return null;
    }

    if (string.IsNullOrWhiteSpace(body) || body.Trim() == "null")
        return null;

    var mediaType = response.Content.Headers.ContentType?.MediaType;
    if (mediaType is not null && !mediaType.Contains("json", StringComparison.OrdinalIgnoreCase))
    {
        Log("POLL ERROR", $"Expected JSON but got '{mediaType}' (wrong server URL?)");
        await Task.Delay(TimeSpan.FromSeconds(5), ct);
        return null;
    }

    DeviceCommand? command;
    try
    {
        command = JsonSerializer.Deserialize<DeviceCommand>(body, jsonOptions);
    }
    catch (JsonException ex)
    {
        Log("POLL ERROR", $"Invalid command JSON: {ex.Message}");
        await Task.Delay(TimeSpan.FromSeconds(5), ct);
        return null;
    }

    if (command is null || command.Id == Guid.Empty || string.IsNullOrWhiteSpace(command.Action))
    {
        Log("POLL ERROR", "Command response is missing Id/Action.");
        return null;
    }

    if (!string.Equals(command.DeviceId, deviceId, StringComparison.Ordinal))
    {
        Log("SECURITY", $"Ignoring command {command.Id} addressed to '{command.DeviceId}'.");
        return null;
    }

    return command;
}

// ==========================================
// COMMAND EXECUTION
// ==========================================

async Task<CommandResult> ExecuteOnceAsync(DeviceCommand command, CancellationToken ct)
{
    if (undelivered.TryGetValue(command.Id, out var pending))
    {
        Log("DUPLICATE", $"Command {command.Id} already executed; re-sending stored result.");
        return pending;
    }

    if (executed.Contains(command.Id))
    {
        Log("DUPLICATE", $"Command {command.Id} already executed; not running it again.");
        return new CommandResult(command.Id, false, "Duplicate delivery ignored by agent.", DateTime.UtcNow, deviceId);
    }

    RememberExecuted(command.Id);

    Console.WriteLine();
    Log("COMMAND", $"{command.Action}  [ID {command.Id}]");

    bool success;
    string message;

    try
    {
        var action = AllowedCommands.Normalize(command.Action);

        (success, message) = action switch
        {
            AllowedCommands.Ping => (true, $"MUTASSIM DEVICE AGENT {AgentVersion} ONLINE. Server connection successful."),
            AllowedCommands.DeviceScan => (true, await DeviceScanAsync(ct)),
            AllowedCommands.AdbInfo => (true, await AdbInfoAsync(ct)),
            _ => (false, "Unsupported command refused by agent: " + Truncate(command.Action, 64))
        };
    }
    catch (OperationCanceledException) when (ct.IsCancellationRequested)
    {
        throw;
    }
    catch (Exception ex)
    {
        success = false;
        message = ex.Message;
    }

    Log(success ? "SUCCESS" : "FAILED", success ? "Command completed." : message);

    var result = new CommandResult(command.Id, success, message, DateTime.UtcNow, deviceId);
    undelivered[command.Id] = result;
    return result;
}

void RememberExecuted(Guid id)
{
    executed.Add(id);
    executedOrder.Enqueue(id);
    while (executedOrder.Count > 5000)
        executed.Remove(executedOrder.Dequeue());
}

async Task<string> DeviceScanAsync(CancellationToken ct)
{
    var adb = Adb.RequirePath();
    var devices = await Adb.ListDevicesAsync(adb, ct);

    if (devices.Count == 0)
        return "No Android devices connected.";

    var sb = new StringBuilder();
    sb.AppendLine($"{devices.Count} device(s) found:");
    foreach (var d in devices)
    {
        sb.Append($"{d.Serial}\t{d.State}");
        var hint = Adb.StateHint(d.State);
        if (hint.Length > 0) sb.Append($"\t({hint})");
        sb.AppendLine();
    }
    return sb.ToString().TrimEnd();
}

async Task<string> AdbInfoAsync(CancellationToken ct)
{
    var adb = Adb.RequirePath();
    var devices = await Adb.ListDevicesAsync(adb, ct);

    if (devices.Count == 0)
        return "No Android devices connected.";

    var sb = new StringBuilder();

    foreach (var d in devices)
    {
        AndroidDeviceInfo info;
        try
        {
            info = await Adb.GetDeviceInfoAsync(adb, d, ct);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            info = new AndroidDeviceInfo(d.Serial, "error: " + ex.Message);
        }

        sb.AppendLine($"Serial: {info.Serial}");
        sb.AppendLine($"Connection: {info.ConnectionStatus}");
        if (info.ConnectionStatus == "device")
        {
            sb.AppendLine($"Brand: {info.Brand}");
            sb.AppendLine($"Manufacturer: {info.Manufacturer}");
            sb.AppendLine($"Model: {info.Model}");
            sb.AppendLine($"Android: {info.AndroidVersion}");
            sb.AppendLine($"SDK: {info.SdkVersion}");
            sb.AppendLine($"Build: {info.BuildNumber}");
        }
        else
        {
            var hint = Adb.StateHint(info.ConnectionStatus);
            if (hint.Length > 0) sb.AppendLine($"Hint: {hint}");
        }

        var reported = await SendDeviceReportAsync(info, ct);
        sb.AppendLine($"Report: {(reported ? "sent to server" : "NOT sent")}");
        sb.AppendLine("------------------------------");
    }

    return sb.ToString().TrimEnd();
}

async Task<bool> SendDeviceReportAsync(AndroidDeviceInfo info, CancellationToken ct)
{
    if (!AllowedCommands.IsValidDeviceId(info.Serial))
    {
        Log("REPORT", $"Skipping report: serial '{info.Serial}' has unsupported characters.");
        return false;
    }

    var report = new DeviceReport(
        info.Serial, info.Brand, info.Manufacturer, info.Model,
        info.AndroidVersion, info.SdkVersion, info.BuildNumber,
        info.ConnectionStatus, deviceId);

    try
    {
        using var response = await http.PostAsJsonAsync("/api/device/report", report, jsonOptions, ct);
        if (response.IsSuccessStatusCode)
        {
            Log("REPORT", $"Device report sent: {info.Serial} ({info.ConnectionStatus})");
            return true;
        }

        Log("REPORT", $"Server rejected report for {info.Serial}: HTTP {(int)response.StatusCode}");
        return false;
    }
    catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException && !ct.IsCancellationRequested)
    {
        Log("REPORT", $"Could not send report for {info.Serial}: {ex.Message}");
        return false;
    }
}

// ==========================================
// RESULT DELIVERY
// ==========================================

/// <summary>True when the server has the result (or can never accept it). False when the connection is down.</summary>
async Task<bool> DeliverResultAsync(CommandResult result, CancellationToken ct)
{
    for (var attempt = 1; attempt <= 3; attempt++)
    {
        try
        {
            using var response = await http.PostAsJsonAsync("/api/commands/result", result, jsonOptions, ct);

            if (response.IsSuccessStatusCode || response.StatusCode == HttpStatusCode.Conflict)
            {
                undelivered.Remove(result.CommandId);
                Log("SENT", "Result delivered to server.");
                return true;
            }

            var body = await response.Content.ReadAsStringAsync(ct);

            if (response.StatusCode is HttpStatusCode.NotFound or HttpStatusCode.BadRequest)
            {
                // Server no longer knows this command (e.g. it was restarted). Retrying can't help.
                undelivered.Remove(result.CommandId);
                Log("RESULT DROPPED", $"HTTP {(int)response.StatusCode}: {Truncate(body, 200)}");
                return true;
            }

            Log("RESULT ERROR", $"HTTP {(int)response.StatusCode}: {Truncate(body, 200)}");
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException)
        {
            Log("RESULT ERROR", $"Attempt {attempt}/3: {ex.Message}");
        }

        await Task.Delay(TimeSpan.FromSeconds(attempt * 2), ct);
    }

    Log("RESULT PENDING", $"Result {result.CommandId} kept; will resend after reconnect.");
    return false;
}

async Task FlushUndeliveredAsync(CancellationToken ct)
{
    foreach (var result in undelivered.Values.ToList())
    {
        Log("RESEND", $"Resending stored result {result.CommandId}");
        if (!await DeliverResultAsync(result, ct))
            return;
    }
}

// ==========================================
// HELPERS
// ==========================================

static void Log(string tag, string message) =>
    Console.WriteLine($"{DateTime.Now:HH:mm:ss} [{tag}] {message}");

static string Truncate(string? value, int max) =>
    string.IsNullOrEmpty(value) ? "" : value.Length <= max ? value : value[..max] + "...";

// ==========================================
// DATA MODELS (agent-only; shared models come from MUTASSIM_SERVER.Core)
// ==========================================

public sealed record HealthResponse(
    bool Success,
    string? Server = null,
    string? Version = null,
    string? Status = null,
    bool? AuthRequired = null
);
