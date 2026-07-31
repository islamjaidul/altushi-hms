namespace Hms.Kernel.Entitlements;

/// <summary>
/// Holds the verified entitlement (ADR-0016). Loaded at startup from a signed file — no network
/// call, ever — and replaceable at runtime by an authorised admin upload (ADR-0026), because a
/// separately-sold module has its own renewal cycle and renewing must not require a redeploy.
/// <para>
/// The swap is a single reference assignment, so a reader either sees the whole old entitlement or
/// the whole new one, never a half-applied mixture.
/// </para>
/// </summary>
public sealed class EntitlementProvider
{
    private volatile EntitlementFile? _current;

    public EntitlementFile Current => _current
        ?? throw new InvalidOperationException("Entitlement not loaded — startup must call Load.");

    public IReadOnlyCollection<string> EnabledModules => Current.Modules.ToArray();

    /// <summary>True once an entitlement is loaded; lets startup diagnostics avoid throwing.</summary>
    public bool IsLoaded => _current is not null;

    public EntitlementState State => Current.State;

    /// <summary>
    /// Past grace, writes are refused but reads are not (P6: never lock a customer out of their own
    /// data over a billing dispute).
    /// </summary>
    public bool WritesAllowed => Current.State != EntitlementState.ReadOnly;

    public bool IsEnabled(string module) => Current.IsEnabled(module);

    public EntitlementFile Load(string fileContent, string publicKeyPem, DateTimeOffset now)
    {
        var loaded = EntitlementFile.Load(fileContent, publicKeyPem, now);
        _current = loaded;
        return loaded;
    }

    /// <summary>
    /// Replaces the live entitlement from an admin upload. Verification is the same offline
    /// signature check startup uses; additionally the file must name the same customer, so a valid
    /// licence issued to a different hospital cannot be pasted in here.
    /// </summary>
    public EntitlementFile Replace(string fileContent, string publicKeyPem, DateTimeOffset now)
    {
        var incoming = EntitlementFile.Load(fileContent, publicKeyPem, now);
        var existing = _current;

        if (existing is not null &&
            !string.Equals(existing.Customer, incoming.Customer, StringComparison.Ordinal))
            throw new EntitlementException(
                $"That licence is issued to \"{incoming.Customer}\", but this installation is "
                + $"\"{existing.Customer}\". Nothing was changed.");

        _current = incoming;
        return incoming;
    }
}
