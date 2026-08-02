using System.ComponentModel.DataAnnotations;

namespace Hms.Shell;

/// <summary>
/// The agreed string bounds (spec 0039 WP1.2), decided once against real Bangladeshi data and
/// applied uniformly: a bound page property, its entity's <c>HasMaxLength</c>, and the column
/// the migration creates all cite these constants, so the three layers cannot drift.
/// </summary>
public static class Bounds
{
    /// <summary>People, doctors, services, products, suppliers. Long Bangladeshi full names
    /// with honorifics stay well under this; a 100 KB paste does not.</summary>
    public const int Name = 200;

    /// <summary>Codes, UHIDs, invoice/receipt numbers, tender references.</summary>
    public const int Code = 40;

    /// <summary>“01712-345678” with room for “+880” and separators.</summary>
    public const int Phone = 20;

    public const int Address = 500;

    /// <summary>Reasons, remarks, operational notes.</summary>
    public const int Note = 4_000;

    /// <summary>Free clinical text — complaints, findings, narratives.</summary>
    public const int Clinical = 10_000;
}

/// <summary>
/// A whole-taka money box: 0 to 10 crore. A decimal or letters never reach this check — the
/// binder refuses them and the input gate (HmsPageModel) turns that into an operator sentence.
/// This bound catches the parseable-but-impossible: negatives and fat-fingered magnitudes.
/// </summary>
public sealed class MoneyAttribute : RangeAttribute
{
    public MoneyAttribute() : base(typeof(long), "0", "100000000")
        => ErrorMessage = "{0} must be a whole-taka amount between 0 and 10,00,00,000";
}

/// <summary>A count of units an operator can plausibly mean — 1 to 9,999 (AUD-VAL-14g:
/// quantity 999999 once posted 29,99,99,700 Tk to a folio).</summary>
public sealed class QtyAttribute : RangeAttribute
{
    public QtyAttribute() : base(1, 9_999)
        => ErrorMessage = "{0} must be between 1 and 9,999";
}

/// <summary>A percentage, 0–100 (AUD-VAL-13a: a service charge of −50% was stored).</summary>
public sealed class PercentAttribute : RangeAttribute
{
    public PercentAttribute() : base(0, 100)
        => ErrorMessage = "{0} must be between 0 and 100";
}

/// <summary>A calendar date the product can mean — refuses 0001-01-01 and 9999-12-31, which
/// Npgsql stores as ±infinity (28 patients and one frozen price already did; AUD-VAL-24c).</summary>
public sealed class PlausibleDateAttribute : RangeAttribute
{
    public PlausibleDateAttribute() : base(typeof(DateOnly), "1900-01-01", "2100-01-01")
        => ErrorMessage = "{0} must be a real calendar date (1900–2100)";
}
