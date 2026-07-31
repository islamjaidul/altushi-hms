using Microsoft.AspNetCore.Razor.TagHelpers;

namespace Hms.Shell.TagHelpers;

/// <summary>
/// The kernel date field (ADR-0020, 05 §3, §7 U13): <c>&lt;input hms-date ... /&gt;</c> renders a
/// forgiving free-text date — <c>12/03/1980</c>, <c>1980-03-12</c>, <c>12 Mar 1980</c> all parse
/// (server-side via <see cref="Hms.Kernel.Time.FlexibleDate"/>) and the field echoes
/// <c>dd MMM yyyy</c> on blur (js/hms-date.js). Never use <c>type="date"</c> — the native picker
/// renders the browser's locale, not the dd/mm convention operators know (CI-enforced).
/// </summary>
[HtmlTargetElement("input", Attributes = "hms-date")]
public sealed class HmsDateTagHelper : TagHelper
{
    /// <summary>Optional CSS selector of a period &lt;select&gt; that editing this date implies
    /// (set to its <c>custom</c> option) — the "dates always win" rule (ADR-0020 #4).</summary>
    [HtmlAttributeName("hms-date-implies")]
    public string? Implies { get; set; }

    public override void Process(TagHelperContext context, TagHelperOutput output)
    {
        output.Attributes.RemoveAll("hms-date");
        output.Attributes.RemoveAll("hms-date-implies");
        output.Attributes.SetAttribute("type", "text");
        output.Attributes.SetAttribute("data-hms-date", "");
        output.Attributes.SetAttribute("autocomplete", "off");
        output.Attributes.SetAttribute("spellcheck", "false");
        if (!context.AllAttributes.ContainsName("placeholder"))
            output.Attributes.SetAttribute("placeholder", "e.g. 12/03/2026");
        if (Implies is not null)
            output.Attributes.SetAttribute("data-implies", Implies);
    }
}
