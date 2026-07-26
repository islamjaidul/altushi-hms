using System.Text.Json;

namespace Hms.Web;

/// <summary>One measured line on a lab report, with the range its flag is judged against.</summary>
public sealed record ResultParameter(string Code, string Name, string Unit, decimal? Low, decimal? High);

public sealed record ResultTemplate(IReadOnlyList<ResultParameter> Parameters);

/// <summary>
/// Starter parameter templates with reference ranges (02 §2.2). Real ranges are banded by age
/// and sex and arrive with the masters import (spec 0009); these adult ranges are enough for the
/// MVP's manual entry and make the H/L auto-flagging demonstrable on day one.
/// </summary>
public static class ResultTemplates
{
    private static readonly Dictionary<string, ResultParameter[]> Templates = new()
    {
        ["CBC"] =
        [
            new("HB", "Haemoglobin", "g/dL", 13.0m, 17.0m),
            new("TC", "Total WBC count", "/cumm", 4000, 11000),
            new("PLT", "Platelet count", "/cumm", 150000, 450000),
            new("RBC", "RBC count", "mill/cumm", 4.5m, 5.9m),
            new("HCT", "Haematocrit", "%", 40, 52),
            new("NEUT", "Neutrophils", "%", 40, 75),
            new("LYMP", "Lymphocytes", "%", 20, 45),
        ],
        ["ESR"] = [new("ESR", "ESR (Westergren)", "mm in 1st hr", 0, 15)],
        ["RBS"] = [new("RBS", "Random blood sugar", "mmol/L", 4.0m, 7.8m)],
        ["LIPID"] =
        [
            new("TCHOL", "Total cholesterol", "mg/dL", 0, 200),
            new("TG", "Triglycerides", "mg/dL", 0, 150),
            new("HDL", "HDL cholesterol", "mg/dL", 40, 60),
            new("LDL", "LDL cholesterol", "mg/dL", 0, 100),
        ],
        ["SCR"] = [new("SCR", "Serum creatinine", "mg/dL", 0.7m, 1.3m)],
        ["TSH"] = [new("TSH", "TSH", "µIU/mL", 0.4m, 4.0m)],
        ["URINE-RE"] =
        [
            new("ALB", "Albumin", "", null, null),
            new("SUG", "Sugar", "", null, null),
            new("PUS", "Pus cells", "/HPF", 0, 5),
            new("RBCU", "RBC", "/HPF", 0, 2),
        ],
    };

    /// <summary>Imaging and cardiology studies report a narrative, not a parameter grid.</summary>
    public static string? For(string code) =>
        Templates.TryGetValue(code, out var parameters)
            ? JsonSerializer.Serialize(new ResultTemplate(parameters))
            : null;

    public static ResultTemplate? Parse(string? json) =>
        string.IsNullOrWhiteSpace(json) ? null : JsonSerializer.Deserialize<ResultTemplate>(json);

    /// <summary>
    /// §7 U12 / 02 §2.2: the flag is computed from the range that was used, and both are stored
    /// with the value — a range revised next year cannot silently re-flag an old report.
    /// </summary>
    public static string Flag(decimal? value, ResultParameter p)
    {
        if (value is null || p.Low is null || p.High is null) return "";
        if (value < p.Low) return "L";
        if (value > p.High) return "H";
        return "";
    }

    public static string RangeText(ResultParameter p) =>
        p.Low is null || p.High is null ? "—" : $"{p.Low} – {p.High}";
}
