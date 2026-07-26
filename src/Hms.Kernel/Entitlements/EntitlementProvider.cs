namespace Hms.Kernel.Entitlements;

/// <summary>
/// Holds the verified entitlement for the process lifetime (ADR-0016). Loaded at startup from
/// a signed file — no network call, ever. Admin upload of a fresh file lands in S5.
/// </summary>
public sealed class EntitlementProvider
{
    private EntitlementFile? _current;

    public EntitlementFile Current => _current
        ?? throw new InvalidOperationException("Entitlement not loaded — startup must call Load.");

    public IReadOnlyCollection<string> EnabledModules => Current.Modules.ToArray();

    public void Load(string fileContent, string publicKeyPem, DateTimeOffset now)
        => _current = EntitlementFile.Load(fileContent, publicKeyPem, now);
}
