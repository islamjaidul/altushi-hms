using System.Text.Json;

namespace Hms.Web;

/// <summary>
/// One reference band. A null <paramref name="Sex"/> or age bound means "applies to anyone" —
/// bands are matched most-specific-first, so an adult-male haemoglobin range wins over the
/// general one without the general one having to be deleted.
/// </summary>
public sealed record ReferenceBand(
    char? Sex, short? AgeFrom, short? AgeTo, decimal? Low, decimal? High)
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

public sealed record ResultTemplate(IReadOnlyList<ResultParameter> Parameters);

/// <summary>
/// Starter parameter templates. Haemoglobin, haematocrit and creatinine are genuinely sex-banded
/// in clinical practice; ESR widens with age. The rest carry a single band — correctly, not as a
/// placeholder. Facility-specific ranges arrive with the masters import (spec 0009).
/// </summary>
public static class ResultTemplates
{
    private static ReferenceBand All(decimal low, decimal high) => new(null, null, null, low, high);
    private static ReferenceBand Male(decimal low, decimal high) => new('M', null, null, low, high);
    private static ReferenceBand Female(decimal low, decimal high) => new('F', null, null, low, high);

    private static readonly Dictionary<string, ResultParameter[]> Templates = new()
    {
        ["CBC"] =
        [
            new("HB", "Haemoglobin", "g/dL",
                [All(12.0m, 16.0m), Male(13.0m, 17.0m), Female(12.0m, 15.0m),
                 new('M', null, 12, 11.5m, 15.5m), new('F', null, 12, 11.5m, 15.5m)]),
            new("TC", "Total WBC count", "/cumm",
                [All(4000, 11000), new(null, null, 12, 5000, 15000)]),
            new("PLT", "Platelet count", "/cumm", [All(150000, 450000)]),
            new("RBC", "RBC count", "mill/cumm",
                [All(4.2m, 5.9m), Male(4.5m, 5.9m), Female(4.1m, 5.1m)]),
            new("HCT", "Haematocrit", "%",
                [All(36, 50), Male(40, 52), Female(36, 46)]),
            new("NEUT", "Neutrophils", "%", [All(40, 75)]),
            new("LYMP", "Lymphocytes", "%", [All(20, 45), new(null, null, 12, 30, 60)]),
        ],
        ["ESR"] =
        [
            new("ESR", "ESR (Westergren)", "mm in 1st hr",
                [All(0, 15), Male(0, 15), Female(0, 20), new('M', 50, null, 0, 20), new('F', 50, null, 0, 30)]),
        ],
        ["RBS"] = [new("RBS", "Random blood sugar", "mmol/L", [All(4.0m, 7.8m)])],
        ["LIPID"] =
        [
            new("TCHOL", "Total cholesterol", "mg/dL", [All(0, 200)]),
            new("TG", "Triglycerides", "mg/dL", [All(0, 150)]),
            new("HDL", "HDL cholesterol", "mg/dL", [All(40, 60), Male(40, 60), Female(50, 70)]),
            new("LDL", "LDL cholesterol", "mg/dL", [All(0, 100)]),
        ],
        ["SCR"] =
        [
            new("SCR", "Serum creatinine", "mg/dL",
                [All(0.6m, 1.3m), Male(0.7m, 1.3m), Female(0.6m, 1.1m)]),
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
            ? JsonSerializer.Serialize(new ResultTemplate(parameters))
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

    /// <summary>True when a stored template predates reference bands and should be refreshed.</summary>
    public static bool NeedsUpgrade(string? json)
    {
        var t = Parse(json);
        return t is not null && t.Parameters.Count > 0 && t.Parameters.All(p => p.SafeBands.Count == 0);
    }

    /// <summary>
    /// §7 U12 / 02 §2.2: the flag comes from the band actually used, and both are stored with
    /// the value — a range revised later cannot silently re-flag an already-released report.
    /// </summary>
    public static string Flag(decimal? value, ReferenceBand? band)
    {
        if (value is null || band?.Low is null || band.High is null) return "";
        if (value < band.Low) return "L";
        if (value > band.High) return "H";
        return "";
    }
}
