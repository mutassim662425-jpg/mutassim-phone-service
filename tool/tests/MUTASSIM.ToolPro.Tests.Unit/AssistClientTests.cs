using System.Net;
using System.Text;
using MUTASSIM.ToolPro.Api.Assist;
using Xunit;

namespace MUTASSIM.ToolPro.Tests.Unit;

public class AssistClientTests
{
    private sealed class StubHandler(Func<HttpRequestMessage, HttpResponseMessage> respond) : HttpMessageHandler
    {
        public List<HttpRequestMessage> Requests { get; } = [];

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct)
        {
            Requests.Add(request);
            return Task.FromResult(respond(request));
        }
    }

    private static HttpResponseMessage Json(HttpStatusCode code, string body) =>
        new(code) { Content = new StringContent(body, Encoding.UTF8, "application/json") };

    private static readonly Uri Server = new("http://localhost:5179/");

    [Fact]
    public async Task Unknown_actions_from_the_server_are_dropped()
    {
        const string body = """
        {"sessionId":"6f1d2c1e-1111-4222-8333-944455556666","problem":"identify","modeGroup":"adb",
         "summaryEn":"s","summaryAr":"s",
         "steps":[{"order":2,"actionId":"adb.reboot.recovery","titleEn":"t","titleAr":"t","reasonEn":"r","reasonAr":"r","dangerous":false},
                  {"order":1,"actionId":"adb.info","titleEn":"t","titleAr":"t","reasonEn":"r","reasonAr":"r","dangerous":false},
                  {"order":3,"actionId":"shell rm -rf /","titleEn":"x","titleAr":"x","reasonEn":"x","reasonAr":"x","dangerous":false}],
         "guidance":[{"en":"g","ar":"g"}]}
        """;
        var client = new AssistClient(new HttpClient(new StubHandler(_ => Json(HttpStatusCode.OK, body))));

        var answer = await client.DiagnoseAsync(Server, new AssistRequestDto("TOOL-1", "identify", null, "en"), default);

        Assert.Equal(["adb.info", "adb.reboot.recovery"], answer.Steps.Select(s => s.ActionId));
        Assert.Equal(["shell rm -rf /"], answer.RejectedActionIds);
    }

    [Fact]
    public async Task Server_error_message_is_shown_without_stack_trace()
    {
        var client = new AssistClient(new HttpClient(new StubHandler(_ =>
            Json(HttpStatusCode.BadRequest, """{"success":false,"message":"Unknown problem"}"""))));

        var ex = await Assert.ThrowsAsync<AssistServerException>(() =>
            client.DiagnoseAsync(Server, new AssistRequestDto("TOOL-1", "x", null, null), default));
        Assert.Equal("Server error 400: Unknown problem", ex.Message);
    }

    [Fact]
    public async Task Unauthorized_explains_the_api_key()
    {
        var client = new AssistClient(new HttpClient(new StubHandler(_ => Json(HttpStatusCode.Unauthorized, "{}"))));
        var ex = await Assert.ThrowsAsync<AssistServerException>(() => client.CheckAsync(Server, default));
        Assert.Contains(AssistClient.ApiKeyEnvironmentVariable, ex.Message);
    }

    [Fact]
    public async Task Unreachable_server_gives_readable_error()
    {
        var client = new AssistClient(new HttpClient(new StubHandler(_ => throw new HttpRequestException("Connection refused"))));
        var ex = await Assert.ThrowsAsync<AssistServerException>(() => client.CheckAsync(Server, default));
        Assert.Contains("Cannot reach the server", ex.Message);
    }

    [Fact]
    public async Task Empty_or_html_answer_is_rejected()
    {
        var empty = new AssistClient(new HttpClient(new StubHandler(_ => Json(HttpStatusCode.OK, ""))));
        await Assert.ThrowsAsync<AssistServerException>(() => empty.CheckAsync(Server, default));

        var html = new AssistClient(new HttpClient(new StubHandler(_ =>
            new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent("<html>router login</html>") })));
        var ex = await Assert.ThrowsAsync<AssistServerException>(() => html.CheckAsync(Server, default));
        Assert.Contains("not valid JSON", ex.Message);
    }

    [Fact]
    public async Task Feedback_failure_never_throws()
    {
        var client = new AssistClient(new HttpClient(new StubHandler(_ => throw new HttpRequestException("down"))));
        Assert.False(await client.SendFeedbackAsync(Server, new AssistFeedbackDto(Guid.NewGuid(), "adb.info", true, "ok"), default));
    }

    [Fact]
    public async Task No_serial_is_sent()
    {
        string? sent = null;
        var handler = new StubHandler(r =>
        {
            sent = r.Content!.ReadAsStringAsync().Result;
            return Json(HttpStatusCode.BadRequest, "{}");
        });
        var client = new AssistClient(new HttpClient(handler));
        await Assert.ThrowsAsync<AssistServerException>(() => client.DiagnoseAsync(Server,
            new AssistRequestDto("TOOL-1", "identify",
                new AssistDeviceDto("Adb", "samsung", "samsung", "SM-A515F", "Exynos", "13", null), "en"), default));
        Assert.DoesNotContain("serial", sent!, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("\"mode\":\"Adb\"", sent);
    }

    [Theory]
    [InlineData("http://localhost:5179", false)]
    [InlineData("http://192.168.1.20:5179", false)]
    [InlineData("https://cloud.example.com", false)]
    [InlineData("http://cloud.example.com", true)]
    public void Insecure_detection(string url, bool insecure) =>
        Assert.Equal(insecure, AssistClient.IsInsecure(AssistClient.ParseServerUrl(url)));

    [Theory]
    [InlineData("")]
    [InlineData("localhost:5179")]
    [InlineData("ftp://x")]
    public void Bad_urls_are_rejected(string url) =>
        Assert.Throws<AssistServerException>(() => AssistClient.ParseServerUrl(url));
}
