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
    string FooterNote)
{
    /// <summary>Reads <c>Org:*</c>, falling back to <c>Hospital:*</c>, then to the supplied defaults.</summary>
    public static OrgIdentity From(IConfiguration config, string defaultName, string defaultTagline)
    {
        string Value(string key, string fallback)
            => config[$"Org:{key}"] ?? config[$"Hospital:{key}"] ?? fallback;

        return new OrgIdentity(
            Value("Name", defaultName),
            Value("Tagline", defaultTagline),
            Value("Address", "VIP Road, Sheikhghat, Sylhet-3100"),
            Value("Phone", "0821-719944, 01700-000000"),
            Value("Monogram", "A"),
            Value("FooterNote",
                "This is a computer generated document — no signature required for receipts."));
    }
}
