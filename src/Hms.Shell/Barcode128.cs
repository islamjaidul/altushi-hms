using System.Text;

namespace Hms.Shell;

/// <summary>
/// Spec 0039 WP6 (AUD-M1-01): a real Code 128 (subsets B/C) encoder, vendored — no package, no
/// network, pure C# to inline SVG. The audit found the printed "barcode" was a fixed CSS
/// gradient, identical on every card and encoding nothing, so the §5 M1 [M] scan workflows
/// could never work from printed output. A keyboard-wedge scanner types exactly the encoded
/// content plus Enter, so anything printed with this feeds the existing search boxes.
/// <para>
/// Symbology facts the tests pin down: every symbol is 11 modules (the stop is 13, including
/// its termination bar); the checksum is <c>(start + Σ position·value) mod 103</c>; digits are
/// packed two-per-symbol in subset C when a run is long enough to pay for the switch.
/// </para>
/// </summary>
public static class Barcode128
{
    public const int StartB = 104;
    public const int StartC = 105;
    public const int CodeB = 100;   // from C back to B
    public const int CodeC = 99;    // from B into C
    public const int Stop = 106;

    /// <summary>Element widths (bar, space, bar, …) for values 0–106; 106 is the stop with its
    /// 2-module termination bar. Standard Code 128 table (ISO/IEC 15417).</summary>
    private static readonly string[] Widths =
    [
        "212222", "222122", "222221", "121223", "121322", "131222", "122213", "122312",
        "132212", "221213", "221312", "231212", "112232", "122132", "122231", "113222",
        "123122", "123221", "223211", "221132", "221231", "213212", "223112", "312131",
        "311222", "321122", "321221", "312212", "322112", "322211", "212123", "212321",
        "232121", "111323", "131123", "131321", "112313", "132113", "132311", "211313",
        "231113", "231311", "112133", "112331", "132131", "113123", "113321", "133121",
        "313121", "211331", "231131", "213113", "213311", "213131", "311123", "311321",
        "331121", "312113", "312311", "332111", "314111", "221411", "431111", "111224",
        "111422", "121124", "121421", "141122", "141221", "112214", "112412", "122114",
        "122411", "142112", "142211", "241211", "221114", "413111", "241112", "134111",
        "111242", "121142", "121241", "114212", "124112", "124211", "411212", "421112",
        "421211", "212141", "214121", "412121", "111143", "111341", "131141", "114113",
        "114311", "411113", "411311", "113141", "114131", "311141", "411131", "211412",
        "211214", "211232", "2331112",
    ];

    /// <summary>Element widths for one symbol value — exposed so tests can assert the start,
    /// checksum and stop patterns of a rendered barcode independently of the SVG.</summary>
    public static string Pattern(int value) => Widths[value];

    /// <summary>Quiet zone either side, in modules (the spec minimum is 10).</summary>
    public const int QuietZone = 10;

    /// <summary>
    /// The full symbol sequence for <paramref name="content"/>: start code, data (with any B↔C
    /// switches), checksum, stop. Deterministic subset choice, so tests can hand-compute:
    /// a digit run uses subset C when it reaches the end of the content with ≥4 digits, or is
    /// ≥6 digits anywhere; an odd-length run spends its first digit in subset B.
    /// </summary>
    public static IReadOnlyList<int> Codewords(string content)
    {
        if (string.IsNullOrEmpty(content))
            throw new ArgumentException("Cannot encode an empty barcode.", nameof(content));
        foreach (var c in content)
            if (c < 32 || c > 126)
                throw new ArgumentException(
                    $"Code 128 subset B cannot encode character U+{(int)c:X4}.", nameof(content));

        var symbols = new List<int>();
        var inC = false;
        var i = 0;
        while (i < content.Length)
        {
            var run = DigitRun(content, i);
            var toEnd = i + run == content.Length;
            var useC = (toEnd && run >= 4) || run >= 6;

            if (useC && run % 2 == 1)
            {
                AppendB(symbols, content[i], ref inC);   // odd run: first digit rides in B
                i++;
                run--;
            }

            if (useC)
            {
                symbols.Add(symbols.Count == 0 ? StartC : CodeC);
                inC = true;
                for (var p = 0; p < run; p += 2, i += 2)
                    symbols.Add((content[i] - '0') * 10 + (content[i + 1] - '0'));
            }
            else
            {
                AppendB(symbols, content[i], ref inC);
                i++;
            }
        }

        // Checksum: start value + each data symbol weighted by its 1-based position, mod 103.
        var checksum = symbols[0];
        for (var p = 1; p < symbols.Count; p++) checksum += p * symbols[p];
        symbols.Add(checksum % 103);
        symbols.Add(Stop);
        return symbols;
    }

    private static void AppendB(List<int> symbols, char c, ref bool inC)
    {
        if (symbols.Count == 0) symbols.Add(StartB);
        else if (inC) symbols.Add(CodeB);
        inC = false;
        symbols.Add(c - 32);
    }

    private static int DigitRun(string s, int from)
    {
        var n = 0;
        while (from + n < s.Length && char.IsAsciiDigit(s[from + n])) n++;
        return n;
    }

    /// <summary>Total width in modules, quiet zones included — the golden-width tests pin this
    /// so a silent change to the encoding is visible.</summary>
    public static int TotalModules(string content)
        => Codewords(content).Sum(v => Widths[v].Sum(w => w - '0')) + 2 * QuietZone;

    /// <summary>
    /// The barcode as inline SVG, in module units with <c>preserveAspectRatio="none"</c> — the
    /// surrounding element sizes it, exactly like the old decorative div. Bars take
    /// <c>currentColor</c>, so ink comes from CSS (tokens, not literals) and prints black.
    /// </summary>
    public static string Svg(string content)
    {
        var symbols = Codewords(content);
        var width = TotalModules(content);
        var svg = new StringBuilder();
        svg.Append("<svg xmlns=\"http://www.w3.org/2000/svg\" viewBox=\"0 0 ").Append(width)
           .Append(" 1\" preserveAspectRatio=\"none\" shape-rendering=\"crispEdges\"")
           .Append(" aria-hidden=\"true\" focusable=\"false\">");

        var x = QuietZone;
        foreach (var value in symbols)
        {
            var bar = true;
            foreach (var w in Widths[value])
            {
                var span = w - '0';
                if (bar)
                    svg.Append("<rect x=\"").Append(x).Append("\" y=\"0\" width=\"").Append(span)
                       .Append("\" height=\"1\" fill=\"currentColor\"/>");
                x += span;
                bar = !bar;
            }
        }
        svg.Append("</svg>");
        return svg.ToString();
    }
}
