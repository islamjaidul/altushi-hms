namespace Hms.Shell;

/// <summary>
/// The letterhead block every printed document shares (05 §6): change the identity once and
/// receipts, reports, cards and statements all follow.
/// </summary>
public sealed record Letterhead(string DocTitle, string DocNo, DateTimeOffset At);

/// <summary>One printable line on a money document.</summary>
public sealed record DocLine(string Description, int Qty, long Amount);
