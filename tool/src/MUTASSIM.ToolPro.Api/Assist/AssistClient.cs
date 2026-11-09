using System.Net;
using System.Net.Http.Json;
using System.Text.Json;

namespace MUTASSIM.ToolPro.Api.Assist;

/// <summary>
/// HTTP client for MUTASSIM GSM CLOUD "Smart Assist". The server only returns action IDs; this class drops
/// any ID the tool does not know, so nothing unknown can ever reach the phone.
/// The API key (when the server requires one) is read from the MUTASSIM_API_KEY environment variable and is
/// never written to settings or logs.
/// </summary>
public sealed class AssistClient(HttpClient http)
{
    public const string ApiKeyHeader = "X-MUTASSIM-KEY";
    public const string ApiKeyEnvironmentVariable = "MUTASSIM_API_KEY";

    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);

    public static Uri ParseServerUrl(string? url)
    {
        if (string.IsNullOrWhiteSpace(url) ||
            !Uri.TryCreate(url.Trim(), UriKind.Absolute, out var uri) ||
            uri.Scheme is not ("http" or "https"))
            throw new AssistServerException("Invalid server address. Example: http://localhost:5179");

        return new Uri(uri.GetLeftPart(UriPartial.Authority) + "/");
    }

    /// <summary>Plain HTTP is only acceptable for this PC or the local network.</summary>
    public static bool IsInsecure(Uri server) =>
        server.Scheme == Uri.UriSchemeHttp && !server.IsLoopback && !IsPrivateNetwork(server.Host);

    public Task<AssistHealthDto> CheckAsync(Uri server, CancellationToken ct) =>
        SendAsync<AssistHealthDto>(HttpMethod.Get, server, "api/health", null, ct);

    public Task<List<AssistProblemDto>> GetProblemsAsync(Uri server, CancellationToken ct) =>
        SendAsync<List<AssistProblemDto>>(HttpMethod.Get, server, "api/assist/problems", null, ct);

    public async Task<AssistAnswer> DiagnoseAsync(Uri server, AssistRequestDto request, CancellationToken ct)
    {
        var response = await SendAsync<AssistResponseDto>(HttpMethod.Post, server, "api/assist/diagnose", request, ct)
            .ConfigureAwait(false);

        if (response.SessionId == Guid.Empty || response.Steps is null || response.Guidance is null)
            throw new AssistServerException("The server returned an incomplete answer.");

        var accepted = response.Steps
            .Where(s => AssistKnownActions.All.Contains(s.ActionId))
            .OrderBy(s => s.Order)
            .ToList();
        var rejected = response.Steps
            .Where(s => !AssistKnownActions.All.Contains(s.ActionId))
            .Select(s => s.ActionId)
            .ToList();

        return new AssistAnswer(response, accepted, rejected);
    }

    /// <summary>Reports a step result. Never throws: a lost report must not break the operation.</summary>
    public async Task<bool> SendFeedbackAsync(Uri server, AssistFeedbackDto feedback, CancellationToken ct)
    {
        try
        {
            var message = feedback.Message is { Length: > 4000 } m ? m[..4000] : feedback.Message;
            await SendAsync<JsonElement>(HttpMethod.Post, server, "api/assist/feedback", feedback with { Message = message }, ct)
                .ConfigureAwait(false);
            return true;
        }
        catch (AssistServerException)
        {
            return false;
        }
    }

    private async Task<T> SendAsync<T>(HttpMethod method, Uri server, string path, object? body, CancellationToken ct)
    {
        using var request = new HttpRequestMessage(method, new Uri(server, path));
        var key = Environment.GetEnvironmentVariable(ApiKeyEnvironmentVariable);
        if (!string.IsNullOrWhiteSpace(key))
            request.Headers.TryAddWithoutValidation(ApiKeyHeader, key);
        if (body is not null)
            request.Content = JsonContent.Create(body, body.GetType(), options: Json);

        HttpResponseMessage response;
        try
        {
            response = await http.SendAsync(request, ct).ConfigureAwait(false);
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException)
        {
            throw new AssistServerException(
                $"Cannot reach the server at {server}. Is MUTASSIM SERVER running? ({(ex is TaskCanceledException ? "timeout" : ex.Message)})", ex);
        }

        using (response)
        {
            var text = await response.Content.ReadAsStringAsync(ct).ConfigureAwait(false);

            if (response.StatusCode == HttpStatusCode.Unauthorized)
                throw new AssistServerException(
                    $"The server requires an API key. Set the {ApiKeyEnvironmentVariable} environment variable to the server's key and restart the tool.");

            if (!response.IsSuccessStatusCode)
                throw new AssistServerException($"Server error {(int)response.StatusCode}: {ServerMessage(text)}");

            if (string.IsNullOrWhiteSpace(text))
                throw new AssistServerException("The server returned an empty answer.");

            try
            {
                return JsonSerializer.Deserialize<T>(text, Json)
                       ?? throw new AssistServerException("The server returned an empty answer.");
            }
            catch (JsonException ex)
            {
                throw new AssistServerException("The server answer is not valid JSON (wrong address?).", ex);
            }
        }
    }

    private static string ServerMessage(string body)
    {
        try
        {
            using var doc = JsonDocument.Parse(body);
            if (doc.RootElement.ValueKind == JsonValueKind.Object &&
                doc.RootElement.TryGetProperty("message", out var m) && m.ValueKind == JsonValueKind.String)
                return m.GetString()!;
        }
        catch (JsonException)
        {
        }
        return body.Length > 200 ? body[..200] : body;
    }

    private static bool IsPrivateNetwork(string host)
    {
        if (!IPAddress.TryParse(host, out var ip)) return host.EndsWith(".local", StringComparison.OrdinalIgnoreCase);
        var b = ip.GetAddressBytes();
        return b.Length == 4 && (b[0] == 10 || (b[0] == 172 && b[1] is >= 16 and <= 31) || (b[0] == 192 && b[1] == 168));
    }
}
