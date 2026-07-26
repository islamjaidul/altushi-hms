using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Hms.Kernel.Entitlements;

namespace Hms.Kernel.Tests;

// ADR-0016: signed entitlement file, vendor key pair, public key baked in; no network call.
public class EntitlementTests
{
    private static (string file, string publicKeyPem) Signed(object payload, Action<byte[]>? tamper = null)
    {
        using var rsa = RSA.Create(2048);
        var payloadBytes = Encoding.UTF8.GetBytes(JsonSerializer.Serialize(payload));
        tamper?.Invoke(payloadBytes);
        var sig = rsa.SignData(payloadBytes, HashAlgorithmName.SHA256, RSASignaturePadding.Pkcs1);
        var file = JsonSerializer.Serialize(new
        {
            payload = Convert.ToBase64String(payloadBytes),
            signature = Convert.ToBase64String(sig),
        });
        return (file, rsa.ExportSubjectPublicKeyInfoPem());
    }

    private static readonly object ValidPayload = new
    {
        customer = "Altushi General Hospital",
        modules = new[] { "Registration", "Billing" },
        branches = 1,
        expiresUtc = DateTime.UtcNow.AddYears(1).ToString("O"),
        graceDays = 30,
    };

    [Fact]
    public void Valid_signature_loads_and_gates_modules()
    {
        var (file, pub) = Signed(ValidPayload);
        var ent = EntitlementFile.Load(file, pub, DateTimeOffset.UtcNow);
        Assert.True(ent.IsEnabled("Registration"));
        Assert.True(ent.IsEnabled("Billing"));
        Assert.False(ent.IsEnabled("Lis"));
        Assert.Equal(EntitlementState.Active, ent.State);
    }

    [Fact]
    public void Tampered_payload_is_rejected()
    {
        var (file, pub) = Signed(ValidPayload);
        // flip one byte inside the base64 payload after signing
        var doc = JsonSerializer.Deserialize<Dictionary<string, string>>(file)!;
        var bytes = Convert.FromBase64String(doc["payload"]);
        bytes[5] ^= 0xFF;
        doc["payload"] = Convert.ToBase64String(bytes);
        var tampered = JsonSerializer.Serialize(doc);

        Assert.Throws<EntitlementException>(() => EntitlementFile.Load(tampered, pub, DateTimeOffset.UtcNow));
    }

    [Fact]
    public void Wrong_key_is_rejected()
    {
        var (file, _) = Signed(ValidPayload);
        using var otherKey = RSA.Create(2048);
        Assert.Throws<EntitlementException>(
            () => EntitlementFile.Load(file, otherKey.ExportSubjectPublicKeyInfoPem(), DateTimeOffset.UtcNow));
    }

    [Fact]
    public void Expired_within_grace_is_grace_state_still_enabled()
    {
        var payload = new
        {
            customer = "X", modules = new[] { "Registration" }, branches = 1,
            expiresUtc = DateTime.UtcNow.AddDays(-5).ToString("O"), graceDays = 30,
        };
        var (file, pub) = Signed(payload);
        var ent = EntitlementFile.Load(file, pub, DateTimeOffset.UtcNow);
        Assert.Equal(EntitlementState.Grace, ent.State);   // P6 default: banner, still working
        Assert.True(ent.IsEnabled("Registration"));
    }

    [Fact]
    public void Expired_past_grace_is_readonly_never_disabled_data()
    {
        var payload = new
        {
            customer = "X", modules = new[] { "Registration" }, branches = 1,
            expiresUtc = DateTime.UtcNow.AddDays(-40).ToString("O"), graceDays = 30,
        };
        var (file, pub) = Signed(payload);
        var ent = EntitlementFile.Load(file, pub, DateTimeOffset.UtcNow);
        Assert.Equal(EntitlementState.ReadOnly, ent.State); // P6: read-only, never lock clinical data
        Assert.True(ent.IsEnabled("Registration"));          // visibility retained; writes gated by state
    }
}
