namespace Hms.Web;

/// <summary>
/// The reference's "one letterhead system" (05 §6): the identity block used by the sidebar,
/// ID cards, receipts, reports and certificates. Configuration today; the Settings screen
/// writes it to `kernel.setting` when masters land (spec 0009).
/// </summary>
public sealed record HospitalIdentity(
    string Name,
    string Tagline,
    string Address,
    string Phone,
    string Monogram,
    string FooterNote);
