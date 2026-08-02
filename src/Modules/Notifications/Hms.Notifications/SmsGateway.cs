using System.Net;

namespace Hms.Notifications;

/// <summary>The outcome of one delivery attempt. <c>FailReason</c> is operator-readable — it
/// lands in the tray's send log (§5 M20 [M] "send log with delivery status").</summary>
public sealed record SmsSendResult(bool Sent, string? FailReason = null);

/// <summary>
/// Spec 0039 WP6 (AUD-M20-01): the pluggable delivery seam. Simulation is the shipped default
/// (edge 3); live delivery is an explicit configuration choice (`HMS_SMS_MODE=live` plus
/// `Sms:GatewayUrl`). The queue/drain code depends only on this interface, so the state machine
/// (queued → sent/failed) is identical in both modes.
/// </summary>
public interface ISmsGateway
{
    Task<SmsSendResult> SendAsync(string recipient, string body, CancellationToken ct = default);
}

/// <summary>Gateway wiring per §5 M20 [M] "configurable SMS gateway (local aggregator)" / I7.
/// All three values come from configuration — no vendor is hardcoded, and no external host
/// appears in code (edge 1). The URL may carry <c>{to}</c>/<c>{message}</c> (and optionally
/// <c>{apikey}</c>/<c>{sender}</c>) placeholders; without placeholders the gateway form-posts.</summary>
public sealed record SmsGatewaySettings(string? GatewayUrl, string? ApiKey, string? SenderId);

/// <summary>Edge 3: the demo gateway. Every attempt succeeds instantly; the tray is the SIM.</summary>
public sealed class SimulatedSmsGateway : ISmsGateway
{
    public Task<SmsSendResult> SendAsync(string recipient, string body, CancellationToken ct = default)
        => Task.FromResult(new SmsSendResult(true));
}

/// <summary>
/// Live delivery over HTTP in the shape Bangladeshi aggregators use: either a GET on a URL
/// template with placeholders, or a form POST of <c>to</c>/<c>message</c> (+ key and sender id
/// when configured). Any 2xx answer counts as accepted; anything else — including a missing
/// URL — is a failed attempt with a reason, never an exception. The drain loop owns retries.
/// </summary>
public sealed class HttpSmsGateway(SmsGatewaySettings settings, HttpClient http) : ISmsGateway
{
    public async Task<SmsSendResult> SendAsync(string recipient, string body, CancellationToken ct = default)
    {
        var url = settings.GatewayUrl;
        if (string.IsNullOrWhiteSpace(url))
            return new SmsSendResult(false,
                "SMS gateway URL is not configured (Sms:GatewayUrl) — message kept for retry.");

        try
        {
            using var response = await (HasPlaceholders(url)
                ? http.GetAsync(Substitute(url, recipient, body), ct)
                : PostFormAsync(url, recipient, body, ct));

            return response.IsSuccessStatusCode
                ? new SmsSendResult(true)
                : new SmsSendResult(false, $"Gateway answered HTTP {(int)response.StatusCode}.");
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested)
        {
            throw;   // host shutdown, not a delivery verdict
        }
        catch (Exception e) when (e is HttpRequestException or TaskCanceledException or UriFormatException)
        {
            return new SmsSendResult(false, $"Gateway unreachable: {e.Message}");
        }
    }

    private static bool HasPlaceholders(string url)
        => url.Contains("{to}", StringComparison.OrdinalIgnoreCase)
           || url.Contains("{message}", StringComparison.OrdinalIgnoreCase);

    private string Substitute(string url, string recipient, string body) => url
        .Replace("{to}", WebUtility.UrlEncode(recipient), StringComparison.OrdinalIgnoreCase)
        .Replace("{message}", WebUtility.UrlEncode(body), StringComparison.OrdinalIgnoreCase)
        .Replace("{apikey}", WebUtility.UrlEncode(settings.ApiKey ?? ""), StringComparison.OrdinalIgnoreCase)
        .Replace("{sender}", WebUtility.UrlEncode(settings.SenderId ?? ""), StringComparison.OrdinalIgnoreCase);

    private Task<HttpResponseMessage> PostFormAsync(
        string url, string recipient, string body, CancellationToken ct)
    {
        var fields = new Dictionary<string, string> { ["to"] = recipient, ["message"] = body };
        if (!string.IsNullOrWhiteSpace(settings.ApiKey)) fields["api_key"] = settings.ApiKey;
        if (!string.IsNullOrWhiteSpace(settings.SenderId)) fields["sender_id"] = settings.SenderId;
        return http.PostAsync(url, new FormUrlEncodedContent(fields), ct);
    }
}

/// <summary>Selects the gateway for the configured posture. Simulation unless the deployment
/// explicitly asked for live (AUD-M20-01: live must mean delivery, not silence).</summary>
public static class SmsGateway
{
    public static ISmsGateway Create(
        SmsOptions options, SmsGatewaySettings settings, HttpMessageHandler? handler = null)
        => options.Simulated
            ? new SimulatedSmsGateway()
            : new HttpSmsGateway(settings,
                handler is null
                    ? new HttpClient { Timeout = TimeSpan.FromSeconds(15) }
                    : new HttpClient(handler) { Timeout = TimeSpan.FromSeconds(15) });
}
