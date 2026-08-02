using System.Net;
using Hms.Notifications;

namespace Hms.Web.Tests;

/// <summary>
/// Spec 0039 WP6 (AUD-M20-01): gateway selection and the delivery verdict. The audit's finding
/// was that "live" mode silently stopped SMS; these tests pin the two facts that fix it —
/// live selects an HTTP gateway, and only a gateway success may report Sent.
/// </summary>
public class SmsGatewayTests
{
    private static readonly SmsGatewaySettings Settings =
        new("http://localhost:9/send", "key-123", "HOSPITAL");

    /// <summary>Scripted transport: answers with the canned status and records the request.</summary>
    private sealed class FakeHandler(HttpStatusCode status) : HttpMessageHandler
    {
        public HttpRequestMessage? Seen;
        public string? SeenBody;

        protected override async Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request, CancellationToken ct)
        {
            Seen = request;
            SeenBody = request.Content is null ? null : await request.Content.ReadAsStringAsync(ct);
            return new HttpResponseMessage(status);
        }
    }

    private sealed class ThrowingHandler : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request, CancellationToken ct)
            => throw new HttpRequestException("connection refused");
    }

    [Fact]
    public void Default_mode_selects_simulation_and_live_selects_http()
    {
        Assert.IsType<SimulatedSmsGateway>(SmsGateway.Create(SmsOptions.From(null), Settings));
        Assert.IsType<SimulatedSmsGateway>(SmsGateway.Create(SmsOptions.From("sim"), Settings));
        Assert.IsType<HttpSmsGateway>(SmsGateway.Create(SmsOptions.From("live"), Settings));
        Assert.IsType<HttpSmsGateway>(SmsGateway.Create(SmsOptions.From("LIVE"), Settings));
    }

    [Fact]
    public async Task Simulated_gateway_always_accepts()
    {
        var verdict = await new SimulatedSmsGateway().SendAsync("01712345678", "hello");
        Assert.True(verdict.Sent);
        Assert.Null(verdict.FailReason);
    }

    [Fact]
    public async Task Url_template_substitutes_and_url_encodes_to_and_message()
    {
        var handler = new FakeHandler(HttpStatusCode.OK);
        var gateway = new HttpSmsGateway(
            new SmsGatewaySettings("http://localhost:9/send?apikey={apikey}&to={to}&msg={message}",
                "key 123", "HOSP"),
            new HttpClient(handler));

        var verdict = await gateway.SendAsync("+8801712345678", "Due: 500 Tk & thanks");

        Assert.True(verdict.Sent);
        Assert.Equal(HttpMethod.Get, handler.Seen!.Method);
        var uri = handler.Seen.RequestUri!.OriginalString;
        Assert.Contains("to=%2B8801712345678", uri);          // '+' must survive as %2B
        Assert.Contains("apikey=key+123", uri);
        Assert.DoesNotContain("{to}", uri);
        Assert.DoesNotContain("{message}", uri);
    }

    [Fact]
    public async Task Without_placeholders_the_gateway_form_posts()
    {
        var handler = new FakeHandler(HttpStatusCode.OK);
        var gateway = new HttpSmsGateway(Settings, new HttpClient(handler));

        var verdict = await gateway.SendAsync("01712345678", "hello there");

        Assert.True(verdict.Sent);
        Assert.Equal(HttpMethod.Post, handler.Seen!.Method);
        Assert.Contains("to=01712345678", handler.SeenBody);
        Assert.Contains("message=hello+there", handler.SeenBody);
        Assert.Contains("api_key=key-123", handler.SeenBody);
        Assert.Contains("sender_id=HOSPITAL", handler.SeenBody);
    }

    [Fact]
    public async Task A_non_2xx_answer_is_a_failure_with_the_status_in_the_reason()
    {
        var gateway = new HttpSmsGateway(Settings, new HttpClient(new FakeHandler(HttpStatusCode.InternalServerError)));
        var verdict = await gateway.SendAsync("01712345678", "hello");
        Assert.False(verdict.Sent);
        Assert.Contains("500", verdict.FailReason);
    }

    [Fact]
    public async Task An_unreachable_gateway_is_a_failure_never_an_exception()
    {
        var gateway = new HttpSmsGateway(Settings, new HttpClient(new ThrowingHandler()));
        var verdict = await gateway.SendAsync("01712345678", "hello");
        Assert.False(verdict.Sent);
        Assert.Contains("unreachable", verdict.FailReason, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task A_missing_gateway_url_fails_with_the_config_key_in_the_reason()
    {
        var gateway = new HttpSmsGateway(new SmsGatewaySettings(null, null, null),
            new HttpClient(new FakeHandler(HttpStatusCode.OK)));
        var verdict = await gateway.SendAsync("01712345678", "hello");
        Assert.False(verdict.Sent);
        Assert.Contains("Sms:GatewayUrl", verdict.FailReason);
    }
}
