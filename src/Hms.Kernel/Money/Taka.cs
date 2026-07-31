namespace Hms.Kernel.Money;

/// <summary>
/// Whole-taka arithmetic (C3). Money is <see cref="long"/> everywhere in this product; there is no
/// decimal in a money path and no sub-taka amount is ever stored.
/// <para>
/// This lives in the kernel because payroll needs exactly the rounding rule billing already uses,
/// and M16 must not reference M11's implementation to get it (ADR-0003). <see cref="RoundHalfUp"/>
/// is the same expression <c>BillingService.RoundHalfUp</c> has always used — moved, not redefined,
/// so an invoice and a payslip round a half-taka the same way.
/// </para>
/// </summary>
public static class Taka
{
    /// <summary>Basis points denominator: 10000 bp = 100%.</summary>
    public const long Bp = 10_000;

    /// <summary>Round half away from zero to whole taka — billing's rule, verbatim.</summary>
    public static long RoundHalfUp(decimal value) => (long)Math.Floor(value + 0.5m);

    /// <summary>
    /// <paramref name="amount"/> × <paramref name="rateBp"/> basis points, rounded once at the end.
    /// Percentages are applied to a total exactly once — never per line and then again on the sum.
    /// </summary>
    public static long ApplyBp(long amount, long rateBp) => RoundHalfUp((decimal)amount * rateBp / Bp);

    /// <summary>
    /// Prorates <paramref name="amount"/> over <paramref name="partBp"/> of a whole. Used for
    /// mid-month joiners and part-days, where the convention (calendar days, fixed 30, working days)
    /// has already decided what the denominator was — this only does the arithmetic.
    /// </summary>
    public static long Prorate(long amount, long partBp) => ApplyBp(amount, partBp);

    /// <summary>
    /// Distributes rounding residue so a set of component amounts sums exactly to
    /// <paramref name="target"/>. Integer components rounded independently will not add up; rather
    /// than let a payslip disagree with itself by a taka, the difference is given to the largest
    /// component (the one where a single taka is least visible), deterministically.
    /// </summary>
    /// <returns>The index that absorbed the residue, or -1 when there was none to absorb.</returns>
    public static int AbsorbResidue(IList<long> amounts, long target)
    {
        if (amounts.Count == 0) return -1;

        var sum = 0L;
        foreach (var a in amounts) sum += a;

        var residue = target - sum;
        if (residue == 0) return -1;

        var largest = 0;
        for (var i = 1; i < amounts.Count; i++)
            if (amounts[i] > amounts[largest]) largest = i;

        amounts[largest] += residue;
        return largest;
    }
}
