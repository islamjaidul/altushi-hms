using System.Text;
using Hms.Kernel.Time;
using Microsoft.AspNetCore.Http;

namespace Hms.Shell.Reporting;

/// <summary>
/// The period's edge: query string in, canonical URL out, and a cookie so a selection survives
/// walking from a report to a dashboard to the log.
/// <para>
/// <b>The URL is truth; the cookie is only memory.</b> A request that names a period gets that
/// period, always. A request that names none is redirected to the canonical URL for the remembered
/// one — and that redirect is the whole trick. Without it two people opening the same link would see
/// different data, which is not a shareable link; with it the address bar always states the period,
/// which is §12.3's "it is shown, never silently reused".
/// </para>
/// <para>
/// Session is deliberately not used: it is in-process memory on a 3 GB box, does not survive a
/// restart, and cannot be shared. Local storage cannot work either — the server must resolve the
/// period before it can query.
/// </para>
/// </summary>
public static class PeriodBinding
{
    public const string CookieName = "hms.period";

    private const string Grain = "p";
    private const string Anchor = "d";
    private const string Units = "u";
    private const string From = "from";
    private const string To = "to";
    private const string Compare = "cmp";
    private const string Preset = "preset";
    private const string Step = "step";

    /// <summary>Bounded so a hostile cookie cannot become a header-size problem (spec 0039 WP1.3).</summary>
    private const int MaxCookieLength = 64;

    public static PeriodQuery Read(HttpRequest request) => new(
        Grain: Str(request, Grain),
        Anchor: Str(request, Anchor),
        From: Str(request, From),
        To: Str(request, To),
        Preset: Str(request, Preset),
        Compare: Str(request, Compare),
        Units: Int(request, Units, 1),
        Step: Int(request, Step, 0));

    /// <summary>True when the URL says anything at all about the period.</summary>
    public static bool HasPeriod(HttpRequest request) =>
        request.Query.Keys.Any(k => FilterSet.PeriodKeys.Contains(k)
                                    && !string.Equals(k, "run", StringComparison.OrdinalIgnoreCase)
                                    && !string.IsNullOrWhiteSpace(request.Query[k]));

    public static Period Resolve(HttpRequest request, DateOnly today, PeriodCalendar calendar,
        PeriodGrain fallback = PeriodGrain.Month)
        => PeriodResolver.Resolve(Read(request), today, calendar, fallback);

    /// <summary>
    /// Whether this request should be redirected to a remembered period. Only ever true for a GET
    /// that carries no period at all and has a cookie that parses — so the redirect cannot loop,
    /// because its target always carries <c>p=</c>.
    /// </summary>
    public static bool ShouldRestore(HttpRequest request, out PeriodQuery remembered)
    {
        remembered = default;
        if (!HttpMethods.IsGet(request.Method)) return false;
        if (HasPeriod(request)) return false;

        var raw = request.Cookies[CookieName];
        if (string.IsNullOrWhiteSpace(raw) || raw.Length > MaxCookieLength) return false;

        remembered = ParseCookie(raw);
        return remembered.Grain is not null || remembered.Preset is not null;
    }

    /// <summary>
    /// This request's path with the canonical period on it, keeping every other parameter. The
    /// period is written in its resolved, concrete form — never as a preset — so the link means the
    /// same thing next week.
    /// </summary>
    public static string CanonicalUrl(HttpRequest request, Period period)
    {
        var kept = request.Query
            .Where(kv => !FilterSet.PeriodKeys.Contains(kv.Key))
            .SelectMany(kv => kv.Value, (kv, v) => new KeyValuePair<string, string>(kv.Key, v ?? ""));
        return request.Path + Query(PeriodPairs(period).Concat(kept));
    }

    /// <summary>A link to this screen with a different period, keeping the filters.</summary>
    public static string UrlFor(HttpRequest request, Period period,
        IEnumerable<KeyValuePair<string, string>>? extra = null)
    {
        var kept = request.Query
            .Where(kv => !FilterSet.PeriodKeys.Contains(kv.Key)
                         && !string.Equals(kv.Key, "handler", StringComparison.OrdinalIgnoreCase))
            .SelectMany(kv => kv.Value, (kv, v) => new KeyValuePair<string, string>(kv.Key, v ?? ""));
        var pairs = PeriodPairs(period).Concat(kept);
        if (extra is not null) pairs = pairs.Concat(extra);
        return request.Path + Query(pairs);
    }

    /// <summary>A preset link — the server resolves it and redirects to the concrete form.</summary>
    public static string PresetUrl(HttpRequest request, string presetKey)
    {
        var kept = request.Query
            .Where(kv => !FilterSet.PeriodKeys.Contains(kv.Key)
                         && !string.Equals(kv.Key, "handler", StringComparison.OrdinalIgnoreCase))
            .SelectMany(kv => kv.Value, (kv, v) => new KeyValuePair<string, string>(kv.Key, v ?? ""));
        return request.Path + Query(new[] { new KeyValuePair<string, string>(Preset, presetKey) }.Concat(kept));
    }

    /// <summary>The period as query pairs — the canonical serialisation, and it round-trips.</summary>
    public static IEnumerable<KeyValuePair<string, string>> PeriodPairs(Period period)
    {
        var q = PeriodResolver.ToQuery(period);
        if (q.Grain is { } g) yield return new(Grain, g);
        if (q.Anchor is { } a) yield return new(Anchor, a);
        if (q.From is { } f) yield return new(From, f);
        if (q.To is { } t) yield return new(To, t);
        if (q.Units > 1) yield return new(Units, q.Units.ToString());
        if (q.Compare is { } c) yield return new(Compare, c);
    }

    /// <summary>
    /// Remembers the selection for the next screen. Stores the <i>preset</i> where there was one, so
    /// an operator who left on "This month" and returns in September gets September rather than the
    /// month they happened to be looking at.
    /// </summary>
    public static void Remember(HttpResponse response, Period period)
    {
        var value = period.Preset.Length > 0
            ? $"{Preset}={period.Preset}"
            : string.Join('&', PeriodPairs(period).Select(p => $"{p.Key}={p.Value}"));

        if (value.Length > MaxCookieLength) return;

        response.Cookies.Append(CookieName, value, new CookieOptions
        {
            HttpOnly = true,
            IsEssential = true,
            SameSite = SameSiteMode.Lax,
            Secure = response.HttpContext.Request.IsHttps,
            Expires = DateTimeOffset.UtcNow.AddDays(30),
            Path = "/",
        });
    }

    public static void Forget(HttpResponse response) => response.Cookies.Delete(CookieName);

    // ---- internals ----------------------------------------------------------------------------

    private static PeriodQuery ParseCookie(string raw)
    {
        string? grain = null, anchor = null, from = null, to = null, preset = null, cmp = null;
        var units = 1;

        foreach (var part in raw.Split('&', StringSplitOptions.RemoveEmptyEntries))
        {
            var eq = part.IndexOf('=');
            if (eq <= 0) continue;
            var key = part[..eq];
            var value = Uri.UnescapeDataString(part[(eq + 1)..]);
            switch (key)
            {
                case Grain: grain = value; break;
                case Anchor: anchor = value; break;
                case From: from = value; break;
                case To: to = value; break;
                case Preset: preset = value; break;
                case Compare: cmp = value; break;
                case Units when int.TryParse(value, out var u): units = u; break;
                default: break;
            }
        }
        return new PeriodQuery(grain, anchor, from, to, preset, cmp, units);
    }

    private static string Query(IEnumerable<KeyValuePair<string, string>> pairs)
    {
        var sb = new StringBuilder();
        foreach (var (key, value) in pairs)
        {
            sb.Append(sb.Length == 0 ? '?' : '&')
              .Append(Uri.EscapeDataString(key)).Append('=').Append(Uri.EscapeDataString(value));
        }
        return sb.ToString();
    }

    private static string? Str(HttpRequest r, string key)
    {
        var v = r.Query[key].ToString();
        return string.IsNullOrWhiteSpace(v) ? null : v;
    }

    private static int Int(HttpRequest r, string key, int fallback)
        => int.TryParse(r.Query[key].ToString(), out var v) ? v : fallback;
}
