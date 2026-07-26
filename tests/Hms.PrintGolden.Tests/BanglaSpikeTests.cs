using Hms.Kernel.Printing;

namespace Hms.PrintGolden.Tests;

/// <summary>
/// Spike A (spec 0005 T10, gates ADR-0009/0014): Bengali shaping through the server-side PDF
/// engine. Automated assertions prove render + font embedding; glyph-level conjunct correctness
/// is verified visually on the emitted artifact (eng/spike-artifacts/bangla-sample.pdf) —
/// printed-sample sign-off is the recorded pass artifact per ADR-0014.
/// </summary>
public class BanglaSpikeTests
{
    private static string RepoRoot()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null && !File.Exists(Path.Combine(dir.FullName, "hms-erp.slnx")))
            dir = dir.Parent!;
        return dir?.FullName ?? throw new InvalidOperationException("repo root not found");
    }

    [Fact]
    public void Bangla_spike_document_renders_with_embedded_bengali_font()
    {
        var root = RepoRoot();
        PdfRenderer.Initialize(Path.Combine(root, "src/Hms.Web/wwwroot/fonts"));

        var pdf = PdfRenderer.RenderBanglaSpikeDocument(
            "Altushi General Hospital",
            "সকল রিপোর্ট অনলাইনে পাওয়া যাবে");

        Assert.True(pdf.Length > 10_000, "PDF suspiciously small — render likely failed");
        var text = System.Text.Encoding.Latin1.GetString(pdf);
        Assert.StartsWith("%PDF", text);
        Assert.Contains("NotoSansBengali", text);   // font subset actually embedded

        var artifactDir = Path.Combine(root, "eng/spike-artifacts");
        Directory.CreateDirectory(artifactDir);
        File.WriteAllBytes(Path.Combine(artifactDir, "bangla-sample.pdf"), pdf);
    }
}
