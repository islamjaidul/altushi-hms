using Hms.Web.Pages.Registration;

namespace Hms.Web.Tests;

/// <summary>
/// Spec 0032 M1-G1/M1-G2. The two pure parsers behind the ≤ 60-second registration screen
/// (§9A.4) — the front door of the whole product — had no test anywhere. Both are the operator's
/// side of §7 U13: *the machine adapts to what a person types, not the other way round.*
///
/// These are the cheapest possible assertions: no database, no HTTP, no Docker. They live in
/// their own project because `Hms.Web` had no test project at all and the alternative was to
/// hide page-model unit tests inside `Hms.Architecture.Tests`, whose name promises NetArchTest
/// rules.
/// </summary>
public class RegistrationInputTests
{
    // §7 U13's six documented shapes, plus the three ways an operator gives us nothing.
    public static TheoryData<string?, DateOnly?, short?, short?, bool> AgeInputs() => new()
    {
        // input          dob                        years   months  estimated
        { "45",           null,                      (short)45, null,  true  },
        { "45y",          null,                      (short)45, null,  true  },
        { "8 months",     null,                      null, (short)8,   true  },
        { "8 mo",         null,                      null, (short)8,   true  },
        { "8m",           null,                      null, (short)8,   true  },
        { "12/03/1980",   new DateOnly(1980, 3, 12), null,     null,   false },
        { "1980-03-12",   new DateOnly(1980, 3, 12), null,     null,   false },
        // Nothing typed is not an error here — the page decides whether a name-only, unknown
        // identity registration is allowed (edge 25/26). The parser just reports "no age".
        { null,           null,                      null,     null,   false },
        { "",             null,                      null,     null,   false },
        { "   ",          null,                      null,     null,   false },
        { "abc",          null,                      null,     null,   false },
        { "??",           null,                      null,     null,   false },
        // Whitespace around a real answer is the operator's, not their mistake.
        { "  45  ",       null,                      (short)45, null,  true  },
        { " 12/03/1980 ", new DateOnly(1980, 3, 12), null,     null,   false },
        // Spec 0039 WP1 (AUD-VAL-09): a date-shaped string that is NOT a real date must be
        // refused whole — never silently read as the age "31" or "2026". Without the parser's
        // whole-input rule these three came back as ages 31, 2026 and 2026.
        { "31/02/2026",   null,                      null,     null,   false },
        { "2026-13-45",   null,                      null,     null,   false },
        { "2026",         null,                      null,     null,   false },
    };

    [Theory]
    [MemberData(nameof(AgeInputs))]
    public void ParseAge_reads_every_shape_an_operator_types(
        string? input, DateOnly? dob, short? years, short? months, bool estimated)
        => Assert.Equal((dob, years, months, estimated), NewModel.ParseAge(input));

    /// <summary>
    /// Day-first is a decision, not a default: 12/03/1980 is 12 March in Bangladesh, never
    /// 3 December (ADR-0020). A month-first reading would silently misdate a whole population.
    /// </summary>
    [Fact]
    public void ParseAge_reads_a_slashed_date_day_first()
    {
        var (dob, _, _, _) = NewModel.ParseAge("12/03/1980");
        Assert.Equal(3, dob!.Value.Month);
        Assert.Equal(12, dob.Value.Day);
    }

    /// <summary>
    /// A typed age is an estimate by nature (edge 26) and a DOB is not. `AgeEstimated` is what
    /// tells a clinician which of the two they are reading, so it is asserted on every row above
    /// and called out here.
    /// </summary>
    [Fact]
    public void A_typed_age_is_estimated_and_a_date_of_birth_is_not()
    {
        Assert.True(NewModel.ParseAge("45").Estimated);
        Assert.True(NewModel.ParseAge("8 months").Estimated);
        Assert.False(NewModel.ParseAge("12/03/1980").Estimated);
    }

    // LC-REG-05. Every one of these is a real form of the same Bangladeshi mobile number, and
    // the operator types whatever is written on the slip in front of them.
    public static TheoryData<string?, string?> Phones() => new()
    {
        { "01712345999",     "01712-345999" },
        { "01712-345999",    "01712-345999" },
        { "+8801712345999",  "01712-345999" },
        { "8801712345999",   "01712-345999" },
        { "01712 345 999",   "01712-345999" },
        { "0171-234-5999",   "01712-345999" },
        { "  01712345999 ",  "01712-345999" },
        { "(01712) 345999",  "01712-345999" },
        // Nothing typed stays nothing: a phone never blocks a registration (edge 24).
        { null,              null },
        { "",                null },
        { "   ",             null },
        // Not a Bangladeshi mobile: kept verbatim rather than mangled into a shape it is not.
        { "12345",           "12345" },
        { "0212-345678",     "0212-345678" },
    };

    [Theory]
    [MemberData(nameof(Phones))]
    public void NormalizePhone_stores_one_readable_form_of_every_way_it_is_typed(
        string? typed, string? stored)
        => Assert.Equal(stored, NewModel.NormalizePhone(typed));

    /// <summary>
    /// The point of normalising: two operators typing the same number two ways produce one
    /// stored value, so the duplicate guard and the search index see one patient, not two.
    /// </summary>
    [Fact]
    public void Every_form_of_one_number_normalises_to_the_same_stored_value()
    {
        string?[] forms =
        [
            "01712345999", "01712-345999", "+8801712345999", "8801712345999",
            "01712 345 999", "0171-234-5999",
        ];
        Assert.Single(forms.Select(NewModel.NormalizePhone).Distinct());
    }
}

/// <summary>
/// LC-REG-05, the other half. There are two phone normalisers in the product and they are not
/// the same function: <see cref="NewModel.NormalizePhone"/> decides what is *stored*, and
/// <see cref="PatientSearch.PhoneDigitsOf"/> decides what a typed search *matches*. Only the
/// second one is on the path an operator hits when they search, and neither had a test.
/// </summary>
public class PatientSearchPhoneTests
{
    public static TheoryData<string, string?> Terms() => new()
    {
        { "01712345999",    "01712345999" },
        { "01712-345999",   "01712345999" },
        { "+8801712345999", "01712345999" },
        { "8801712345999",  "01712345999" },
        { "01712 345 999",  "01712345999" },
        { "0171-234-5999",  "01712345999" },
        { "345999",         "345999" },      // the tail, as read off a slip
        // Short digit runs are ages and serials, not phones — matching them against every
        // patient's number would make the type-ahead useless (MinPhoneDigits).
        { "45",             null },
        { "8",              null },
        { "Rahim",          null },
        { "",               null },
    };

    [Theory]
    [MemberData(nameof(Terms))]
    public void PhoneDigitsOf_reduces_every_typed_form_to_the_digits_that_are_stored(
        string term, string? digits)
        => Assert.Equal(digits, PatientSearch.PhoneDigitsOf(term));

    /// <summary>
    /// The contract between the two normalisers: whatever `NormalizePhone` stores, the database's
    /// generated `phone_digits` column strips to digits, and a search for any typed form must
    /// reduce to a string that is contained in it. This is the assertion that ties the storage
    /// side to the search side without a database.
    /// </summary>
    [Theory]
    [InlineData("01712345999")]
    [InlineData("01712-345999")]
    [InlineData("+8801712345999")]
    [InlineData("8801712345999")]
    [InlineData("01712 345 999")]
    [InlineData("0171-234-5999")]
    [InlineData("345999")]
    public void Any_typed_form_reduces_into_the_stored_phone_digits(string typed)
    {
        var stored = NewModel.NormalizePhone("01712345999");                  // "01712-345999"
        var storedDigits = new string(stored!.Where(char.IsDigit).ToArray()); // the generated column
        Assert.Contains(PatientSearch.PhoneDigitsOf(typed)!, storedDigits);
    }
}
