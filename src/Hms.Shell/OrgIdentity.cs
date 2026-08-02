using Microsoft.Extensions.Configuration;

namespace Hms.Shell;

/// <summary>
/// The reference's "one letterhead system" (05 §6): the identity block used by the sidebar,
/// ID cards, receipts, reports and certificates. Configuration today; the Settings screen
/// writes it to `kernel.setting` when masters land (spec 0009).
/// <para>
/// Named for the organisation, not the hospital, because the same shell serves the HRM SKU sold
/// to employers that are not hospitals (ADR-0025, P27). Configuration is read from <c>Org:*</c>
/// with a fallback to the original <c>Hospital:*</c> keys — the live deployment is configured with
/// those, and renaming them would be a silent outage.
/// </para>
/// </summary>
public sealed record OrgIdentity(
    string Name,
    string Tagline,
    string Address,
    string Phone,
    string Monogram,
    string FooterNote,
    string ProductName,
    string DefaultShortcuts)
{
    /// <summary>Reads <c>Org:*</c>, falling back to <c>Hospital:*</c>, then to the supplied defaults.</summary>
    public static OrgIdentity From(
        IConfiguration config,
        string defaultName,
        string defaultTagline,
        string productName = "Hospital ERP",
        string shortcuts = "F2 New patient · F3 Item search · F10 Payment")
    {
        string Value(string key, string fallback)
            => config[$"Org:{key}"] ?? config[$"Hospital:{key}"] ?? fallback;

        var name = Value("Name", defaultName);

        return new OrgIdentity(
            name,
            Value("Tagline", defaultTagline),
            Value("Address", "VIP Road, Sheikhghat, Sylhet-3100"),
            Value("Phone", "0821-719944, 01700-000000"),
            Value("Monogram", Initial(name)),
            Value("FooterNote",
                "This is a computer generated document — no signature required for receipts."),
            productName,
            shortcuts);
    }

    /// <summary>
    /// The monogram defaults to the organisation's own initial. It used to default to the literal
    /// "A" — correct for Altushi General Hospital and wrong for every other customer, which the
    /// first HRM deployment showed by badging "Meghna Textiles Ltd." with an A. An explicit
    /// <c>Org:Monogram</c> still wins, for organisations whose mark is not their first letter.
    /// </summary>
    private static string Initial(string name)
    {
        foreach (var c in name)
            if (char.IsLetterOrDigit(c)) return char.ToUpperInvariant(c).ToString();

        return "•";   // a name with no letters at all — better than an empty box
    }
}

/// <summary>
/// Product-level naming and shortcuts, supplied by each host. The layout used to hardcode
/// <c>F2 New patient · F3 Item search · F10 Payment</c> and <c>MVP build</c>, which the HRM SKU then
/// displayed on every screen of a payroll product sold to a textile mill — the two lines every page
/// shows, both belonging to a different product (P27's neutral-vocabulary commitment, spec 0036).
/// </summary>
public static class ProductChrome
{
    public const string ErpName = "Hospital ERP";
    public const string ErpShortcuts = "F2 New patient · F3 Item search · F10 Payment";

    public const string HrmName = "HR & Payroll";
    public const string HrmShortcuts = "F2 New employee · F3 Search · F10 Payroll";
}
