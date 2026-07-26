using System.Security.Cryptography;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace Hms.Kernel.Entitlements;

public sealed class EntitlementException(string message) : Exception(message);

public enum EntitlementState
{
    Active,
    /// <summary>Past expiry, inside the grace window — banner shown, everything still works (P6).</summary>
    Grace,
    /// <summary>Past grace — writes gated off, data always readable (P6: never lock clinical data).</summary>
    ReadOnly,
}

/// <summary>
/// Signed module-entitlement file (ADR-0016). Format: JSON envelope
/// { "payload": base64(payload-json), "signature": base64(RSA-SHA256 over the payload bytes) }.
/// Verification is offline against the vendor public key baked into the build — no network call, ever.
/// Disabling a module hides behaviour, never data.
/// </summary>
public sealed class EntitlementFile
{
    public required string Customer { get; init; }
    public required IReadOnlySet<string> Modules { get; init; }
    public required int Branches { get; init; }
    public required DateTimeOffset ExpiresUtc { get; init; }
    public required int GraceDays { get; init; }
    public required EntitlementState State { get; init; }

    public bool IsEnabled(string module) => Modules.Contains(module);

    private sealed record Envelope(
        [property: JsonPropertyName("payload")] string Payload,
        [property: JsonPropertyName("signature")] string Signature);

    private sealed record Payload(
        [property: JsonPropertyName("customer")] string Customer,
        [property: JsonPropertyName("modules")] string[] Modules,
        [property: JsonPropertyName("branches")] int Branches,
        [property: JsonPropertyName("expiresUtc")] string ExpiresUtc,
        [property: JsonPropertyName("graceDays")] int GraceDays);

    public static EntitlementFile Load(string fileContent, string publicKeyPem, DateTimeOffset now)
    {
        Envelope envelope;
        byte[] payloadBytes, signature;
        try
        {
            envelope = JsonSerializer.Deserialize<Envelope>(fileContent)
                       ?? throw new EntitlementException("Entitlement file is empty.");
            payloadBytes = Convert.FromBase64String(envelope.Payload);
            signature = Convert.FromBase64String(envelope.Signature);
        }
        catch (Exception e) when (e is JsonException or FormatException)
        {
            throw new EntitlementException("Entitlement file is malformed.");
        }

        using var rsa = RSA.Create();
        rsa.ImportFromPem(publicKeyPem);
        if (!rsa.VerifyData(payloadBytes, signature, HashAlgorithmName.SHA256, RSASignaturePadding.Pkcs1))
            throw new EntitlementException("Entitlement signature is invalid.");

        Payload payload;
        try
        {
            payload = JsonSerializer.Deserialize<Payload>(payloadBytes)
                      ?? throw new EntitlementException("Entitlement payload is empty.");
        }
        catch (JsonException)
        {
            throw new EntitlementException("Entitlement payload is malformed.");
        }

        var expires = DateTimeOffset.Parse(payload.ExpiresUtc);
        var state = now <= expires ? EntitlementState.Active
            : now <= expires.AddDays(payload.GraceDays) ? EntitlementState.Grace
            : EntitlementState.ReadOnly;

        return new EntitlementFile
        {
            Customer = payload.Customer,
            Modules = payload.Modules.ToHashSet(StringComparer.Ordinal),
            Branches = payload.Branches,
            ExpiresUtc = expires,
            GraceDays = payload.GraceDays,
            State = state,
        };
    }
}
