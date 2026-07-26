using System.Globalization;
using System.Text;

namespace Hms.Web;

/// <summary>
/// Presentation helpers shared by every screen. Money is whole taka (C3) grouped the way
/// Bangladeshi operators read it — lakh/crore, not thousands (§7 U15).
/// </summary>
public static class Ui
{
    public const string Taka = "৳";

    /// <summary>1234567 → "12,34,567" (2-2-3 grouping).</summary>
    public static string Group(long value)
    {
        var negative = value < 0;
        var digits = Math.Abs(value).ToString(CultureInfo.InvariantCulture);
        if (digits.Length <= 3) return negative ? "-" + digits : digits;

        var last3 = digits[^3..];
        var rest = digits[..^3];
        var sb = new StringBuilder();
        for (var i = 0; i < rest.Length; i++)
        {
            // groups of two, counted from the right of `rest`
            if (i > 0 && (rest.Length - i) % 2 == 0) sb.Append(',');
            sb.Append(rest[i]);
        }
        sb.Append(',').Append(last3);
        return negative ? "-" + sb : sb.ToString();
    }

    public static string Money(long value) => $"{Taka} {Group(value)}";

    /// <summary>Zero renders as an em dash so a full due column reads at a glance (§7 U12).</summary>
    public static string MoneyOrDash(long value) => value == 0 ? "—" : Money(value);

    private static readonly string[] Ones =
    [
        "", "One", "Two", "Three", "Four", "Five", "Six", "Seven", "Eight", "Nine", "Ten",
        "Eleven", "Twelve", "Thirteen", "Fourteen", "Fifteen", "Sixteen", "Seventeen",
        "Eighteen", "Nineteen",
    ];
    private static readonly string[] Tens =
    [
        "", "", "Twenty", "Thirty", "Forty", "Fifty", "Sixty", "Seventy", "Eighty", "Ninety",
    ];

    private static string UnderHundred(long n) =>
        n < 20 ? Ones[n] : (Tens[n / 10] + (n % 10 > 0 ? " " + Ones[n % 10] : "")).Trim();

    /// <summary>"One Lakh Twenty Three Thousand Taka Only" — the receipt's legal line (§7 U10).</summary>
    public static string Words(long amount)
    {
        if (amount == 0) return "Zero Taka Only";
        var negative = amount < 0;
        var n = Math.Abs(amount);
        var parts = new List<string>();

        void Take(long unit, string name)
        {
            var count = n / unit;
            if (count == 0) return;
            parts.Add((unit >= 100 && count >= 100 ? Words(count).Replace(" Taka Only", "") : UnderHundred(count))
                      + " " + name);
            n %= unit;
        }

        Take(10_000_000, "Crore");
        Take(100_000, "Lakh");
        Take(1_000, "Thousand");
        Take(100, "Hundred");
        if (n > 0) parts.Add(UnderHundred(n));

        var words = string.Join(" ", parts);
        return (negative ? "Minus " : "") + words + " Taka Only";
    }

    /// <summary>Age from DOB when we have it, else the recorded age (02 §2.2 — DOB always wins).</summary>
    public static string AgeDisplay(DateOnly? dob, short? ageYears, short? ageMonths, DateOnly today)
    {
        if (dob is { } d)
        {
            var years = today.Year - d.Year;
            if (today < d.AddYears(years)) years--;
            if (years > 0) return $"{years} y";
            var months = (today.Year - d.Year) * 12 + today.Month - d.Month;
            return $"{Math.Max(0, months)} m";
        }
        if (ageYears is { } y2 && y2 > 0) return $"{y2} y";
        if (ageMonths is { } m2) return $"{m2} m";
        return "—";
    }

    public static string SexWord(char sex) => sex switch
    {
        'M' or 'm' => "Male",
        'F' or 'f' => "Female",
        _ => "Other",
    };

    public static string SexShort(char sex) => sex switch
    {
        'M' or 'm' => "M",
        'F' or 'f' => "F",
        _ => "O",
    };

    /// <summary>Two-letter monogram for avatars; never throws on odd names.</summary>
    public static string Initials(string? name)
    {
        if (string.IsNullOrWhiteSpace(name)) return "—";
        var parts = name.Split(' ', StringSplitOptions.RemoveEmptyEntries)
            .Where(p => char.IsLetter(p[0])).ToArray();
        if (parts.Length == 0) return name.Trim()[..1].ToUpperInvariant();
        if (parts.Length == 1) return parts[0][..Math.Min(2, parts[0].Length)].ToUpperInvariant();
        return (parts[0][..1] + parts[^1][..1]).ToUpperInvariant();
    }

    private static readonly TimeZoneInfo Dhaka = TimeZoneInfo.FindSystemTimeZoneById("Asia/Dhaka");

    /// <summary>Everything an operator reads is Asia/Dhaka wall-clock; storage stays UTC.</summary>
    public static DateTimeOffset Local(DateTimeOffset instant) => TimeZoneInfo.ConvertTime(instant, Dhaka);

    /// <summary>
    /// Start of a Dhaka business day as a UTC instant. Queries compare against timestamptz, which
    /// Npgsql binds in UTC only — so the conversion happens here, once, rather than per query.
    /// </summary>
    public static DateTimeOffset DhakaMidnightUtc(DateOnly day)
    {
        var local = day.ToDateTime(TimeOnly.MinValue);
        var offset = Dhaka.GetUtcOffset(local);
        return new DateTimeOffset(local, offset).ToUniversalTime();
    }

    public static string Time(DateTimeOffset instant) => Local(instant).ToString("HH:mm");
    public static string DateTimeShort(DateTimeOffset instant) => Local(instant).ToString("dd MMM yyyy, HH:mm");
    public static string DateLong(DateOnly date) => date.ToString("dd MMM yyyy");

    /// <summary>
    /// Maps a workflow state to a pill class + operator-facing word. Colour and word always
    /// travel together (§7 U12) — the word alone must carry the meaning.
    /// </summary>
    public static (string Css, string Word) Status(string state) => state switch
    {
        "paid" => ("good", "Paid"),
        "partially_paid" => ("warn", "Part paid"),
        "billed" => ("warn", "Due"),
        "draft" => ("", "Draft"),
        "cancelled" => ("bad", "Cancelled"),
        "refunded" => ("bad", "Refunded"),

        "pending_collection" => ("warn", "Awaiting collection"),
        "collected" => ("info", "Collected"),
        "received" => ("info", "Received at lab"),
        "resulted" => ("warn", "Result entered"),
        "verified" => ("good", "Verified"),
        "report_ready" => ("good", "Report ready"),
        "delivered" => ("good", "Delivered"),
        "rejected" => ("bad", "Rejected"),

        "ordered" => ("warn", "Ordered"),
        "invoiced" => ("warn", "Awaiting payment"),
        "in_progress" => ("info", "In progress"),
        "reported" => ("good", "Reported"),

        "booked" => ("warn", "Waiting"),
        "arrived" => ("warn", "Arrived"),
        "in_chamber" => ("info", "In consult"),
        "done" => ("good", "Done"),
        "no_show" => ("bad", "No show"),

        "opened" or "active" or "reopened" => ("good", "Open"),
        "close_pending" => ("warn", "Closing"),
        "closed" => ("", "Closed"),

        "pending" => ("warn", "Pending"),
        "approved" => ("good", "Approved"),
        "expired" => ("bad", "Expired"),

        "queued" => ("warn", "Queued"),
        "sent" => ("good", "Sent"),
        "failed" => ("bad", "Failed"),
        "skipped_no_phone" => ("", "No phone on file"),

        _ => ("", Humanize(state)),
    };

    public static string Humanize(string token) =>
        string.IsNullOrEmpty(token)
            ? ""
            : char.ToUpperInvariant(token[0]) + token[1..].Replace('_', ' ');
}
