using Hms.Shell;

namespace Hms.Web.Tests;

/// <summary>
/// Spec 0039 WP6 (AUD-M1-01). Every expected codeword sequence here is computed BY HAND from the
/// Code 128 definition, never by calling the encoder — a test that asks the code what the code
/// produces can only ever agree with it.
/// </summary>
public class Barcode128Tests
{
    [Fact]
    public void Wikipedia_example_start_checksum_and_stop_match_the_published_symbol()
    {
        // Subset B values are ASCII-32: W=55 i=73 k=75 i=73 p=80 e=69 d=68 i=73 a=65.
        // Checksum = 104 + 1·55 + 2·73 + 3·75 + 4·73 + 5·80 + 6·69 + 7·68 + 8·73 + 9·65
        //          = 104 + 55 + 146 + 225 + 292 + 400 + 414 + 476 + 584 + 585 = 3281
        // 3281 mod 103 = 3281 − 31·103 = 3281 − 3193 = 88  (the published example's checksum).
        Assert.Equal(
            new[] { 104, 55, 73, 75, 73, 80, 69, 68, 73, 65, 88, 106 },
            Barcode128.Codewords("Wikipedia"));
    }

    [Fact]
    public void Pure_even_digits_use_subset_C_from_the_start()
    {
        // Start C = 105, pairs 12 and 34.
        // Checksum = 105 + 1·12 + 2·34 = 185; 185 mod 103 = 82.
        Assert.Equal(new[] { 105, 12, 34, 82, 106 }, Barcode128.Codewords("1234"));
    }

    [Fact]
    public void A_uhid_switches_to_subset_C_for_its_digit_tail()
    {
        // ALT-000001: A=33 L=44 T=52 -=13 in B, then Code C (99) and pairs 00,00,01.
        // Checksum = 104 + 1·33 + 2·44 + 3·52 + 4·13 + 5·99 + 6·0 + 7·0 + 8·1
        //          = 104 + 33 + 88 + 156 + 52 + 495 + 0 + 0 + 8 = 936; 936 mod 103 = 936 − 9·103 = 9.
        Assert.Equal(
            new[] { 104, 33, 44, 52, 13, 99, 0, 0, 1, 9, 106 },
            Barcode128.Codewords("ALT-000001"));
    }

    [Fact]
    public void An_odd_digit_tail_spends_its_first_digit_in_subset_B()
    {
        // S12345: S=51, then '1'=17 in B (odd run), Code C (99), pairs 23 and 45.
        // Checksum = 104 + 1·51 + 2·17 + 3·99 + 4·23 + 5·45 = 104+51+34+297+92+225 = 803;
        // 803 mod 103 = 803 − 7·103 = 82.
        Assert.Equal(
            new[] { 104, 51, 17, 99, 23, 45, 82, 106 },
            Barcode128.Codewords("S12345"));
    }

    [Fact]
    public void Start_code_and_stop_patterns_are_the_symbology_constants()
    {
        Assert.Equal("211214", Barcode128.Pattern(Barcode128.StartB));
        Assert.Equal("211232", Barcode128.Pattern(Barcode128.StartC));
        Assert.Equal("2331112", Barcode128.Pattern(Barcode128.Stop));
    }

    [Fact]
    public void Every_symbol_is_eleven_modules_and_the_stop_is_thirteen()
    {
        for (var value = 0; value <= 105; value++)
            Assert.Equal(11, Barcode128.Pattern(value).Sum(w => w - '0'));
        Assert.Equal(13, Barcode128.Pattern(106).Sum(w => w - '0'));
    }

    [Fact]
    public void Golden_width_a_regression_in_the_encoding_moves_it()
    {
        // ALT-000001 encodes to 11 codewords: 10 × 11 modules + 13 (stop) + 2 × 10 quiet = 143.
        Assert.Equal(143, Barcode128.TotalModules("ALT-000001"));
        // Wikipedia: 12 codewords → 11 × 11 + 13 + 20 = 154.
        Assert.Equal(154, Barcode128.TotalModules("Wikipedia"));
    }

    [Fact]
    public void Svg_is_self_contained_sized_in_modules_and_inked_by_currentColor()
    {
        var svg = Barcode128.Svg("ALT-000001");
        Assert.StartsWith("<svg ", svg);
        Assert.Contains("viewBox=\"0 0 143 1\"", svg);
        Assert.Contains("fill=\"currentColor\"", svg);
        Assert.DoesNotContain("https://", svg);   // vendored means vendored (edge 1)
        // The gradient fake had no rects; a real symbol has one per bar.
        Assert.True(svg.Split("<rect ").Length > 20, "expected a rect per bar");
    }

    [Fact]
    public void Content_outside_subset_B_is_refused_not_silently_mangled()
    {
        Assert.Throws<ArgumentException>(() => Barcode128.Codewords(""));
        Assert.Throws<ArgumentException>(() => Barcode128.Codewords("রিপোর্ট"));
        Assert.Throws<ArgumentException>(() => Barcode128.Codewords("tab\tchar"));
    }
}
