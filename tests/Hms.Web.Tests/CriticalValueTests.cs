using Hms.Web;

namespace Hms.Web.Tests;

/// <summary>
/// Spec 0044 — §5 M9 [M] "abnormal &amp; critical flags".
/// <para>
/// The defect these exist for: <c>Flag</c> had three outcomes, so a haemoglobin of 3.1 g/dL and
/// one of 11.9 both rendered <c>L</c>, in the same colour, on the same row — and verification was
/// a single POST with no acknowledgement, so a panic result released exactly as fast as a normal
/// one. US9.3's acceptance criterion asks for the opposite of that.
/// </para>
/// </summary>
public class CriticalValueTests
{
    private static readonly ReferenceBand Hb =
        new(null, null, null, Low: 12.0m, High: 16.0m, CriticalLow: 7.0m, CriticalHigh: 20.0m);

    /// <summary>A band with no panic values — an ESR or a cholesterol. It must stay quiet.</summary>
    private static readonly ReferenceBand Esr = new(null, null, null, Low: 0, High: 15);

    [Theory]
    // the case that named the spec
    [InlineData(3.1, ResultFlags.CriticalLow)]
    [InlineData(11.9, ResultFlags.Low)]
    // and its mirror at the top
    [InlineData(24.0, ResultFlags.CriticalHigh)]
    [InlineData(17.5, ResultFlags.High)]
    [InlineData(13.5, "")]
    public void Haemoglobin_is_judged_critical_before_it_is_judged_abnormal(double value, string expected)
        => Assert.Equal(expected, ResultTemplates.Flag((decimal)value, Hb));

    /// <summary>
    /// A critical list is written "≤ 7.0 g/dL", so 7.0 itself is critical. An exclusive test
    /// would let exactly the threshold through as merely low — the one value most likely to be
    /// typed when someone is reading off a printed policy.
    /// </summary>
    [Theory]
    [InlineData(7.0, ResultFlags.CriticalLow)]
    [InlineData(7.01, ResultFlags.Low)]
    [InlineData(20.0, ResultFlags.CriticalHigh)]
    [InlineData(19.99, ResultFlags.High)]
    public void The_critical_bound_itself_is_critical(double value, string expected)
        => Assert.Equal(expected, ResultTemplates.Flag((decimal)value, Hb));

    [Theory]
    [InlineData(0.0)]
    [InlineData(80.0)]
    [InlineData(9.0)]
    public void A_band_with_no_critical_bounds_never_reports_one(double value)
        => Assert.False(ResultFlags.IsCritical(ResultTemplates.Flag((decimal)value, Esr)));

    [Fact]
    public void A_band_with_no_critical_bounds_still_flags_abnormal()
    {
        Assert.Equal(ResultFlags.High, ResultTemplates.Flag(40m, Esr));
        Assert.Equal("", ResultTemplates.Flag(8m, Esr));
    }

    /// <summary>Creatinine has a panic high and no meaningful panic low — a real asymmetry.</summary>
    [Fact]
    public void A_parameter_may_carry_only_one_critical_bound()
    {
        var scr = new ReferenceBand(null, null, null, 0.6m, 1.3m, CriticalLow: null, CriticalHigh: 5.0m);
        Assert.Equal(ResultFlags.CriticalHigh, ResultTemplates.Flag(6.2m, scr));
        Assert.Equal(ResultFlags.Low, ResultTemplates.Flag(0.2m, scr));      // low, never critical
    }

    [Fact]
    public void A_value_that_is_missing_is_not_flagged()
        => Assert.Equal("", ResultTemplates.Flag(null, Hb));

    // ---- the shipped templates -------------------------------------------------------

    [Theory]
    [InlineData("CBC", "HB")]
    [InlineData("CBC", "TC")]
    [InlineData("CBC", "PLT")]
    [InlineData("RBS", "RBS")]
    [InlineData("SCR", "SCR")]
    public void The_parameters_that_have_a_panic_value_ship_with_one(string test, string code)
    {
        var p = ResultTemplates.Parse(ResultTemplates.For(test))!
            .Parameters.Single(x => x.Code == code);
        Assert.All(p.SafeBands, b =>
            Assert.True(b.CriticalLow is not null || b.CriticalHigh is not null,
                $"{test}/{code} band '{b.Label}' carries no critical bound"));
    }

    /// <summary>Not everything has a panic value, and inventing one would be worse than none.</summary>
    [Theory]
    [InlineData("ESR", "ESR")]
    [InlineData("LIPID", "TCHOL")]
    [InlineData("TSH", "TSH")]
    public void The_parameters_that_have_none_ship_with_none(string test, string code)
    {
        var p = ResultTemplates.Parse(ResultTemplates.For(test))!
            .Parameters.Single(x => x.Code == code);
        Assert.All(p.SafeBands, b =>
        {
            Assert.Null(b.CriticalLow);
            Assert.Null(b.CriticalHigh);
        });
    }

    /// <summary>
    /// A real haemoglobin of 3.1 g/dL on a 34-year-old man, through the same band-matching the
    /// screens use. The male band must win and still carry the panic bound.
    /// </summary>
    [Fact]
    public void An_adult_male_haemoglobin_of_three_point_one_is_critical_low()
    {
        var hb = ResultTemplates.Parse(ResultTemplates.For("CBC"))!
            .Parameters.Single(p => p.Code == "HB");
        var band = hb.BandFor('M', 34);

        Assert.Equal(13.0m, band!.Low);                       // most-specific band still wins
        Assert.Equal(ResultFlags.CriticalLow, ResultTemplates.Flag(3.1m, band));
        Assert.Equal(ResultFlags.Low, ResultTemplates.Flag(11.9m, band));
    }

    // ---- upgrade safety (AC4/AC5) ----------------------------------------------------

    /// <summary>
    /// A template stored before this spec has no critical bounds and no version. It must parse
    /// — not throw — and must be recognised as needing a refresh, or an existing hospital would
    /// silently never gain critical flags.
    /// </summary>
    [Fact]
    public void A_template_stored_before_critical_bounds_parses_and_wants_upgrading()
    {
        const string old = """
            {"Parameters":[{"Code":"HB","Name":"Haemoglobin","Unit":"g/dL",
              "Bands":[{"Sex":null,"AgeFrom":null,"AgeTo":null,"Low":12.0,"High":16.0}]}]}
            """;

        var parsed = ResultTemplates.Parse(old);
        Assert.NotNull(parsed);
        var band = parsed!.Parameters.Single().SafeBands.Single();
        Assert.Null(band.CriticalLow);
        Assert.Null(band.CriticalHigh);
        Assert.Equal(0, parsed.Version);
        Assert.True(ResultTemplates.NeedsUpgrade(old));
    }

    [Fact]
    public void The_current_template_does_not_want_upgrading()
        => Assert.False(ResultTemplates.NeedsUpgrade(ResultTemplates.For("CBC")));

    // ---- the vocabulary the four screens share ---------------------------------------

    [Theory]
    [InlineData(ResultFlags.CriticalLow, true, true)]
    [InlineData(ResultFlags.CriticalHigh, true, true)]
    [InlineData(ResultFlags.Low, false, true)]
    [InlineData(ResultFlags.High, false, true)]
    [InlineData("", false, false)]
    [InlineData(null, false, false)]
    public void Critical_and_abnormal_are_different_questions(string? flag, bool critical, bool abnormal)
    {
        Assert.Equal(critical, ResultFlags.IsCritical(flag));
        Assert.Equal(abnormal, ResultFlags.IsAbnormal(flag));
    }

    /// <summary>A printed report must never show a bare two-letter code to a patient.</summary>
    [Fact]
    public void Every_flag_renders_words_a_human_reads()
    {
        Assert.Equal("CRITICAL LOW", ResultFlags.Label(ResultFlags.CriticalLow));
        Assert.Equal("CRITICAL HIGH", ResultFlags.Label(ResultFlags.CriticalHigh));
        Assert.Equal("Low", ResultFlags.Label(ResultFlags.Low));
        Assert.Equal("High", ResultFlags.Label(ResultFlags.High));
        Assert.Equal("", ResultFlags.Label(""));
    }

    /// <summary>Criticals use the danger token, abnormals the warning one — never the same.</summary>
    [Fact]
    public void A_critical_value_does_not_look_like_a_merely_abnormal_one()
    {
        Assert.NotEqual(ResultFlags.PillClass(ResultFlags.Low),
                        ResultFlags.PillClass(ResultFlags.CriticalLow));
        Assert.Contains("bad", ResultFlags.PillClass(ResultFlags.CriticalHigh));
        Assert.Contains("warn", ResultFlags.PillClass(ResultFlags.High));
    }
}
