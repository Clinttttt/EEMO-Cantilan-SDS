using System.Globalization;
using System.Text.RegularExpressions;

namespace EEMOCantilanSDS.Application.Common.Revenue;

/// <summary>
/// Mirrors the NPM web registration suggestion: section-scoped, non-reserving, editable.
/// A mixture of prefixes falls back to a plain next number; arbitrary labels are not guessed.
/// </summary>
public static class NpmStallNumberSuggestion
{
    public static string? Next(IEnumerable<string> registeredStallNumbers)
    {
        var values = registeredStallNumbers.Where(x => !string.IsNullOrWhiteSpace(x) &&
            !x.Trim().StartsWith("SP-", StringComparison.OrdinalIgnoreCase)).ToArray();
        var numbers = new List<int>();
        string? commonPrefix = null;
        var samePrefix = true;
        var width = 0;
        foreach (var stall in values)
        {
            var match = Regex.Match(stall.Trim(), @"^(.*?)(\d+)\s*$", RegexOptions.CultureInvariant);
            if (!match.Success || !int.TryParse(match.Groups[2].Value, NumberStyles.None,
                CultureInfo.InvariantCulture, out var number)) continue;
            numbers.Add(number);
            var prefix = match.Groups[1].Value;
            width = Math.Max(width, match.Groups[2].Value.Length);
            if (commonPrefix is null) commonPrefix = prefix;
            else if (!string.Equals(commonPrefix, prefix, StringComparison.OrdinalIgnoreCase)) samePrefix = false;
        }
        if (numbers.Count == 0) return values.Length == 0 ? "1" : null;
        if (numbers.Max() == int.MaxValue) return null;
        var next = (numbers.Max() + 1).ToString(CultureInfo.InvariantCulture)
            .PadLeft(Math.Max(1, width), '0');
        return samePrefix && !string.IsNullOrEmpty(commonPrefix) ? commonPrefix + next : next;
    }
}
