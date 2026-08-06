using Hms.Hr;
using Hms.Hr.Data;

namespace Hms.Web.Tests;

/// <summary>
/// Spec 0056 — letter rendering, nominee arithmetic, expiry wording and anniversary dates. All pure,
/// so they run in milliseconds and cover the parts that would otherwise only be exercised by
/// clicking through a screen.
/// </summary>
public class LetterRenderTests
{
    private static LetterFacts Facts(string? gross = null) => new()
    {
        EmployeeName = "Nasrin Akter",
        EmployeeCode = "EMP-00042",
        Designation = "Senior Staff Nurse",
        Department = "Nursing",
        JoinedOn = "01 July 2021",
        OrganisationName = "Sylhet Evergreen Hospital",
        Today = "06 August 2026",
        LetterNo = "LTR-2026-27-0001",
        GrossSalary = gross,
    };

    [Fact]
    public void Tokens_are_substituted()
    {
        var body = LetterService.Render(
            "Dear {{employee.name}} ({{employee.code}}), of {{org.name}}.", Facts());

        Assert.Equal("Dear Nasrin Akter (EMP-00042), of Sylhet Evergreen Hospital.", body);
    }

    [Fact]
    public void Whitespace_inside_a_token_is_tolerated()
    {
        // An operator editing a template in a textarea will do this eventually.
        Assert.Equal("Nasrin Akter", LetterService.Render("{{ employee.name }}", Facts()));
    }

    /// <summary>
    /// The load-bearing choice: an unresolvable token stays <b>visible</b>. A confirmation letter
    /// that silently drops the joining date reads as finished and is wrong; one still showing
    /// <c>{{employee.confirmed_on}}</c> is obviously not ready to sign.
    /// </summary>
    [Fact]
    public void A_value_the_product_does_not_have_is_left_visible()
    {
        var body = LetterService.Render("Confirmed on {{employee.confirmed_on}}.", Facts());

        Assert.Equal("Confirmed on {{employee.confirmed_on}}.", body);
        Assert.Contains("employee.confirmed_on", LetterService.Unresolved(body));
    }

    [Fact]
    public void An_unknown_token_is_left_visible_too()
    {
        var body = LetterService.Render("Hello {{employee.favourite_colour}}.", Facts());

        Assert.Contains("{{employee.favourite_colour}}", body);
    }

    /// <summary>
    /// D6 expressed as a rendering fact: the salary tokens are populated only for a reader who holds
    /// salary read, so a body that mentions pay in a context that must not reveal it prints the
    /// placeholder rather than the figure.
    /// </summary>
    [Fact]
    public void A_salary_token_renders_unresolved_when_the_issuer_may_not_see_pay()
    {
        var withheld = LetterService.Render("Gross BDT {{employee.gross}}.", Facts(gross: null));
        var permitted = LetterService.Render("Gross BDT {{employee.gross}}.", Facts(gross: "45,000"));

        Assert.Contains("{{employee.gross}}", withheld);
        Assert.DoesNotContain("45,000", withheld);
        Assert.Equal("Gross BDT 45,000.", permitted);
    }

    [Fact]
    public void Unresolved_reports_each_token_once()
    {
        var gaps = LetterService.Unresolved("{{a.b}} {{a.b}} {{c.d}}");

        Assert.Equal(2, gaps.Count);
    }

    [Fact]
    public void Every_starter_template_uses_only_tokens_the_product_can_fill()
    {
        // A starter offering a token the save path rejects would be an editor that cannot save
        // what it just suggested.
        var known = LetterService.Tokens.Select(t => t.Token).ToHashSet(StringComparer.Ordinal);

        foreach (var kind in LetterKind.All)
        {
            var starter = LetterService.Starter(kind);
            foreach (var token in LetterService.Unresolved(starter))
                Assert.True(known.Contains(token), $"{kind} starter uses unknown token {token}");
        }
    }

    [Fact]
    public void Only_the_letters_that_state_pay_are_salary_bearing()
    {
        Assert.True(LetterKind.SalaryBearing(LetterKind.SalaryCertificate));
        Assert.True(LetterKind.SalaryBearing(LetterKind.Appointment));
        Assert.True(LetterKind.SalaryBearing(LetterKind.Increment));

        Assert.False(LetterKind.SalaryBearing(LetterKind.Experience));
        Assert.False(LetterKind.SalaryBearing(LetterKind.Relieving));
        Assert.False(LetterKind.SalaryBearing(LetterKind.Noc));
    }

    [Fact]
    public void A_salary_bearing_starter_is_the_only_kind_that_mentions_a_salary_token()
    {
        // Otherwise an experience certificate would print a placeholder for a figure it must never
        // carry — the leak D6 exists to prevent, arriving through the template rather than the code.
        foreach (var kind in LetterKind.All.Where(k => !LetterKind.SalaryBearing(k)))
        {
            var starter = LetterService.Starter(kind);
            Assert.DoesNotContain("employee.gross", starter);
            Assert.DoesNotContain("employee.basic", starter);
        }
    }
}

public class NomineeAndExpiryTests
{
    [Theory]
    [InlineData(0, "nobody is nominated")]
    [InlineData(5_000, "50% — 50% is unallocated")]
    [InlineData(12_000, "120%, which is 20% too much")]
    public void An_incomplete_nomination_is_described_in_the_operators_words(int totalBp, string expected)
    {
        var complaint = EmployeeFileService.ShareComplaint(NomineePurpose.Gratuity, totalBp);

        Assert.NotNull(complaint);
        Assert.Contains(expected, complaint);
    }

    [Fact]
    public void A_complete_nomination_has_nothing_to_say()
    {
        Assert.Null(EmployeeFileService.ShareComplaint(
            NomineePurpose.ProvidentFund, EmployeeFileService.WholeShareBp));
    }

    [Fact]
    public void The_complaint_names_which_benefit_is_short()
    {
        Assert.StartsWith("Provident fund",
            EmployeeFileService.ShareComplaint(NomineePurpose.ProvidentFund, 0));
        Assert.StartsWith("Gratuity",
            EmployeeFileService.ShareComplaint(NomineePurpose.Gratuity, 0));
    }

    private static readonly DateOnly Today = new(2026, 8, 6);

    [Fact]
    public void A_document_with_no_expiry_says_so_rather_than_warning()
    {
        var (tone, words) = EmployeeFileService.ExpiryState(null, Today, 45);

        Assert.Equal("muted", tone);
        Assert.Equal("No expiry", words);
    }

    [Theory]
    [InlineData("2026-08-05", "bad", "1 day ago")]
    [InlineData("2026-06-06", "bad", "61 days ago")]
    [InlineData("2026-08-06", "bad", "Expires today")]
    [InlineData("2026-08-07", "warn", "Expires in 1 day")]
    [InlineData("2026-09-20", "warn", "Expires in 45 days")]
    [InlineData("2026-09-21", "good", "Valid to 21 Sep 2026")]
    public void Expiry_reads_the_same_everywhere_it_appears(string expires, string tone, string words)
    {
        var state = EmployeeFileService.ExpiryState(DateOnly.Parse(expires), Today, 45);

        Assert.Equal(tone, state.Tone);
        Assert.Contains(words, state.Words);
    }
}

public class AnniversaryDateTests
{
    [Fact]
    public void An_anniversary_later_this_year_is_this_years()
    {
        Assert.Equal(new DateOnly(2026, 12, 25),
            HrAlertService.NextOccurrence(new DateOnly(1990, 12, 25), new DateOnly(2026, 8, 6)));
    }

    [Fact]
    public void An_anniversary_already_past_rolls_to_next_year()
    {
        Assert.Equal(new DateOnly(2027, 3, 1),
            HrAlertService.NextOccurrence(new DateOnly(1990, 3, 1), new DateOnly(2026, 8, 6)));
    }

    [Fact]
    public void Today_counts_as_the_next_occurrence()
    {
        Assert.Equal(new DateOnly(2026, 8, 6),
            HrAlertService.NextOccurrence(new DateOnly(1990, 8, 6), new DateOnly(2026, 8, 6)));
    }

    /// <summary>
    /// 29 February lands on 1 March in a common year. Stated as a decision rather than left to
    /// arithmetic, because the alternative — clamping to the 28th — silently moves a person's
    /// birthday, and skipping it entirely misses it three years in four.
    /// </summary>
    [Fact]
    public void A_leap_day_birthday_falls_on_the_first_of_March_in_a_common_year()
    {
        Assert.Equal(new DateOnly(2026, 3, 1),
            HrAlertService.NextOccurrence(new DateOnly(2000, 2, 29), new DateOnly(2026, 1, 1)));

        Assert.Equal(new DateOnly(2028, 2, 29),
            HrAlertService.NextOccurrence(new DateOnly(2000, 2, 29), new DateOnly(2028, 1, 1)));
    }

    [Fact]
    public void A_thirty_first_falls_on_the_last_day_of_a_shorter_month()
    {
        Assert.Equal(new DateOnly(2027, 4, 30),
            HrAlertService.NextOccurrence(new DateOnly(2020, 4, 30), new DateOnly(2026, 8, 6)));
    }
}

public class EmploymentTypeTests
{
    [Fact]
    public void Only_a_contract_is_required_to_carry_an_end_date()
    {
        Assert.True(EmploymentType.NeedsEndDate(EmploymentType.Contract));

        foreach (var other in EmploymentType.All.Where(t => t != EmploymentType.Contract))
            Assert.False(EmploymentType.NeedsEndDate(other));
    }

    [Fact]
    public void Every_type_has_a_label_an_operator_would_recognise()
    {
        foreach (var t in EmploymentType.All)
        {
            var label = EmploymentType.Label(t);
            Assert.False(string.IsNullOrWhiteSpace(label));
            Assert.DoesNotContain('_', label);
        }
    }

    [Fact]
    public void Contract_end_and_death_land_on_their_own_statuses()
    {
        // Both used to fall through to "resigned", which is not what either of them is.
        Assert.Equal(EmploymentStatus.ContractEnded,
            EmployeeService.TerminalStatusFor(EmploymentEventKind.ContractEnded));
        Assert.Equal(EmploymentStatus.Deceased,
            EmployeeService.TerminalStatusFor(EmploymentEventKind.Deceased));
        Assert.Equal(EmploymentStatus.Retired,
            EmployeeService.TerminalStatusFor(EmploymentEventKind.Retired));
        Assert.Equal(EmploymentStatus.Terminated,
            EmployeeService.TerminalStatusFor(EmploymentEventKind.Terminated));
        Assert.Equal(EmploymentStatus.Resigned,
            EmployeeService.TerminalStatusFor(EmploymentEventKind.Resigned));
    }

    [Fact]
    public void Every_separation_kind_maps_to_an_employment_event()
    {
        foreach (var kind in SeparationKind.All)
        {
            var eventKind = SeparationService.EventKindFor(kind);
            Assert.False(string.IsNullOrWhiteSpace(eventKind));
        }

        Assert.Equal(EmploymentEventKind.ContractEnded,
            SeparationService.EventKindFor(SeparationKind.ContractEnd));
        Assert.Equal(EmploymentEventKind.Deceased,
            SeparationService.EventKindFor(SeparationKind.Death));
    }

    [Fact]
    public void The_employer_initiated_kinds_are_the_ones_that_pay_in_lieu()
    {
        Assert.True(SeparationKind.EmployerInitiated(SeparationKind.Termination));
        Assert.True(SeparationKind.EmployerInitiated(SeparationKind.ContractEnd));
        Assert.False(SeparationKind.EmployerInitiated(SeparationKind.Resignation));
        Assert.False(SeparationKind.EmployerInitiated(SeparationKind.Retirement));
    }
}
