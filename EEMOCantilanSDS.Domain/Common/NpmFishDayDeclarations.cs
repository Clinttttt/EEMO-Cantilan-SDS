using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;

namespace EEMOCantilanSDS.Domain.Common
{
    /// <summary>
    /// The fish days one online payment covers, and the kilos the payor declared for each of them.
    ///
    /// <para>
    /// A fish-section day costs the stall's daily fee plus that day's weighing fee, so a payment covering several days
    /// cannot be remembered as one figure and a count: each day has to carry its own kilos, or settlement would mark the
    /// days with a weight nobody declared. They are held on the transaction in one text column as
    /// <c>day:kilos</c> pairs ordered by day — <c>26:12.5,27:0,28:3</c> — because the office reads its own tables by hand
    /// when reconciling a payment, and a row it can read beats a blob it cannot.
    /// </para>
    ///
    /// <para>
    /// Kilos are written with the invariant decimal point so a machine's regional settings can never turn 12.5 kg into
    /// 125 kg. Parsing is total: anything it cannot read is dropped rather than throwing, since this text is read back
    /// while settling money that has already been captured, and refusing to parse would strand the payment.
    /// </para>
    /// </summary>
    public static class NpmFishDayDeclarations
    {
        /// <summary>One day of the month and the kilos declared for it.</summary>
        public readonly record struct Declaration(int Day, decimal Kilos);

        /// <summary>
        /// Writes the declarations for storage: ordered by day, one entry per day, invariant decimals. Days outside a
        /// month and negative weights are refused rather than stored, since neither can be settled.
        /// </summary>
        public static string Format(IEnumerable<Declaration> declarations)
        {
            if (declarations is null) throw new ArgumentNullException(nameof(declarations));

            var ordered = declarations
                .Where(d => d.Day >= 1 && d.Day <= 31 && d.Kilos >= 0m)
                .GroupBy(d => d.Day)
                .Select(g => g.First())
                .OrderBy(d => d.Day)
                .ToList();

            if (ordered.Count == 0)
                throw new ArgumentException("A fish-day payment must cover at least one day.", nameof(declarations));

            return string.Join(",", ordered.Select(d =>
                $"{d.Day}:{d.Kilos.ToString(CultureInfo.InvariantCulture)}"));
        }

        /// <summary>
        /// Reads declarations back. Never throws: an entry it cannot read is skipped, so a payment already taken can
        /// still settle the days it can account for.
        /// </summary>
        public static IReadOnlyList<Declaration> Parse(string? stored)
        {
            if (string.IsNullOrWhiteSpace(stored))
                return Array.Empty<Declaration>();

            var declarations = new List<Declaration>();
            foreach (var entry in stored.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
            {
                var parts = entry.Split(':', 2);
                if (parts.Length != 2) continue;
                if (!int.TryParse(parts[0], NumberStyles.Integer, CultureInfo.InvariantCulture, out var day)) continue;
                if (day is < 1 or > 31) continue;
                if (!decimal.TryParse(parts[1], NumberStyles.Number, CultureInfo.InvariantCulture, out var kilos)) continue;
                if (kilos < 0m) continue;

                declarations.Add(new Declaration(day, kilos));
            }

            return declarations
                .GroupBy(d => d.Day)
                .Select(g => g.First())
                .OrderBy(d => d.Day)
                .ToList();
        }
    }
}
