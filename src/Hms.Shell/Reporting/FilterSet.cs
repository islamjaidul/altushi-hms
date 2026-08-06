using Microsoft.AspNetCore.Http;

namespace Hms.Shell.Reporting;

/// <summary>
/// The filters a screen is carrying, as they appear in the URL. Deliberately untyped: the Shell
/// knows that filters are key/value pairs and how to add and remove one; the module knows what
/// "org unit" means and what to call it on a chip.
/// <para>
/// It exists mainly so that a chip's remove link is computed in one place. A chip that removes the
/// wrong filter, or silently drops the period along with it, is the classic report-screen defect.
/// </para>
/// </summary>
public sealed class FilterSet
{
    /// <summary>Keys the period owns. Never treated as filters, never removable by a chip.</summary>
    public static readonly IReadOnlySet<string> PeriodKeys =
        new HashSet<string>(StringComparer.OrdinalIgnoreCase)
            { "p", "d", "u", "from", "to", "cmp", "preset", "step", "run" };

    /// <summary>Keys the grammar owns — paging, sorting and the handler. Not filters either.</summary>
    public static readonly IReadOnlySet<string> ReservedKeys =
        new HashSet<string>(StringComparer.OrdinalIgnoreCase)
            { "sort", "dir", "handler", "key" };

    private readonly List<KeyValuePair<string, string>> _pairs;

    private FilterSet(List<KeyValuePair<string, string>> pairs) => _pairs = pairs;

    public IReadOnlyList<KeyValuePair<string, string>> Pairs => _pairs;

    public bool Any => _pairs.Count > 0;

    public static FilterSet FromRequest(HttpRequest request)
    {
        var pairs = new List<KeyValuePair<string, string>>();
        foreach (var (key, values) in request.Query)
        {
            if (PeriodKeys.Contains(key) || ReservedKeys.Contains(key)) continue;
            foreach (var v in values)
            {
                if (!string.IsNullOrWhiteSpace(v)) pairs.Add(new(key, v!));
            }
        }
        return new FilterSet(pairs);
    }

    public static FilterSet Empty() => new([]);

    public string? One(string key) => _pairs
        .FirstOrDefault(p => string.Equals(p.Key, key, StringComparison.OrdinalIgnoreCase)).Value;

    public long? Id(string key) => long.TryParse(One(key), out var v) ? v : null;

    public IReadOnlyList<string> All(string key) => _pairs
        .Where(p => string.Equals(p.Key, key, StringComparison.OrdinalIgnoreCase))
        .Select(p => p.Value).ToList();

    public IReadOnlyList<long> Ids(string key) => All(key)
        .Select(v => long.TryParse(v, out var n) ? n : (long?)null)
        .Where(n => n.HasValue).Select(n => n!.Value).ToList();

    /// <summary>This set as query pairs, for building a link that keeps the filters.</summary>
    public IEnumerable<KeyValuePair<string, string>> AsQuery() => _pairs;

    /// <summary>The same filters minus one value — a chip's remove target.</summary>
    public IEnumerable<KeyValuePair<string, string>> Without(string key, string value) => _pairs
        .Where(p => !(string.Equals(p.Key, key, StringComparison.OrdinalIgnoreCase)
                      && string.Equals(p.Value, value, StringComparison.Ordinal)));
}
