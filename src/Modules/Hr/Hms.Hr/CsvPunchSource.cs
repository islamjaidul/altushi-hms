using System.Globalization;
using System.Runtime.CompilerServices;

namespace Hms.Hr;

/// <summary>
/// The file/paste implementation of <see cref="IPunchSource"/> (spec 0039 WP3, AUD-M16-08). §13 I8
/// wants a live device feed and §9A.3 excludes it, so this is how punches reach the product today:
/// whatever export the employer's machine produces, re-saved as delimited text.
/// <para>
/// One row per punch: <c>employee_code, date time[, in|out]</c> — comma, semicolon or tab
/// separated. Times are Dhaka wall-clock (Asia/Dhaka has no DST, so the +06:00 offset is a
/// constant, not a guess). A header row and blank lines are skipped; a row whose timestamp cannot
/// be read is surfaced through the import's rejection report rather than silently dropped.
/// </para>
/// <para>
/// <see cref="Name"/> is a constant, deliberately: it becomes the punch's <c>DeviceId</c>, and
/// UNIQUE(employee, device, punched_at) is what makes re-importing the same rows — even from a
/// renamed file — a reported duplicate instead of a doubled day.
/// </para>
/// </summary>
public sealed class CsvPunchSource : IPunchSource
{
    public string Name => "csv-import";

    private static readonly string[] Formats =
    [
        "dd/MM/yyyy HH:mm", "dd/MM/yyyy HH:mm:ss",
        "yyyy-MM-dd HH:mm", "yyyy-MM-dd HH:mm:ss",
        "yyyy-MM-ddTHH:mm", "yyyy-MM-ddTHH:mm:ss",
        "dd MMM yyyy HH:mm",
    ];

    private static readonly TimeSpan Dhaka = TimeSpan.FromHours(6);

    public async IAsyncEnumerable<PunchRow> ReadAsync(
        Stream content, [EnumeratorCancellation] CancellationToken ct = default)
    {
        using var reader = new StreamReader(content, leaveOpen: true);
        while (await reader.ReadLineAsync(ct) is { } line)
        {
            var raw = line.Trim();
            if (raw.Length == 0) continue;
            if (raw.StartsWith("employee", StringComparison.OrdinalIgnoreCase)) continue;  // header

            var parts = raw.Split([',', ';', '\t'], StringSplitOptions.TrimEntries);
            if (parts.Length < 2 || parts[0].Length == 0)
            {
                // Malformed rows flow into ImportAsync's rejection report (its employee-code
                // lookup fails), so the operator sees the bad line instead of a shrunk count.
                yield return new PunchRow(Clip(raw), DateTimeOffset.MinValue, null);
                continue;
            }

            if (!DateTime.TryParseExact(parts[1], Formats, CultureInfo.InvariantCulture,
                    DateTimeStyles.None, out var local))
            {
                yield return new PunchRow(Clip(raw), DateTimeOffset.MinValue, null);
                continue;
            }

            var direction = parts.Length >= 3 ? parts[2].ToLowerInvariant() : null;
            if (direction is not ("in" or "out")) direction = null;

            yield return new PunchRow(
                parts[0].ToUpperInvariant(),
                new DateTimeOffset(local, Dhaka).ToUniversalTime(),
                direction);
        }
    }

    private static string Clip(string s) => s.Length <= 60 ? s : s[..60];
}
