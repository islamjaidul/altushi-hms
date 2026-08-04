using System.Text.Json;

namespace Hms.Web;

/// <summary>
/// One reference band. A null <paramref name="Sex"/> or age bound means "applies to anyone" —
/// bands are matched most-specific-first, so an adult-male haemoglobin range wins over the
/// general one without the general one having to be deleted.
/// <para>
/// <paramref name="CriticalLow"/> / <paramref name="CriticalHigh"/> are the panic bounds
/// (spec 0044, §5 M9 [M]). They are <b>appended</b> to the positional record deliberately:
/// templates stored before this existed deserialise with both null and keep behaving exactly as
/// they did. Null means this parameter has no critical value — which is the correct answer for
/// an ESR or a cholesterol, not an omission.
/// </para>
/// </summary>
public sealed record ReferenceBand(
    char? Sex, short? AgeFrom, short? AgeTo, decimal? Low, decimal? High,
    decimal? CriticalLow = null, decimal? CriticalHigh = null)
{
    public bool Matches(char sex, short? ageYears)
    {
        if (Sex is { } s && char.ToUpperInvariant(s) != char.ToUpperInvariant(sex)) return false;
        if (AgeFrom is { } from && (ageYears is null || ageYears < from)) return false;
        if (AgeTo is { } to && (ageYears is null || ageYears > to)) return false;
        return true;
    }

    /// <summary>More constraints satisfied = a better match; used to order candidates.</summary>
    public int Specificity => (Sex is null ? 0 : 2) + (AgeFrom is null && AgeTo is null ? 0 : 1);

    public string Text => Low is null || High is null ? "—" : $"{Low} – {High}";

    public string Label
    {
        get
        {
            var parts = new List<string>();
            if (Sex is { } s) parts.Add(char.ToUpperInvariant(s) == 'M' ? "male" : "female");
            if (AgeFrom is { } f && AgeTo is { } t) parts.Add($"{f}–{t} y");
            else if (AgeFrom is { } f2) parts.Add($"{f2}+ y");
            else if (AgeTo is { } t2) parts.Add($"under {t2 + 1} y");
            return parts.Count == 0 ? "all patients" : string.Join(", ", parts);
        }
    }
}

/// <summary>One measured line on a lab report, with the bands its flag is judged against.</summary>
public sealed record ResultParameter(
    string Code, string Name, string Unit, IReadOnlyList<ReferenceBand>? Bands)
{
    /// <summary>
    /// A template written before bands existed deserialises with a null list. Treat it as
    /// "no reference range" rather than throwing — an upgraded database must keep working,
    /// and an unbanded parameter is still enterable, just unflagged.
    /// </summary>
    public IReadOnlyList<ReferenceBand> SafeBands => Bands ?? [];

    /// <summary>§5 M9 [M]: "reference ranges by age/sex". The chosen band is stored with the
    /// value, so a range revised next year cannot re-flag a report issued today (edge 22).</summary>
    public ReferenceBand? BandFor(char sex, short? ageYears) =>
        SafeBands.Where(b => b.Matches(sex, ageYears))
             .OrderByDescending(b => b.Specificity)
             .FirstOrDefault();
}

/// <summary>
/// <paramref name="Version"/> is what lets an already-seeded database notice that the shipped
/// templates have moved on (spec 0044). A template written before versioning deserialises as 0,
/// which is below <see cref="ResultTemplates.CurrentVersion"/>, so it is refreshed on next boot.
/// </summary>
public sealed record ResultTemplate(IReadOnlyList<ResultParameter> Parameters, int Version = 0);

/// <summary>
/// Starter parameter templates. Haemoglobin, haematocrit and creatinine are genuinely sex-banded
/// in clinical practice; ESR widens with age. The rest carry a single band — correctly, not as a
/// placeholder. Facility-specific ranges arrive with the masters import (spec 0009).
/// </summary>
public static class ResultTemplates
{
    /// <summary>
    /// Bumped whenever the shipped templates change, so <see cref="NeedsUpgrade"/> can tell a
    /// database seeded by an older build. Version 1 added the critical bounds (spec 0044).
    /// </summary>
    public const int CurrentVersion = 1;

    private static ReferenceBand All(decimal low, decimal high, decimal? cl = null, decimal? ch = null)
        => new(null, null, null, low, high, cl, ch);
    private static ReferenceBand Male(decimal low, decimal high, decimal? cl = null, decimal? ch = null)
        => new('M', null, null, low, high, cl, ch);
    private static ReferenceBand Female(decimal low, decimal high, decimal? cl = null, decimal? ch = null)
        => new('F', null, null, low, high, cl, ch);

    // Critical (panic) bounds below are WIDELY-USED DEFAULTS, not clinical authority. A lab's
    // critical list is part of its own accreditation and its pathologist must review these
    // before go-live; they are shipped so the mechanism is live and reviewable, not so a vendor
    // decides when a hospital telephones a clinician. Parameters with no medically standard
    // panic value — ESR, lipids, TSH, urine microscopy — deliberately carry none.
    private static readonly Dictionary<string, ResultParameter[]> Templates = new()
    {
        ["CBC"] =
        [
            new("HB", "Haemoglobin", "g/dL",
                [All(12.0m, 16.0m, cl: 7.0m, ch: 20.0m),
                 Male(13.0m, 17.0m, cl: 7.0m, ch: 20.0m),
                 Female(12.0m, 15.0m, cl: 7.0m, ch: 20.0m),
                 new('M', null, 12, 11.5m, 15.5m, 7.0m, 20.0m),
                 new('F', null, 12, 11.5m, 15.5m, 7.0m, 20.0m)]),
            new("TC", "Total WBC count", "/cumm",
                [All(4000, 11000, cl: 2000, ch: 30000),
                 new(null, null, 12, 5000, 15000, 2000, 30000)]),
            new("PLT", "Platelet count", "/cumm", [All(150000, 450000, cl: 50000, ch: 1000000)]),
            new("RBC", "RBC count", "mill/cumm",
                [All(4.2m, 5.9m), Male(4.5m, 5.9m), Female(4.1m, 5.1m)]),
            new("HCT", "Haematocrit", "%",
                [All(36, 50, cl: 20, ch: 60), Male(40, 52, cl: 20, ch: 60),
                 Female(36, 46, cl: 20, ch: 60)]),
            new("NEUT", "Neutrophils", "%", [All(40, 75)]),
            new("LYMP", "Lymphocytes", "%", [All(20, 45), new(null, null, 12, 30, 60)]),
        ],
        ["ESR"] =
        [
            new("ESR", "ESR (Westergren)", "mm in 1st hr",
                [All(0, 15), Male(0, 15), Female(0, 20), new('M', 50, null, 0, 20), new('F', 50, null, 0, 30)]),
        ],
        // mmol/L, not mg/dL — the panic bounds have to be in the parameter's own unit.
        ["RBS"] = [new("RBS", "Random blood sugar", "mmol/L", [All(4.0m, 7.8m, cl: 2.8m, ch: 22.2m)])],
        ["LIPID"] =
        [
            new("TCHOL", "Total cholesterol", "mg/dL", [All(0, 200)]),
            new("TG", "Triglycerides", "mg/dL", [All(0, 150)]),
            new("HDL", "HDL cholesterol", "mg/dL", [All(40, 60), Male(40, 60), Female(50, 70)]),
            new("LDL", "LDL cholesterol", "mg/dL", [All(0, 100)]),
        ],
        // Creatinine has a standard panic HIGH and no meaningful panic low — a null bound is a
        // clinical statement here, not a gap.
        ["SCR"] =
        [
            new("SCR", "Serum creatinine", "mg/dL",
                [All(0.6m, 1.3m, ch: 5.0m), Male(0.7m, 1.3m, ch: 5.0m),
                 Female(0.6m, 1.1m, ch: 5.0m)]),
        ],
        ["TSH"] = [new("TSH", "TSH", "µIU/mL", [All(0.4m, 4.0m)])],
        ["URINE-RE"] =
        [
            new("ALB", "Albumin", "", [new(null, null, null, null, null)]),
            new("SUG", "Sugar", "", [new(null, null, null, null, null)]),
            new("PUS", "Pus cells", "/HPF", [All(0, 5)]),
            new("RBCU", "RBC", "/HPF", [All(0, 2)]),
        ],
    };

    /// <summary>Imaging and cardiology studies report a narrative, not a parameter grid.</summary>
    public static string? For(string code) =>
        Templates.TryGetValue(code, out var parameters)
            ? JsonSerializer.Serialize(new ResultTemplate(parameters, CurrentVersion))
            : null;

    public static ResultTemplate? Parse(string? json)
    {
        if (string.IsNullOrWhiteSpace(json)) return null;
        try
        {
            var parsed = JsonSerializer.Deserialize<ResultTemplate>(json);
            if (parsed?.Parameters is null) return null;
            // Normalise on the way in so no caller has to think about the older shape.
            return parsed with
            {
                Parameters = parsed.Parameters
                    .Select(p => p.Bands is null ? p with { Bands = [] } : p).ToList(),
            };
        }
        catch (JsonException) { return null; }   // a hand-edited template must not break the bench
    }

    /// <summary>
    /// True when a stored template is older than what this build ships and should be refreshed.
    /// <para>
    /// Two reasons, both real: a template written before reference bands existed carries none at
    /// all, and one written before spec 0044 carries no critical bounds. The version check covers
    /// the second and every future change; the band check stays because templates predating
    /// versioning report version 0 whether or not they have bands, and the original condition is
    /// what the older upgrade path was proven against.
    /// </para>
    /// </summary>
    public static bool NeedsUpgrade(string? json)
    {
        var t = Parse(json);
        if (t is null || t.Parameters.Count == 0) return false;
        if (t.Version < CurrentVersion) return true;
        return t.Parameters.All(p => p.SafeBands.Count == 0);
    }

    /// <summary>
    /// §7 U12 / 02 §2.2: the flag comes from the band actually used, and both are stored with
    /// the value — a range revised later cannot silently re-flag an already-released report.
    /// <para>
    /// Critical is judged <b>before</b> abnormal (spec 0044): a haemoglobin of 3.1 is both low
    /// and critically low, and the only useful thing to say about it is the second one. Bounds
    /// are inclusive because a lab's critical list is written as "≤ 7.0 g/dL", and an exclusive
    /// test would let exactly 7.0 through as merely low.
    /// </para>
    /// </summary>
    public static string Flag(decimal? value, ReferenceBand? band)
    {
        if (value is null || band is null) return "";
        // Critical bounds stand on their own: a parameter may have a panic value defined even
        // where its normal range is not, and vice versa.
        if (band.CriticalLow is { } cl && value <= cl) return ResultFlags.CriticalLow;
        if (band.CriticalHigh is { } ch && value >= ch) return ResultFlags.CriticalHigh;
        if (band.Low is null || band.High is null) return "";
        if (value < band.Low) return ResultFlags.Low;
        if (value > band.High) return ResultFlags.High;
        return "";
    }
}

/// <summary>
/// The flag vocabulary, in one place (spec 0044). Four screens render these — result entry,
/// verification, the printed report and the EMR's inline view — and before this they each
/// compared against string literals, so adding a value meant finding every literal.
/// </summary>
public static class ResultFlags
{
    public const string Low = "L";
    public const string High = "H";
    public const string CriticalLow = "CL";
    public const string CriticalHigh = "CH";

    public static bool IsCritical(string? flag) => flag is CriticalLow or CriticalHigh;
    public static bool IsAbnormal(string? flag) => !string.IsNullOrEmpty(flag);

    /// <summary>What the operator reads. Never an unexplained two-letter code on a report.</summary>
    public static string Label(string? flag) => flag switch
    {
        CriticalLow => "CRITICAL LOW",
        CriticalHigh => "CRITICAL HIGH",
        Low => "Low",
        High => "High",
        _ => "",
    };

    /// <summary>Short badge text for a dense grid, where the full label will not fit.</summary>
    public static string Short(string? flag) => flag switch
    {
        CriticalLow => "C-LOW",
        CriticalHigh => "C-HIGH",
        Low => "Low",
        High => "High",
        _ => "",
    };

    /// <summary>Existing token classes (`eng/check-ui-tokens.sh`) — no new colours invented.</summary>
    public static string PillClass(string? flag) => flag switch
    {
        CriticalLow or CriticalHigh => "pill bad",
        Low or High => "pill warn",
        _ => "pill",
    };
}
