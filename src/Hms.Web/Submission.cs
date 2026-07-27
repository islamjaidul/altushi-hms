using Hms.Billing;
using Hms.Billing.Data;
using Microsoft.EntityFrameworkCore;
using Npgsql;

namespace Hms.Web;

/// <summary>
/// One prepared bill, one invoice (spec 0021). Every screen that issues a financial document
/// mints a token when it renders, carries it through cart postbacks, and hands it to the
/// billing spine at save. A double-click, a browser resend, or a retried request then resolves
/// to the invoice that already exists instead of billing the patient a second time.
///
/// The check is courtesy; the <c>unique (submission_token)</c> index is the guarantee. Two
/// requests can both pass the check — one insert wins, the loser lands here and is redirected
/// to the winner's invoice. Same discipline as ADR-0015: constraints, not timing.
/// </summary>
public static class Submission
{
    public static Guid NewToken() => Guid.NewGuid();

    /// <summary>True when this exception is Postgres refusing a duplicate submission token.</summary>
    public static bool IsDuplicateSubmission(DbUpdateException e) =>
        e.InnerException is PostgresException { SqlState: "23505" } pg &&
        pg.ConstraintName?.Contains("submission_token", StringComparison.Ordinal) == true;

    /// <summary>The invoice a token already produced, or null if this submission is new.</summary>
    public static Task<Invoice?> ExistingAsync(TxScope s, Guid? token, CancellationToken ct = default)
        => BillingService.FindBySubmissionAsync(s.Bill, token, ct);
}
