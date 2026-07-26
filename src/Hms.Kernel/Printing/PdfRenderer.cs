using QuestPDF.Drawing;
using QuestPDF.Fluent;
using QuestPDF.Helpers;
using QuestPDF.Infrastructure;

namespace Hms.Kernel.Printing;

/// <summary>
/// Server-side PDF path (ADR-0009): archival copies and the no-printer fallback.
/// Candidate engine: QuestPDF (Skia/HarfBuzz shaping). Bengali conjunct/matra shaping is the
/// ADR-0014 spike acceptance test — see PrintGolden tests + eng/spike-artifacts.
/// Licence note (G4): QuestPDF Community licence — free below the vendor revenue threshold;
/// verified at selection time, recorded in spec 0005 notes for the ADR-0009 update.
/// </summary>
public static class PdfRenderer
{
    private static bool _initialized;

    /// <summary>Registers the self-hosted fonts (no system-font dependence in containers).</summary>
    public static void Initialize(string fontDirectory)
    {
        if (_initialized) return;
        QuestPDF.Settings.License = LicenseType.Community;
        QuestPDF.Settings.UseEnvironmentFonts = false;
        // QuestPDF/Skia consumes ttf/otf; the UI's woff2 files are browser-only.
        foreach (var font in Directory.EnumerateFiles(fontDirectory, "*.ttf"))
            FontManager.RegisterFont(File.OpenRead(font));
        _initialized = true;
    }

    public const string BengaliFont = "Noto Sans Bengali";

    /// <summary>
    /// The S1 spike document: an A4 letterhead-style page with English body and a Bangla footer
    /// containing conjunct-heavy text. If this shapes correctly, the ADR-0009 primary path holds.
    /// </summary>
    public static byte[] RenderBanglaSpikeDocument(string hospitalNameEn, string banglaFooter)
    {
        return Document.Create(doc => doc.Page(page =>
        {
            page.Size(PageSizes.A4);
            page.Margin(36);
            page.Content().Column(col =>
            {
                col.Item().Text(hospitalNameEn).FontSize(20).Bold();
                col.Item().PaddingTop(4).Text("Sylhet, Bangladesh — Test Document (Spike A)").FontSize(10);
                col.Item().PaddingTop(16).LineHorizontal(0.5f);
                col.Item().PaddingTop(16).Text(
                    "This is the ADR-0009/0014 walking-skeleton test PDF. " +
                    "The footer below must render Bengali conjuncts and matras correctly.")
                    .FontSize(11);
                col.Item().PaddingTop(24).Text(t =>
                {
                    t.DefaultTextStyle(s => s.FontFamily(BengaliFont).FontSize(14));
                    t.Line("আলতুশি জেনারেল হাসপাতাল — স্বাস্থ্যসেবা আপনার দোরগোড়ায়");
                    t.Line("চিকিৎসা প্রতিবেদন ক্ষতিগ্রস্ত হলে দ্রুত যোগাযোগ করুন।");
                    t.Line("মূল্য: ৳ ১,৫০০ (এক হাজার পাঁচশত টাকা মাত্র)");
                });
            });
            page.Footer().Text(t =>
            {
                t.DefaultTextStyle(s => s.FontFamily(BengaliFont).FontSize(9));
                t.Span("সকল রিপোর্ট অনলাইনে পাওয়া যাবে — ধন্যবাদ");
            });
        })).GeneratePdf();
    }
}
